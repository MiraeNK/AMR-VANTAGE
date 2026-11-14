using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace Vantage.Fleet.Transport;

public delegate void MessageHandler(string topic, string payload);

/// <summary>Publish/subscribe transport. MQTT for live operation, in-process for reproducible experiments.</summary>
public interface IMessageBus : IAsyncDisposable
{
    string ClientId { get; }
    bool IsConnected { get; }
    Task PublishAsync(string topic, string payload, bool retain = false);
    Task SubscribeAsync(string topicFilter, MessageHandler handler);
}

public static class TopicFilter
{
    /// <summary>MQTT wildcard match (+ single level, # multi level).</summary>
    public static bool Matches(string filter, string topic)
    {
        var f = filter.Split('/');
        var t = topic.Split('/');
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] == "#") return true;
            if (i >= t.Length) return false;
            if (f[i] != "+" && f[i] != t[i]) return false;
        }
        return f.Length == t.Length;
    }
}

/// <summary>Deterministic synchronous hub: every publish is delivered before PublishAsync returns.</summary>
public sealed class InProcessHub
{
    private readonly List<(string Filter, MessageHandler Handler, string Client)> _subs = new();
    private readonly Dictionary<string, string> _retained = new();
    private readonly object _gate = new();

    public event Action<string, string, string>? Published;

    public IMessageBus Connect(string clientId) => new Client(this, clientId);

    private void Publish(string client, string topic, string payload, bool retain)
    {
        (string, MessageHandler, string)[] targets;
        lock (_gate)
        {
            if (retain) _retained[topic] = payload;
            targets = _subs.Where(s => TopicFilter.Matches(s.Filter, topic)).ToArray();
        }
        Published?.Invoke(client, topic, payload);
        foreach (var (_, h, _) in targets) h(topic, payload);
    }

    private void Subscribe(string client, string filter, MessageHandler handler)
    {
        KeyValuePair<string, string>[] retained;
        lock (_gate)
        {
            _subs.Add((filter, handler, client));
            retained = _retained.Where(kv => TopicFilter.Matches(filter, kv.Key)).ToArray();
        }
        foreach (var kv in retained) handler(kv.Key, kv.Value);
    }

    private sealed class Client(InProcessHub hub, string id) : IMessageBus
    {
        public string ClientId => id;
        public bool IsConnected => true;

        public Task PublishAsync(string topic, string payload, bool retain = false)
        {
            hub.Publish(id, topic, payload, retain);
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(string topicFilter, MessageHandler handler)
        {
            hub.Subscribe(id, topicFilter, handler);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (hub._gate) hub._subs.RemoveAll(s => s.Client == id);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>MQTT 3.1.1 client with automatic reconnect (QoS 1).</summary>
public sealed class MqttBus : IMessageBus
{
    private readonly IManagedMqttClient _client;
    private readonly List<(string Filter, MessageHandler Handler)> _handlers = new();
    private readonly string _host;
    private readonly int _port;
    private readonly (string Topic, string Payload)? _will;

    public MqttBus(string clientId, string host = "127.0.0.1", int port = 1883, (string Topic, string Payload)? lastWill = null)
    {
        ClientId = clientId;
        _host = host;
        _port = port;
        _will = lastWill;
        _client = new MqttFactory().CreateManagedMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString() ?? "";
            MessageHandler[] hs;
            lock (_handlers) hs = _handlers.Where(h => TopicFilter.Matches(h.Filter, topic)).Select(h => h.Handler).ToArray();
            foreach (var h in hs) h(topic, payload);
            return Task.CompletedTask;
        };
        _client.ConnectedAsync += _ => { ConnectionChanged?.Invoke(true); return Task.CompletedTask; };
        _client.DisconnectedAsync += _ => { ConnectionChanged?.Invoke(false); return Task.CompletedTask; };
    }

    public string ClientId { get; }
    public bool IsConnected => _client.IsConnected;
    public event Action<bool>? ConnectionChanged;

    public async Task StartAsync()
    {
        var b = new MqttClientOptionsBuilder()
            .WithClientId(ClientId)
            .WithTcpServer(_host, _port)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(5))
            .WithCleanSession();
        if (_will is { } w)
            b = b.WithWillTopic(w.Topic).WithWillPayload(w.Payload).WithWillRetain().WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        var options = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(b.Build())
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(1))
            .Build();
        await _client.StartAsync(options);
    }

    public Task PublishAsync(string topic, string payload, bool retain = false) =>
        _client.EnqueueAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build());

    public async Task SubscribeAsync(string topicFilter, MessageHandler handler)
    {
        lock (_handlers) _handlers.Add((topicFilter, handler));
        await _client.SubscribeAsync(topicFilter, MqttQualityOfServiceLevel.AtLeastOnce);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.StopAsync();
        _client.Dispose();
    }
}
