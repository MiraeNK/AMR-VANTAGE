using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json;

namespace FMR.AisinAMR.Services
{
    /// <summary>
    /// Embedded MQTT Broker — berjalan langsung di dalam proses aplikasi.
    /// Semua robot (Polebot) connect ke broker ini di port 1883.
    /// </summary>
    public class MqttServerService : IDisposable
    {
        // ──────────────────────────────────────────────────────────
        //  Events — digunakan ViewModel untuk update UI
        // ──────────────────────────────────────────────────────────
        public event EventHandler<bool>?              ServerStatusChanged;
        public event EventHandler<MqttMessageEvent>?  MessageReceived;
        public event EventHandler<string>?             ClientConnected;
        public event EventHandler<string>?             ClientDisconnected;

        // ──────────────────────────────────────────────────────────
        //  State
        // ──────────────────────────────────────────────────────────
        private MqttServer?          _server;
        private readonly int         _port = 1883;
        private bool                 _isRunning;
        private readonly List<string> _connectedClients = new();

        public bool   IsRunning          => _isRunning;
        public int    ConnectedCount     => _connectedClients.Count;

        // ──────────────────────────────────────────────────────────
        //  Start Broker
        // ──────────────────────────────────────────────────────────
        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_isRunning) return;

            if (!IsPortAvailable(_port))
            {
                var message = $"Port {_port} is already in use. Stop the other MQTT broker or choose a different port.";
                _isRunning = false;
                ServerStatusChanged?.Invoke(this, false);
                Console.WriteLine($"[MQTT] {message}");
                throw new InvalidOperationException(message);
            }

            try
            {
                var options = new MqttServerOptionsBuilder()
                    .WithDefaultEndpoint()
                    .WithDefaultEndpointPort(_port)
                    .Build();

                var factory = new MqttFactory();
                _server = factory.CreateMqttServer(options);

                // ── Event handlers ──────────────────────────────
                _server.ClientConnectedAsync    += OnClientConnected;
                _server.ClientDisconnectedAsync += OnClientDisconnected;
                _server.InterceptingPublishAsync += OnMessageReceived;

                await _server.StartAsync();

                _isRunning = true;
                ServerStatusChanged?.Invoke(this, true);

                Console.WriteLine($"[MQTT] Broker started on port {_port}");
            }
            catch (SocketException sex) when (sex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                var message = $"Port {_port} is already in use. Stop the other MQTT broker or choose a different port.";
                _isRunning = false;
                ServerStatusChanged?.Invoke(this, false);
                Console.WriteLine($"[MQTT] Failed to start broker: {message}");
                throw new InvalidOperationException(message, sex);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                ServerStatusChanged?.Invoke(this, false);
                Console.WriteLine($"[MQTT] Failed to start broker: {ex.Message}");
                throw;
            }
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Stop Broker
        // ──────────────────────────────────────────────────────────
        public async Task StopAsync()
        {
            if (_server is null || !_isRunning) return;

            await _server.StopAsync();
            _isRunning = false;
            _connectedClients.Clear();
            ServerStatusChanged?.Invoke(this, false);
            Console.WriteLine("[MQTT] Broker stopped.");
        }

        // ──────────────────────────────────────────────────────────
        //  Publish (broker → all subscribers)
        //  Digunakan untuk kirim command ke robot
        // ──────────────────────────────────────────────────────────
        public async Task PublishAsync(string topic, object payload, bool retain = false)
        {
            if (_server is null || !_isRunning) return;

            var json    = JsonConvert.SerializeObject(payload);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(json))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retain)
                .Build();

            await _server.InjectApplicationMessage(
                new InjectedMqttApplicationMessage(message)
                {
                    SenderClientId = "fmr-server"
                });
        }

        // ──────────────────────────────────────────────────────────
        //  Publish Nav Goal ke robot
        // ──────────────────────────────────────────────────────────
        public async Task SendNavGoalAsync(string robotId, double x, double y, double yaw,
                                            string taskId = "")
        {
            var goal = new
            {
                task_id  = string.IsNullOrEmpty(taskId) ? Guid.NewGuid().ToString("N")[..8] : taskId,
                x        = Math.Round(x,   3),
                y        = Math.Round(y,   3),
                yaw      = Math.Round(yaw, 3),
                priority = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await PublishAsync($"amr/{robotId}/cmd/goal", goal);
        }

        // ──────────────────────────────────────────────────────────
        //  Send E-Stop
        // ──────────────────────────────────────────────────────────
        public async Task SendEStopAsync(string robotId)
        {
            var cmd = new { command = "estop", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            await PublishAsync($"amr/{robotId}/cmd/estop", cmd);
        }

        // ──────────────────────────────────────────────────────────
        //  Cancel Navigation
        // ──────────────────────────────────────────────────────────
        public async Task SendCancelAsync(string robotId)
        {
            var cmd = new { command = "cancel", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            await PublishAsync($"amr/{robotId}/cmd/cancel", cmd);
        }

        // ══════════════════════════════════════════════════════════
        //  Private Event Handlers
        // ══════════════════════════════════════════════════════════

        private Task OnClientConnected(ClientConnectedEventArgs e)
        {
            var clientId = e.ClientId;
            if (!_connectedClients.Contains(clientId))
                _connectedClients.Add(clientId);

            ClientConnected?.Invoke(this, clientId);
            Console.WriteLine($"[MQTT] Client connected: {clientId}");
            return Task.CompletedTask;
        }

        private Task OnClientDisconnected(ClientDisconnectedEventArgs e)
        {
            var clientId = e.ClientId;
            _connectedClients.Remove(clientId);

            ClientDisconnected?.Invoke(this, clientId);
            Console.WriteLine($"[MQTT] Client disconnected: {clientId}");
            return Task.CompletedTask;
        }

        private Task OnMessageReceived(InterceptingPublishEventArgs e)
        {
            var topic   = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();

            var evt = new MqttMessageEvent
            {
                Topic     = topic,
                Payload   = payload,
                ClientId  = e.ClientId,
                Timestamp = DateTime.Now
            };

            MessageReceived?.Invoke(this, evt);
            return Task.CompletedTask;
        }

        // ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            _server?.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Event Data
    // ──────────────────────────────────────────────────────────────
    public class MqttMessageEvent
    {
        public string   Topic     { get; set; } = string.Empty;
        public string   Payload   { get; set; } = string.Empty;
        public string   ClientId  { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
