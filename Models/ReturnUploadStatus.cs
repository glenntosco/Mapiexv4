namespace P4WIntegration.Models;

public class ReturnUploadStatus : BaseUploadStatus
{
    public required string P4WReturnId { get; set; }
    public int? SAPDocEntry { get; set; }
}