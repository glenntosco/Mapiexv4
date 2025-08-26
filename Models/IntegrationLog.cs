namespace P4WIntegration.Models;

public class IntegrationLog
{
    public int LogId { get; set; }
    public DateTime Timestamp { get; set; }
    public required string Level { get; set; }
    public required string CompanyName { get; set; }
    public required string Operation { get; set; }
    public required string Message { get; set; }
    public string? Exception { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid ExecutionId { get; set; }
}