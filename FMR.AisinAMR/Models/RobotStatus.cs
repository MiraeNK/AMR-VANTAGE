using System;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FMR.AisinAMR.Models
{
    /// <summary>
    /// Data model untuk status realtime satu robot AMR.
    /// Di-update setiap kali pesan MQTT masuk dari robot.
    /// </summary>
    public partial class RobotStatus : ObservableObject
    {
        [ObservableProperty] private string _robotId      = "polebot01";
        [ObservableProperty] private string _robotName    = "Polebot-01";
        [ObservableProperty] private bool   _isOnline;

        // Position (dari /amcl_pose atau /odom)
        [ObservableProperty] private double _poseX;
        [ObservableProperty] private double _poseY;
        [ObservableProperty] private double _poseYaw;

        // Velocity (dari /cmd_vel atau /odom.twist)
        [ObservableProperty] private double _linearVelocity;
        [ObservableProperty] private double _angularVelocity;

        // Battery
        [ObservableProperty] private double _batteryLevel = 100.0;
        [ObservableProperty] private string _batteryStatus = "Normal";

        // Navigation state
        [ObservableProperty] private string _navState = "Idle";

        // PLC
        [ObservableProperty] private bool   _plcConnected;
        [ObservableProperty] private long   _heartbeatCount;
        [ObservableProperty] private string _ipAddress    = "192.168.3.250";
        [ObservableProperty] private string _robotType    = "Polebot";
        [ObservableProperty] private string _hardwareVersion = string.Empty;
        [ObservableProperty] private string[] _capabilities = Array.Empty<string>();

        // SSH State
        [ObservableProperty] private bool _isLaunching;
        [ObservableProperty] private string _currentMode = string.Empty;
        [ObservableProperty] private string _sshStatus = string.Empty;
        [ObservableProperty] private string _sshLog = string.Empty;

        // Feedback Loop UI
        [ObservableProperty] private System.Collections.Generic.Dictionary<string, bool> _rosNodes = new();
        [ObservableProperty] private string _lastCommandType = "None";
        [ObservableProperty] private string _lastCommandStatus = "Unknown";
        [ObservableProperty] private string _lastCommandLatency = "";

        // Timestamps
        [ObservableProperty] private DateTime _lastSeen = DateTime.Now;
        [ObservableProperty] private DateTime _lastPoseUpdate;

        // Scan visualization
        public ObservableCollection<System.Windows.Point> ScanPoints { get; } = new();
        [ObservableProperty] private double _scanAngleMin;
        [ObservableProperty] private double _scanAngleInc;
        [ObservableProperty] private double[] _scanRanges = Array.Empty<double>();

        // Nav Goal Visualization
        [ObservableProperty] private bool _hasActiveGoal;
        [ObservableProperty] private double _goalX;
        [ObservableProperty] private double _goalY;

        // Computed
        public string StatusLabel => IsOnline ? "Online" : "Offline";
        public bool   IsNavigating => NavState == "Navigating";
    }

    /// <summary>
    /// Payload MQTT standard untuk nav goal
    /// </summary>
    public class NavGoalPayload
    {
        public string task_id   { get; set; } = string.Empty;
        public double x         { get; set; }
        public double y         { get; set; }
        public double yaw       { get; set; }
        public int    priority  { get; set; } = 1;
        public long   timestamp { get; set; }
    }

    /// <summary>
    /// Payload MQTT untuk status/pose dari robot
    /// </summary>
    public class PosePayload
    {
        public long   timestamp { get; set; }
        public double x         { get; set; }
        public double y         { get; set; }
        public double yaw       { get; set; }
        public string frame     { get; set; } = "map";
    }

    /// <summary>
    /// Payload MQTT untuk status/health dari robot
    /// </summary>
    public class HealthPayload
    {
        public long   timestamp  { get; set; }
        public bool   plc_ok     { get; set; }
        public double battery    { get; set; }
        public string nav_state  { get; set; } = "Idle";
        public long   heartbeat  { get; set; }
    }

    /// <summary>
    /// Payload MQTT untuk laser scan
    /// </summary>
    public class ScanPayload
    {
        public long timestamp { get; set; }
        public double angle_min { get; set; }
        public double angle_inc { get; set; }
        public double[] ranges { get; set; } = Array.Empty<double>();
    }
}
