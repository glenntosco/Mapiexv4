using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Utilities;

namespace P4WIntegration.Workers;

public class GoodsReceiptUploadWorker : IWorker
{
    private readonly ILogger<GoodsReceiptUploadWorker> _logger;
    private readonly ServiceLayerClient _serviceLayer;
    private readonly P4WarehouseClient _p4wClient;
    private readonly IntegrationDbContext _dbContext;
    private readonly CommandLineOptions _options;
    private readonly Company _company;

    public GoodsReceiptUploadWorker(
        ILogger<GoodsReceiptUploadWorker> logger,
        ServiceLayerClient serviceLayer,
        IntegrationDbContext dbContext,
        CommandLineOptions options,
        CompanyConfig config,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _serviceLayer = serviceLayer;
        _dbContext = dbContext;
        _options = options;
        _company = config.Companies.First(c => c.CompanyName.Equals(options.Company, StringComparison.OrdinalIgnoreCase));
        _p4wClient = new P4WarehouseClient(_company.P4WarehouseApiKey, _company.SapB1.ClientName, loggerFactory.CreateLogger<P4WarehouseClient>());
    }

    public async Task<WorkerResult> ExecuteAsync()
    {
        _logger.LogInformation("Starting GoodsReceiptUpload for company {Company}", _company.CompanyName);

        var result = new WorkerResult { Success = true };
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            // Get the last upload time
            var lastUpload = await GetLastUploadTime();
            _logger.LogInformation("Last upload time: {LastUpload}", lastUpload);

            // Query P4W for completed goods receipts
            var completedReceipts = await _p4wClient.GetCompletedGoodsReceiptsAsync(lastUpload);
            
            if (completedReceipts == null || completedReceipts.Count == 0)
            {
                _logger.LogInformation("No completed goods receipts to upload");
                return result;
            }

            _logger.LogInformation("Found {Count} completed goods receipts to upload", completedReceipts.Count);

            if (_options.Limit.HasValue)
            {
                completedReceipts = completedReceipts.Take(_options.Limit.Value).ToList();
                _logger.LogInformation("Limited to {Limit} goods receipts", _options.Limit.Value);
            }

            foreach (var receipt in completedReceipts)
            {
                try
                {
                    var receiptId = receipt.GetValueOrDefault("ReceiptId", "")?.ToString() ?? "";
                    
                    if (string.IsNullOrEmpty(receiptId))
                    {
                        _logger.LogWarning("Goods receipt missing ReceiptId, skipping");
                        errorCount++;
                        continue;
                    }

                    // Check if already uploaded
                    var uploadStatus = await _dbContext.GoodsReceiptUploadStatuses
                        .FirstOrDefaultAsync(g => g.CompanyName == _company.CompanyName && g.P4WReceiptId == receiptId);

                    if (uploadStatus != null && uploadStatus.Status == "Success")
                    {
                        _logger.LogDebug("Goods receipt {ReceiptId} already uploaded, skipping", receiptId);
                        continue;
                    }

                    // Map to SAP Goods Receipt PO structure
                    var sapGoodsReceipt = MapToSAPGoodsReceipt(receipt);

                    if (!_options.DryRun)
                    {
                        _logger.LogInformation("Uploading goods receipt {ReceiptId} to SAP", receiptId);
                        
                        // POST to Service Layer
                        var sapResult = await _serviceLayer.PostAsync<Dictionary<string, object>>("GoodsReceiptPOs", sapGoodsReceipt);
                        
                        if (sapResult != null && sapResult.ContainsKey("DocEntry"))
                        {
                            var docEntry = SyncHelper.SafeIntParse(sapResult["DocEntry"]);
                            
                            // Update upload status
                            if (uploadStatus == null)
                            {
                                uploadStatus = new GoodsReceiptUploadStatus
                                {
                                    CompanyName = _company.CompanyName,
                                    P4WReceiptId = receiptId
                                };
                                _dbContext.GoodsReceiptUploadStatuses.Add(uploadStatus);
                            }

                            uploadStatus.UploadDateTime = DateTime.UtcNow;
                            uploadStatus.Status = "Success";
                            uploadStatus.SAPDocEntry = docEntry;
                            uploadStatus.ErrorMessage = null;

                            // Mark as processed in P4W
                            await _p4wClient.MarkGoodsReceiptAsProcessedAsync(receiptId);
                            
                            _logger.LogInformation("Successfully uploaded goods receipt {ReceiptId} as SAP DocEntry {DocEntry}", 
                                receiptId, docEntry);
                        }
                        else
                        {
                            throw new Exception("Failed to get DocEntry from SAP response");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("[DRY RUN] Would upload goods receipt {ReceiptId} to SAP", receiptId);
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading goods receipt {ReceiptId}", 
                        receipt.GetValueOrDefault("ReceiptId", "Unknown"));
                    
                    // Log error to database
                    var receiptId = receipt.GetValueOrDefault("ReceiptId", "")?.ToString();
                    if (!string.IsNullOrEmpty(receiptId) && !_options.DryRun)
                    {
                        var uploadStatus = await _dbContext.GoodsReceiptUploadStatuses
                            .FirstOrDefaultAsync(g => g.CompanyName == _company.CompanyName && g.P4WReceiptId == receiptId);
                        
                        if (uploadStatus == null)
                        {
                            uploadStatus = new GoodsReceiptUploadStatus
                            {
                                CompanyName = _company.CompanyName,
                                P4WReceiptId = receiptId
                            };
                            _dbContext.GoodsReceiptUploadStatuses.Add(uploadStatus);
                        }

                        uploadStatus.UploadDateTime = DateTime.UtcNow;
                        uploadStatus.Status = "Failed";
                        uploadStatus.ErrorMessage = ex.Message;
                    }
                    
                    errorCount++;
                }
            }

            if (!_options.DryRun)
            {
                await _dbContext.SaveChangesAsync();
            }

            result.RecordsProcessed = processedCount;
            result.ErrorCount = errorCount;
            result.Success = errorCount == 0;
            result.PartialSuccess = errorCount > 0 && processedCount > 0;

            _logger.LogInformation("GoodsReceiptUpload completed - Processed: {Processed}, Errors: {Errors}", 
                processedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in GoodsReceiptUpload");
            result.Success = false;
            result.ErrorCount = 1;
        }

        return result;
    }

    private async Task<DateTime> GetLastUploadTime()
    {
        var lastUpload = await _dbContext.GoodsReceiptUploadStatuses
            .Where(g => g.CompanyName == _company.CompanyName && g.Status == "Success")
            .MaxAsync(g => (DateTime?)g.UploadDateTime);

        return lastUpload ?? DateTime.UtcNow.AddDays(-7); // Default to 7 days ago
    }

    private Dictionary<string, object> MapToSAPGoodsReceipt(Dictionary<string, object> p4wReceipt)
    {
        // Extract header and lines from P4W receipt
        var lines = p4wReceipt.GetValueOrDefault("Lines", new List<Dictionary<string, object>>()) as List<Dictionary<string, object>> ?? new List<Dictionary<string, object>>();
        
        var mappedLines = lines.Select(line => new Dictionary<string, object>
        {
            ["ItemCode"] = line.GetValueOrDefault("ItemCode", ""),
            ["Quantity"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("Quantity", 0m)),
            ["Price"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("Price", 0m)),
            ["WarehouseCode"] = line.GetValueOrDefault("WarehouseCode", _company.Settings.DefaultWarehouseCode),
            ["BaseType"] = 22, // Purchase Order
            ["BaseEntry"] = SyncHelper.SafeIntParse(line.GetValueOrDefault("PODocEntry", 0)),
            ["BaseLine"] = SyncHelper.SafeIntParse(line.GetValueOrDefault("POLineNum", 0))
        }).ToList();

        return new Dictionary<string, object>
        {
            ["CardCode"] = p4wReceipt.GetValueOrDefault("VendorCode", ""),
            ["DocDate"] = SyncHelper.FormatSapDate(DateTime.Parse(p4wReceipt.GetValueOrDefault("ReceiptDate", DateTime.Now.ToString())?.ToString() ?? DateTime.Now.ToString())),
            ["Comments"] = $"P4W Receipt: {p4wReceipt.GetValueOrDefault("ReceiptId", "")}",
            ["DocumentLines"] = mappedLines
        };
    }
}