using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMR.AisinAMR.Models;
using FMR.AisinAMR.Services;
using Newtonsoft.Json;

namespace FMR.AisinAMR.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // ──────────────────────────────────────────────────────────
        //  Services
        // ──────────────────────────────────────────────────────────
        private readonly MqttServerService _mqttServer;
        private readonly MqttClientService _mqttClient;
        private readonly SshLaunchService _sshService;
        private readonly CommandTracker _commandTracker;
        private readonly System.Timers.Timer _nodeStatusTimer;

        // ──────────────────────────────────────────────────────────
        //  Observable Properties
        // ──────────────────────────────────────────────────────────

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MqttStatusText))]
        private bool _isMqttServerOnline;
        public string MqttStatusText => IsMqttServerOnline ? "MQTT Online" : "MQTT Offline";

        [ObservableProperty] private int _connectedRobotsCount;
        [ObservableProperty] private DateTime _lastUpdated = DateTime.Now;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
        private string _currentPage = "Dashboard";

        public string CurrentPageTitle => CurrentPage switch
        {
            "Dashboard" => "Overview · Real-time monitoring",
            "Fleet"     => "Robot Fleet · All units",
            "Map"       => "Map View · Floor plan",
            "Settings"  => "Settings · Configuration",
            _           => CurrentPage
        };

        // SSH Credentials
        [ObservableProperty] private string _sshUsername = "eqdev";
        [ObservableProperty] private string _sshPassword = "";
        [ObservableProperty] private string _sshPort = "22";
        [ObservableProperty] private string _sshHostIp = "192.168.137.40";
        [ObservableProperty] private string _sshTestResult = "";
        [ObservableProperty] private bool _isSshPopupOpen;

        public ObservableCollection<RobotStatus> RobotList { get; } = new();
        [ObservableProperty] private RobotStatus? _robotSelected;

        // Keep Robot for dashboard/legacy bindings temporarily if needed
        [ObservableProperty] private RobotStatus _robot = new();

        [ObservableProperty] private string _selectedRobotId = string.Empty;
        [ObservableProperty] private string _mapFilePath = string.Empty;
        [ObservableProperty] private double _mapResolution = 0.05;
        [ObservableProperty] private double _originX = -0.691;
        [ObservableProperty] private double _originY = -3.07;
        [ObservableProperty] private bool _autoPublishInitialPose;

        partial void OnMapResolutionChanged(double value) => FMR.AisinAMR.Helpers.MapCoordinateHelper.Resolution = value;
        partial void OnOriginXChanged(double value) => FMR.AisinAMR.Helpers.MapCoordinateHelper.OriginX = value;
        partial void OnOriginYChanged(double value) => FMR.AisinAMR.Helpers.MapCoordinateHelper.OriginY = value;

        public ObservableCollection<MqttLogEntry> MqttMessages { get; } = new();
        public ObservableCollection<CommandAck> CommandHistory => _commandTracker.CommandHistory;

        [ObservableProperty] private RobotStatus? _launchingRobot;

        public MainViewModel()
        {
            _mqttServer = new MqttServerService();
            _mqttClient = new MqttClientService();
            _sshService = new SshLaunchService();
            _commandTracker = new CommandTracker();
            
            _commandTracker.CommandAcked += OnCommandAcked;

            _sshService.OutputReceived += OnSshOutputReceived;
            _sshService.LaunchCompleted += OnLaunchCompleted;
            
            _nodeStatusTimer = new System.Timers.Timer(3000);
            _nodeStatusTimer.Elapsed += async (s, e) => await PollNodeStatusAsync();

            _mqttServer.ServerStatusChanged  += OnServerStatusChanged;
            _mqttServer.MessageReceived       += OnMqttMessageReceived;
            _mqttServer.ClientConnected       += OnClientConnected;
            _mqttServer.ClientDisconnected    += OnClientDisconnected;

            _mqttClient.ConnectionStatusChanged += OnServerStatusChanged;
            _mqttClient.MessageReceived         += OnMqttMessageReceived;

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _mqttServer.StartAsync();
                await _mqttClient.StartAsync();
                await _mqttClient.SubscribeAsync("amr/+/identity");
                await _mqttClient.SubscribeAsync("amr/+/status/#");
                await _mqttClient.SubscribeAsync("amr/+/event/#");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize MQTT services:\n{ex.Message}", "FMR — MQTT Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  SSH Commands
        // ──────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task TestSshConnection()
        {
            SshTestResult = "Testing...";
            await Task.Run(() =>
            {
                try
                {
                    int port = int.TryParse(SshPort, out int p) ? p : 22;
                    string host = string.IsNullOrWhiteSpace(SshHostIp) ? (RobotList.FirstOrDefault()?.IpAddress ?? "192.168.137.40") : SshHostIp;
                    using var client = new Renci.SshNet.SshClient(host, port, SshUsername, SshPassword); 
                    client.Connect();
                    client.Disconnect();
                    Application.Current.Dispatcher.Invoke(() => SshTestResult = $"Success connecting to {host}");
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => SshTestResult = $"Error: {ex.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task StartNavigation(RobotStatus r)
        {
            if (r == null) return;
            r.IsLaunching = true;
            r.SshStatus = "Launching";
            r.SshLog = "Starting Navigation Mode...\n";
            _launchingRobot = r;
            IsSshPopupOpen = true;
            _nodeStatusTimer.Start();

            var cmdId = _commandTracker.TrackCommand(r.RobotId, "Launch");
            string host = string.IsNullOrWhiteSpace(SshHostIp) ? r.IpAddress : SshHostIp;
            bool success = await _sshService.LaunchNavigationAsync(host, SshUsername, SshPassword);
            
            if (!success)
            {
                r.IsLaunching = false;
                r.SshStatus = "Error";
            }
        }

        [RelayCommand]
        private async Task StartMapping(RobotStatus r)
        {
            if (r == null) return;
            r.IsLaunching = true;
            r.SshStatus = "Launching";
            r.SshLog = "Starting Mapping Mode...\n";
            _launchingRobot = r;
            IsSshPopupOpen = true;
            _nodeStatusTimer.Start();

            var cmdId = _commandTracker.TrackCommand(r.RobotId, "Launch");
            string host = string.IsNullOrWhiteSpace(SshHostIp) ? r.IpAddress : SshHostIp;
            bool success = await _sshService.LaunchMappingAsync(host, SshUsername, SshPassword);
            
            if (!success)
            {
                r.IsLaunching = false;
                r.SshStatus = "Error";
            }
        }

        [RelayCommand]
        private void CloseSshPopup()
        {
            IsSshPopupOpen = false;
            _nodeStatusTimer.Stop();
            _launchingRobot = null;
        }

        private void OnSshOutputReceived(object? sender, string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_launchingRobot != null)
                {
                    var lines = (_launchingRobot.SshLog + text).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    _launchingRobot.SshLog = string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 200))) + "\n";
                }
            });
        }

        private async void OnLaunchCompleted(object? sender, (bool success, string mode, System.Collections.Generic.Dictionary<string, bool> nodes) e)
        {
            Application.Current.Dispatcher.Invoke(() => 
            {
                if (_launchingRobot != null)
                {
                    _launchingRobot.IsLaunching = false;
                    _launchingRobot.SshStatus = e.success ? "Success" : "Failed";
                    _launchingRobot.RosNodes = e.nodes;
                    if (e.success) _launchingRobot.CurrentMode = e.mode;
                }
            });

            if (_launchingRobot != null && !string.IsNullOrEmpty(_launchingRobot.RobotId))
            {
                var payload = new 
                { 
                    success = e.success, 
                    mode = e.mode, 
                    nodes = e.nodes, 
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() 
                };
                await _mqttClient.PublishAsync($"amr/{_launchingRobot.RobotId}/server/launch_status", JsonConvert.SerializeObject(payload));
            }
        }

        private async Task PollNodeStatusAsync()
        {
            if (_launchingRobot == null || !IsSshPopupOpen) return;
            string host = string.IsNullOrWhiteSpace(SshHostIp) ? _launchingRobot.IpAddress : SshHostIp;
            var nodes = await _sshService.CheckRosNodesAsync(host, SshUsername, SshPassword);
            
            Application.Current.Dispatcher.Invoke(() => 
            {
                if (_launchingRobot != null && nodes.Count > 0)
                {
                    _launchingRobot.RosNodes = nodes;
                }
            });
        }

        private void OnCommandAcked(object? sender, CommandAck cmd)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var r = RobotList.FirstOrDefault(x => x.RobotId == cmd.RobotId);
                if (r != null)
                {
                    r.LastCommandType = cmd.CommandType;
                    r.LastCommandStatus = cmd.IsSuccess ? "✓ ACK" : (cmd.ErrorMessage == "Timeout" ? "Timeout" : "Failed");
                    r.LastCommandLatency = cmd.Latency.HasValue ? $"{cmd.Latency.Value.TotalMilliseconds:F0}ms" : "-";
                }
            });
        }

        // ──────────────────────────────────────────────────────────
        //  MQTT Event Handlers
        // ──────────────────────────────────────────────────────────

        [RelayCommand]
        private void Navigate(string page) => CurrentPage = page;

        [RelayCommand]
        private void SelectRobot(RobotStatus r)
        {
            if (r != null) RobotSelected = r;
        }

        [RelayCommand]
        private void Refresh() => LastUpdated = DateTime.Now;

        [RelayCommand]
        private async Task SendGoal(string robotId)
        {
            var targetRobotId = string.IsNullOrWhiteSpace(robotId) ? RobotSelected?.RobotId ?? Robot.RobotId : robotId;
            if (string.IsNullOrWhiteSpace(targetRobotId)) return;
            var cmdId = _commandTracker.TrackCommand(targetRobotId, "NavGoal");
            var goal = new NavGoalPayload { task_id = cmdId, x = 1.5, y = 0.8, yaw = 0.0, priority = 1, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            await _mqttClient.PublishAsync($"amr/{targetRobotId}/cmd/goal", goal);
        }

        public async Task SendNavGoalAtAsync(double x, double y, double yaw = 0.0)
        {
            var targetRobotId = RobotSelected?.RobotId ?? Robot.RobotId;
            if (string.IsNullOrWhiteSpace(targetRobotId)) return;
            var cmdId = _commandTracker.TrackCommand(targetRobotId, "NavGoal");
            var payload = new NavGoalPayload { task_id = cmdId, x = x, y = y, yaw = yaw, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            await _mqttClient.PublishAsync($"amr/{targetRobotId}/cmd/goal", payload);

            if (RobotSelected != null)
            {
                RobotSelected.HasActiveGoal = true;
                RobotSelected.GoalX = x;
                RobotSelected.GoalY = y;
            }
        }

        [RelayCommand]
        private async Task EStop(RobotStatus r)
        {
            var targetRobotId = r?.RobotId ?? RobotSelected?.RobotId ?? Robot.RobotId;
            if (string.IsNullOrWhiteSpace(targetRobotId)) return;
            var cmdId = _commandTracker.TrackCommand(targetRobotId, "EStop");
            await _mqttClient.PublishAsync($"amr/{targetRobotId}/cmd/estop", new { task_id = cmdId, command = "estop", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }

        [RelayCommand]
        private async Task CancelNav()
        {
            var targetRobotId = RobotSelected?.RobotId ?? Robot.RobotId;
            if (string.IsNullOrWhiteSpace(targetRobotId)) return;
            var cmdId = _commandTracker.TrackCommand(targetRobotId, "Cancel");
            await _mqttClient.PublishAsync($"amr/{targetRobotId}/cmd/cancel", new { task_id = cmdId, command = "cancel", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            
            if (RobotSelected != null) RobotSelected.HasActiveGoal = false;
        }

        [RelayCommand]
        private async Task SetInitialPose()
        {
            var r = RobotSelected ?? Robot;
            if (string.IsNullOrWhiteSpace(r.RobotId)) return;
            var payload = new { x = r.PoseX, y = r.PoseY, yaw = r.PoseYaw * (Math.PI / 180.0), timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            await _mqttClient.PublishAsync($"amr/{r.RobotId}/cmd/set_pose", payload);
        }

        [RelayCommand]
        private void SaveSettings()
        {
            if (AutoPublishInitialPose) _ = SetInitialPose();
        }

        [RelayCommand]
        private void ResetSettings()
        {
            SelectedRobotId = RobotSelected?.RobotId ?? Robot.RobotId;
            MapFilePath = string.Empty;
            MapResolution = 0.05;
            OriginX = 0.0;
            OriginY = 0.0;
            AutoPublishInitialPose = false;
        }

        private void OnServerStatusChanged(object? sender, bool isOnline)
        {
            Application.Current.Dispatcher.Invoke(() => IsMqttServerOnline = isOnline);
        }

        private void OnClientConnected(object? sender, string clientId)
        {
            Application.Current.Dispatcher.Invoke(() => { ConnectedRobotsCount = RobotList.Count(x => x.IsOnline); AddLog("connect", clientId, "Client connected"); });
        }

        private void OnClientDisconnected(object? sender, string clientId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog("disconnect", clientId, "Client disconnected");
                var r = RobotList.FirstOrDefault(x => x.RobotId == clientId);
                if (r != null) r.IsOnline = false;
                ConnectedRobotsCount = RobotList.Count(x => x.IsOnline);
            });
        }

        private void OnMqttMessageReceived(object? sender, MqttMessageEvent e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog(e.Topic, e.ClientId, e.Payload);
                ParseAndUpdateRobotState(e.Topic, e.Payload);
                
                if (e.Topic.Contains("/event/"))
                {
                    _commandTracker.Acknowledge(e.Topic, e.Payload);
                }
                
                LastUpdated = DateTime.Now;
            });
        }

        private void ParseAndUpdateRobotState(string topic, string payload)
        {
            try
            {
                var parts = topic.Split('/');
                if (parts.Length < 3) return;
                var robotId = parts[1];

                var r = RobotList.FirstOrDefault(x => x.RobotId == robotId);
                if (r == null && topic.Contains("/identity"))
                {
                    r = new RobotStatus { RobotId = robotId };
                    RobotList.Add(r);
                    if (RobotList.Count == 1)
                    {
                        Robot = r;
                        RobotSelected = r;
                    }
                }
                if (r == null) 
                {
                    r = new RobotStatus { RobotId = robotId };
                    RobotList.Add(r);
                }

                if (topic.Contains("/identity"))
                {
                    dynamic? data = JsonConvert.DeserializeObject(payload);
                    if (data != null)
                    {
                        if (data.name != null) r.RobotName = data.name;
                        if (data.ip != null) r.IpAddress = data.ip;
                        if (data.online != null) r.IsOnline = data.online;
                    }
                }
                else if (topic.Contains("/status/pose"))
                {
                    var data = JsonConvert.DeserializeObject<PosePayload>(payload);
                    if (data is null) return;
                    r.PoseX = data.x;
                    r.PoseY = data.y;
                    r.PoseYaw = data.yaw * (180.0 / Math.PI);
                    r.LastPoseUpdate = DateTime.Now;
                    r.IsOnline = true;
                    dynamic? d = JsonConvert.DeserializeObject(payload);
                    if (d?.linear_vel != null) r.LinearVelocity = d.linear_vel;
                    if (d?.angular_vel != null) r.AngularVelocity = d.angular_vel;
                }
                else if (topic.Contains("/status/health"))
                {
                    var data = JsonConvert.DeserializeObject<HealthPayload>(payload);
                    if (data is null) return;
                    r.PlcConnected = data.plc_ok;
                    r.BatteryLevel = data.battery;
                    r.NavState = data.nav_state ?? "Idle";
                    r.HeartbeatCount = data.heartbeat;
                    r.LastSeen = DateTime.Now;
                    r.IsOnline = true;
                    dynamic? d = JsonConvert.DeserializeObject(payload);
                    if (d?.ip != null) r.IpAddress = d.ip;
                    r.BatteryStatus = data.battery switch { >= 70 => "Normal", >= 30 => "Low", _ => "Critical" };
                }
                else if (topic.Contains("/status/scan"))
                {
                    var data = JsonConvert.DeserializeObject<ScanPayload>(payload);
                    if (data is null) return;
                    r.ScanAngleMin = data.angle_min;
                    r.ScanAngleInc = data.angle_inc;
                    r.ScanRanges = data.ranges;
                }
                else if (topic.Contains("/event/arrived")) { r.NavState = "Arrived"; r.HasActiveGoal = false; }
                else if (topic.Contains("/event/error")) { r.NavState = "Error"; r.HasActiveGoal = false; }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ViewModel] Parse error for {topic}: {ex.Message}");
            }
            finally
            {
                ConnectedRobotsCount = RobotList.Count(x => x.IsOnline);
            }
        }

        private void AddLog(string topic, string clientId, string payload)
        {
            if (MqttMessages.Count >= 50) MqttMessages.RemoveAt(0);
            MqttMessages.Add(new MqttLogEntry { Timestamp = DateTime.Now, Topic = topic, ClientId = clientId, Payload = payload.Length > 60 ? payload[..60] + "…" : payload });
        }


        public void Dispose()
        {
            _nodeStatusTimer?.Stop();
            _nodeStatusTimer?.Dispose();
            
            _commandTracker.CommandAcked -= OnCommandAcked;

            _sshService.OutputReceived -= OnSshOutputReceived;
            _sshService.LaunchCompleted -= OnLaunchCompleted;
            _sshService.Dispose();

            _mqttServer.ServerStatusChanged -= OnServerStatusChanged;
            _mqttServer.MessageReceived -= OnMqttMessageReceived;
            _mqttServer.ClientConnected -= OnClientConnected;
            _mqttServer.ClientDisconnected -= OnClientDisconnected;

            _mqttClient.ConnectionStatusChanged -= OnServerStatusChanged;
            _mqttClient.MessageReceived -= OnMqttMessageReceived;

            _mqttServer.Dispose();
            _mqttClient.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    public class MqttLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Topic { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }
}
