namespace P4WIntegration.Models;

public abstract class BaseUploadStatus
{
    public required string CompanyName { get; set; }
    public DateTime UploadDateTime { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
}