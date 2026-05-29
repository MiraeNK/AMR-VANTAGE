using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace FMR.AisinAMR.Services
{
    public class SshLaunchService : IDisposable
    {
        public event EventHandler<string>? OutputReceived;
        public event EventHandler<(bool success, string mode, Dictionary<string, bool> nodes)>? LaunchCompleted;
        
        private SshClient? _sshClient;
        private ShellStream? _shellStream;
        private CancellationTokenSource? _cts;

        private void OnOutputReceived(string output)
        {
            OutputReceived?.Invoke(this, output);
        }

        public async Task<bool> LaunchNavigationAsync(string host, string user, string password)
        {
            var command = "cd ~ && ~/catkin_ws/start_ros1_headless.sh &\n" +
                          "sleep 8\n" +
                          "source ~/amr_mp/install/setup.bash && ros2 launch amr_mp_bringup navigation.launch.py &\n" +
                          "sleep 3\n" +
                          "~/amr_mp/check_ros_status.sh\n";
                          
            return await LaunchAsync(host, user, password, command, "Navigation");
        }

        public async Task<bool> LaunchMappingAsync(string host, string user, string password)
        {
            var command = "cd ~ && ~/catkin_ws/start_ros1_headless.sh &\n" +
                          "sleep 8\n" +
                          "source ~/amr_mp/install/setup.bash && ros2 launch amr_mp_bringup mapping.launch.py &\n" +
                          "sleep 3\n" +
                          "~/amr_mp/check_ros_status.sh\n";
                          
            return await LaunchAsync(host, user, password, command, "Mapping");
        }

        private async Task<bool> LaunchAsync(string host, string user, string password, string command, string mode)
        {
            await StopAllAsync(host, user, password); // ensure previous sessions are stopped
            
            try
            {
                _sshClient = new SshClient(host, user, password);
                _sshClient.Connect();
                
                _shellStream = _sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
                
                _cts = new CancellationTokenSource();
                
                bool hasError = false;

                // Read stream in background
                _ = Task.Run(async () =>
                {
                    while (!_cts.Token.IsCancellationRequested && _sshClient.IsConnected)
                    {
                        try
                        {
                            if (_shellStream.DataAvailable)
                            {
                                var text = _shellStream.Read();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    string lowerText = text.ToLower();
                                    if (lowerText.Contains("error") || lowerText.Contains("failed") || 
                                        lowerText.Contains("exception") || lowerText.Contains("no such file") || 
                                        lowerText.Contains("not found"))
                                    {
                                        hasError = true;
                                    }
                                    
                                    OnOutputReceived(text);
                                }
                            }
                            else
                            {
                                await Task.Delay(100, _cts.Token);
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }
                }, _cts.Token);

                // Send command
                _shellStream.WriteLine(command);
                
                // Wait for the scripts and sleeps to finish (8 + 3 = 11 seconds)
                // We'll wait 12 seconds to be safe
                _ = Task.Run(async () => 
                {
                    await Task.Delay(12000);
                    var nodes = await CheckRosNodesAsync(host, user, password);
                    bool isSuccess = !hasError;
                    LaunchCompleted?.Invoke(this, (isSuccess, mode, nodes));
                });
                
                return true;
            }
            catch (Exception ex)
            {
                OnOutputReceived($"SSH Error: {ex.Message}\n");
                return false;
            }
        }
        
        public async Task<Dictionary<string, bool>> CheckRosNodesAsync(string host, string user, string password)
        {
            var nodes = new Dictionary<string, bool>();
            try
            {
                using var client = new SshClient(host, user, password);
                client.Connect();
                var cmd = client.CreateCommand("~/amr_mp/check_ros_status.sh");
                var result = await Task.Run(() => cmd.Execute());
                client.Disconnect();
                
                if (!string.IsNullOrWhiteSpace(result))
                {
                    using var doc = JsonDocument.Parse(result);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.True)
                            nodes[prop.Name] = true;
                        else if (prop.Value.ValueKind == JsonValueKind.False)
                            nodes[prop.Name] = false;
                        // Ignore non-boolean fields like "timestamp"
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckRosNodesAsync failed: {ex.Message}");
            }
            
            return nodes;
        }

        public async Task StopAllAsync(string host, string user, string password)
        {
            CleanupCurrentSession();
            
            try
            {
                using var client = new SshClient(host, user, password);
                client.Connect();
                var cmd = client.CreateCommand("pkill -f \"ros2 launch\" ; pkill -f \"dynamic_bridge\" ; pkill -f \"lsc_laser_publisher\" ; pkill -f \"roscore\"");
                await Task.Run(() => cmd.Execute());
                client.Disconnect();
            }
            catch
            {
                // Ignore kill errors
            }
        }

        private void CleanupCurrentSession()
        {
            _cts?.Cancel();
            _shellStream?.Dispose();
            _sshClient?.Dispose();
            
            _cts = null;
            _shellStream = null;
            _sshClient = null;
        }

        public void Dispose()
        {
            CleanupCurrentSession();
        }
    }
}
