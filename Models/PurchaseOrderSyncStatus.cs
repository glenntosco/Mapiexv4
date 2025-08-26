namespace P4WIntegration.Models;

public class PurchaseOrderSyncStatus : BaseSyncStatus
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
}