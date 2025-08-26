namespace P4WIntegration.Models;

public class ExecutionHistory
{
    public Guid ExecutionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public required string Operation { get; set; }
    public required string CompanyName { get; set; }
    public required string Status { get; set; }
    public int RecordsProcessed { get; set; }
    public int ErrorCount { get; set; }
    public string? CommandLine { get; set; }
}