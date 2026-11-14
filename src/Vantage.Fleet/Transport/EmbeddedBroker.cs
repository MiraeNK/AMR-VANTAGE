using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;

namespace Vantage.Fleet.Transport;

/// <summary>MQTT broker hosted inside the FMR process (port 1883 on all interfaces).</summary>
public sealed class EmbeddedBroker : IAsyncDisposable
{
    private MqttServer? _server;
    private readonly ConcurrentDictionary<string, DateTime> _clients = new();
    private long _messages;

    public EmbeddedBroker(int port = 1883) => Port = port;

    public int Port { get; }
    public bool IsRunning { get; private set; }
    public IReadOnlyCollection<string> Clients => _clients.Keys.ToList();
    public long MessageCount => Interlocked.Read(ref _messages);

    public event Action<bool>? StatusChanged;
    public event Action<string, bool>? ClientChanged;

    public async Task StartAsync()
    {
        if (IsRunning) return;
        if (!PortFree(Port))
            throw new InvalidOperationException($"TCP port {Port} is already in use. Stop the other broker (e.g. Mosquitto) or change the port.");

        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Any)
            .WithDefaultEndpointPort(Port)
            .Build();

        _server = new MqttFactory().CreateMqttServer(options);
        _server.ClientConnectedAsync += e =>
        {
            _clients[e.ClientId] = DateTime.Now;
            ClientChanged?.Invoke(e.ClientId, true);
            return Task.CompletedTask;
        };
        _server.ClientDisconnectedAsync += e =>
        {
            _clients.TryRemove(e.ClientId, out _);
            ClientChanged?.Invoke(e.ClientId, false);
            return Task.CompletedTask;
        };
        _server.InterceptingPublishAsync += _ =>
        {
            Interlocked.Increment(ref _messages);
            return Task.CompletedTask;
        };

        await _server.StartAsync();
        IsRunning = true;
        StatusChanged?.Invoke(true);
    }

    public async Task StopAsync()
    {
        if (_server is null || !IsRunning) return;
        await _server.StopAsync();
        IsRunning = false;
        _clients.Clear();
        StatusChanged?.Invoke(false);
    }

    private static bool PortFree(int port)
    {
        try
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _server?.Dispose();
    }
}
