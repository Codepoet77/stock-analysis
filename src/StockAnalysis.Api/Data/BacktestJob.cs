namespace StockAnalysis.Api.Data;

public enum BacktestJobStatus
{
    Running,
    Completed,
    Cancelled,
    Failed
}

public class BacktestJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public BacktestJobStatus Status { get; set; } = BacktestJobStatus.Running;
    public string ParametersJson { get; set; } = "";
    public string? ProgressJson { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool CancelRequested { get; set; }
}
