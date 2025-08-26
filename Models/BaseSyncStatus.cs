namespace P4WIntegration.Models;

public abstract class BaseSyncStatus
{
    public required string CompanyName { get; set; }
    public DateTime LastSyncDateTime { get; set; }
    public string? SyncHash { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedDateTime { get; set; }
    public bool SyncedToP4W { get; set; }
    public DateTime? P4WSyncDateTime { get; set; }
    
    // Image synchronization fields
    public string? ImageUrl { get; set; }
    public DateTime? ImageSyncDateTime { get; set; }
    public string? ImageHash { get; set; }
}