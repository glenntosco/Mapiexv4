namespace P4WIntegration.Models;

public class SyncState
{
    public int Id { get; set; }
    public required string CompanyName { get; set; }
    public required string EntityType { get; set; }
    public DateTime LastSyncDateTime { get; set; }
}