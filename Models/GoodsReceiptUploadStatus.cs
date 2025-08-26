namespace P4WIntegration.Models;

public class GoodsReceiptUploadStatus : BaseUploadStatus
{
    public required string P4WReceiptId { get; set; }
    public int? SAPDocEntry { get; set; }
}