using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace P4WIntegration.Services;

public class AzureBlobService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureBlobService> _logger;
    private readonly string _containerName;
    private readonly string _baseUrl;
    private readonly TimeSpan _uploadTimeout;
    private readonly long _maxFileSizeBytes;

    public AzureBlobService(
        string connectionString, 
        string containerName, 
        string baseUrl,
        ILogger<AzureBlobService> logger,
        TimeSpan? uploadTimeout = null,
        long maxFileSizeBytes = 10485760) // 10MB default
    {
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerName = containerName;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
        _uploadTimeout = uploadTimeout ?? TimeSpan.FromMinutes(5);
        _maxFileSizeBytes = maxFileSizeBytes;
        
        _containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
    }

    /// <summary>
    /// Uploads an image stream to Azure Blob Storage
    /// </summary>
    /// <param name="imageStream">The image stream to upload</param>
    /// <param name="blobName">The name of the blob (path within container)</param>
    /// <param name="contentType">The MIME type of the image (optional, will be detected)</param>
    /// <returns>The public URL of the uploaded blob</returns>
    public async Task<string?> UploadImageAsync(Stream imageStream, string blobName, string? contentType = null)
    {
        if (imageStream == null || imageStream.Length == 0)
        {
            _logger.LogWarning("Cannot upload empty or null image stream for blob {BlobName}", blobName);
            return null;
        }

        if (imageStream.Length > _maxFileSizeBytes)
        {
            _logger.LogWarning("Image size {Size} bytes exceeds maximum allowed size {MaxSize} bytes for blob {BlobName}", 
                imageStream.Length, _maxFileSizeBytes, blobName);
            return null;
        }

        try
        {
            // Ensure container exists
            await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            // Reset stream position if possible
            if (imageStream.CanSeek)
            {
                imageStream.Position = 0;
            }

            // Detect content type if not provided
            if (string.IsNullOrEmpty(contentType))
            {
                contentType = DetectContentType(blobName);
            }

            var blobClient = _containerClient.GetBlobClient(blobName);
            
            // Set upload options
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                },
                Metadata = new Dictionary<string, string>
                {
                    ["UploadedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["UploadedBy"] = "P4WIntegration"
                }
            };

            // Upload with timeout
            using var cts = new CancellationTokenSource(_uploadTimeout);
            var response = await blobClient.UploadAsync(imageStream, uploadOptions, cts.Token);

            if (response?.Value != null)
            {
                var publicUrl = GetBlobUrl(blobName);
                _logger.LogInformation("Successfully uploaded image to blob {BlobName}, URL: {Url}", blobName, publicUrl);
                return publicUrl;
            }

            _logger.LogError("Failed to upload image to blob {BlobName} - no response received", blobName);
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Upload timeout exceeded for blob {BlobName} after {Timeout} seconds", 
                blobName, _uploadTimeout.TotalSeconds);
            return null;
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Storage error uploading blob {BlobName}: {ErrorCode} - {Message}", 
                blobName, ex.ErrorCode, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading image to blob {BlobName}", blobName);
            return null;
        }
    }

    /// <summary>
    /// Gets the public URL for a blob
    /// </summary>
    /// <param name="blobName">The name of the blob</param>
    /// <returns>The public URL</returns>
    public string GetBlobUrl(string blobName)
    {
        return $"{_baseUrl}/{_containerName}/{blobName}";
    }

    /// <summary>
    /// Checks if a blob exists in the container
    /// </summary>
    /// <param name="blobName">The name of the blob to check</param>
    /// <returns>True if the blob exists, false otherwise</returns>
    public async Task<bool> BlobExistsAsync(string blobName)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            var response = await blobClient.ExistsAsync();
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if blob {BlobName} exists", blobName);
            return false;
        }
    }

    /// <summary>
    /// Downloads a blob as a stream
    /// </summary>
    /// <param name="blobName">The name of the blob to download</param>
    /// <returns>The blob content as a stream, or null if not found</returns>
    public async Task<Stream?> DownloadBlobAsync(string blobName)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            
            if (!await blobClient.ExistsAsync())
            {
                _logger.LogWarning("Blob {BlobName} does not exist", blobName);
                return null;
            }

            var response = await blobClient.DownloadStreamingAsync();
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading blob {BlobName}", blobName);
            return null;
        }
    }

    /// <summary>
    /// Calculates the SHA256 hash of a stream for change detection
    /// </summary>
    /// <param name="stream">The stream to hash</param>
    /// <returns>The SHA256 hash as a hex string</returns>
    public static async Task<string> CalculateHashAsync(Stream stream)
    {
        using var sha256 = SHA256.Create();
        
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var hashBytes = await sha256.ComputeHashAsync(stream);
        
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generates a blob name for a product image
    /// </summary>
    /// <param name="companyName">The company name</param>
    /// <param name="itemCode">The item code</param>
    /// <param name="imageHash">The hash of the image content</param>
    /// <param name="fileExtension">The file extension (with dot)</param>
    /// <returns>The blob name</returns>
    public static string GenerateBlobName(string companyName, string itemCode, string imageHash, string fileExtension)
    {
        // Sanitize inputs for blob naming
        var sanitizedCompany = SanitizeForBlobName(companyName);
        var sanitizedItemCode = SanitizeForBlobName(itemCode);
        var sanitizedHash = imageHash[..Math.Min(8, imageHash.Length)]; // Use first 8 chars of hash
        
        return $"{sanitizedCompany}/{sanitizedItemCode}_{sanitizedHash}{fileExtension}";
    }

    /// <summary>
    /// Detects the MIME content type based on file extension
    /// </summary>
    /// <param name="fileName">The file name or path</param>
    /// <returns>The MIME type</returns>
    private static string DetectContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tiff" or ".tif" => "image/tiff",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Sanitizes a string for use in blob names
    /// </summary>
    /// <param name="input">The input string</param>
    /// <returns>The sanitized string</returns>
    private static string SanitizeForBlobName(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "unknown";

        // Replace invalid characters with underscores
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        
        var result = sb.ToString();
        
        // Ensure it doesn't start or end with invalid characters
        return result.Trim('_', '-').ToLowerInvariant();
    }
}