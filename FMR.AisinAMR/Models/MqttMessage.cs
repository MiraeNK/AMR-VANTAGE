using System;

namespace FMR.AisinAMR.Models
{
    public class MqttMessage
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Topic { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }
}
