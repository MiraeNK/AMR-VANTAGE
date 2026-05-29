using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FMR.AisinAMR.Models;

namespace FMR.AisinAMR.Services
{
    public class CommandTracker
    {
        private readonly ConcurrentDictionary<string, CommandAck> _pending = new();
        private readonly object _historyLock = new();
        
        public event EventHandler<CommandAck>? CommandAcked;

        public ObservableCollection<CommandAck> CommandHistory { get; } = new();

        public CommandTracker()
        {
            StartTimeoutChecker();
        }

        public string TrackCommand(string robotId, string commandType)
        {
            var cmdId = Guid.NewGuid().ToString("N");
            var cmd = new CommandAck
            {
                CommandId = cmdId,
                RobotId = robotId,
                CommandType = commandType,
                SentAt = DateTime.Now
            };

            _pending.TryAdd(cmdId, cmd);

            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_historyLock)
                {
                    CommandHistory.Insert(0, cmd);
                    if (CommandHistory.Count > 50)
                    {
                        CommandHistory.RemoveAt(50);
                    }
                }
            });

            return cmdId;
        }

        public void Acknowledge(string topic, string payload)
        {
            // Parse topic to determine command type and robot id
            // Format: amr/{robotId}/event/{eventType}
            var parts = topic.Split('/');
            if (parts.Length < 4 || parts[0] != "amr" || parts[2] != "event")
                return;

            string robotId = parts[1];
            string eventType = parts[3];

            string commandType = eventType switch
            {
                "goal_accepted" => "NavGoal",
                "goal_cancelled" => "Cancel",
                "estop" => "EStop",
                "arrived" => "NavGoal", // Can also mark NavGoal as arrived
                "error" => "Error",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(commandType) && eventType != "error")
                return;

            // Find matching pending command
            var match = _pending.Values
                .Where(c => c.RobotId == robotId && 
                            (c.CommandType == commandType || (eventType == "error" && !c.IsAcked)))
                .OrderBy(c => c.SentAt)
                .FirstOrDefault();

            if (match != null)
            {
                if (eventType == "error")
                {
                    match.IsSuccess = false;
                    match.ErrorMessage = "Robot reported error";
                }
                else
                {
                    match.IsSuccess = true;
                }

                match.IsAcked = true;
                match.AckedAt = DateTime.Now;
                match.AckPayload = payload;

                _pending.TryRemove(match.CommandId, out _);

                // Notify UI
                CommandAcked?.Invoke(this, match);
                
                // Force UI to refresh binding since properties changed
                Application.Current.Dispatcher.Invoke(() =>
                {
                    int index = CommandHistory.IndexOf(match);
                    if (index >= 0)
                    {
                        CommandHistory[index] = match; // trigger collection change
                    }
                });
            }
        }

        private void StartTimeoutChecker()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(1000);
                    
                    var now = DateTime.Now;
                    foreach (var kvp in _pending.ToList())
                    {
                        var cmd = kvp.Value;
                        double timeoutSeconds = cmd.CommandType switch
                        {
                            "EStop" => 3,
                            "Cancel" => 5,
                            "Launch" => 30,
                            _ => 10 // default for NavGoal and others
                        };

                        if ((now - cmd.SentAt).TotalSeconds > timeoutSeconds)
                        {
                            if (_pending.TryRemove(kvp.Key, out var timedOutCmd))
                            {
                                timedOutCmd.IsAcked = true; // Mark as resolved (failed)
                                timedOutCmd.IsSuccess = false;
                                timedOutCmd.ErrorMessage = "Timeout";

                                CommandAcked?.Invoke(this, timedOutCmd);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    int index = CommandHistory.IndexOf(timedOutCmd);
                                    if (index >= 0)
                                    {
                                        CommandHistory[index] = timedOutCmd;
                                    }
                                });
                            }
                        }
                    }
                }
            });
        }
    }
}
