namespace FMR.AisinAMR.Models
{
    public class NavGoal
    {
        public string task_id { get; set; } = string.Empty;
        public double x { get; set; }
        public double y { get; set; }
        public double yaw { get; set; }
        public int priority { get; set; } = 1;
        public long timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
