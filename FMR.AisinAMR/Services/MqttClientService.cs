using System;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using Newtonsoft.Json;

namespace FMR.AisinAMR.Services
{
    public class MqttClientService : IAsyncDisposable
    {
        private readonly string _brokerHost;
        private readonly int _brokerPort;
        private IManagedMqttClient? _client;

        public event EventHandler<bool>? ConnectionStatusChanged;
        public event EventHandler<MqttMessageEvent>? MessageReceived;

        public MqttClientService(string brokerHost = "localhost", int brokerPort = 1883)
        {
            _brokerHost = brokerHost;
            _brokerPort = brokerPort;
        }

        public async Task StartAsync()
        {
            if (_client is not null)
                return;

            var factory = new MqttFactory();
            _client = factory.CreateManagedMqttClient();

            _client.ConnectedAsync += _ =>
            {
                ConnectionStatusChanged?.Invoke(this, true);
                return Task.CompletedTask;
            };

            _client.DisconnectedAsync += _ =>
            {
                ConnectionStatusChanged?.Invoke(this, false);
                return Task.CompletedTask;
            };

            _client.ApplicationMessageReceivedAsync += async e =>
            {
                var message = new MqttMessageEvent
                {
                    Topic = e.ApplicationMessage.Topic ?? string.Empty,
                    Payload = e.ApplicationMessage.ConvertPayloadToString(),
                    ClientId = e.ClientId ?? string.Empty,
                    Timestamp = DateTime.Now
                };

                MessageReceived?.Invoke(this, message);
                await Task.CompletedTask;
            };

            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId($"fmramr-ui-{Guid.NewGuid():N}")
                .WithTcpServer(_brokerHost, _brokerPort)
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(clientOptions)
                .Build();

            await _client.StartAsync(managedOptions);
        }

        public async Task SubscribeAsync(string topicFilter, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_client is null)
                throw new InvalidOperationException("MQTT client has not been started.");

            await _client.SubscribeAsync(new[]
            {
                new MqttTopicFilterBuilder()
                    .WithTopic(topicFilter)
                    .WithQualityOfServiceLevel(qos)
                    .Build()
            });
        }

        public async Task PublishAsync(string topic, object payload, bool retain = false)
        {
            if (_client is null)
                throw new InvalidOperationException("MQTT client has not been started.");

            var json = payload is string s ? s : JsonConvert.SerializeObject(payload);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retain)
                .Build();

            await _client.EnqueueAsync(message);
        }

        public async Task StopAsync()
        {
            if (_client is null)
                return;

            await _client.StopAsync();
            if (_client is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_client is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _client = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_client is null)
                return;

            await _client.StopAsync();
            if (_client is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_client is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _client = null;
        }
    }
}
