namespace P4WIntegration.Models;

public class GoodsDeliveryUploadStatus : BaseUploadStatus
{
    public required string P4WDeliveryId { get; set; }
    public int? SAPDocEntry { get; set; }
}