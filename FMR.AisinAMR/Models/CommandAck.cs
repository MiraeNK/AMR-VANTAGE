using System;

namespace FMR.AisinAMR.Models
{
    public class CommandAck
    {
        public string CommandId { get; set; } = string.Empty;
        public string RobotId { get; set; } = string.Empty;
        public string CommandType { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public DateTime? AckedAt { get; set; }
        public bool IsAcked { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string AckPayload { get; set; } = string.Empty;
        
        public TimeSpan? Latency => AckedAt.HasValue ? AckedAt.Value - SentAt : null;
    }
}
