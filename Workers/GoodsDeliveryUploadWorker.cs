using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Utilities;

namespace P4WIntegration.Workers;

public class GoodsDeliveryUploadWorker : IWorker
{
    private readonly ILogger<GoodsDeliveryUploadWorker> _logger;
    private readonly ServiceLayerClient _serviceLayer;
    private readonly P4WarehouseClient _p4wClient;
    private readonly IntegrationDbContext _dbContext;
    private readonly CommandLineOptions _options;
    private readonly Company _company;

    public GoodsDeliveryUploadWorker(
        ILogger<GoodsDeliveryUploadWorker> logger,
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
        _logger.LogInformation("Starting GoodsDeliveryUpload for company {Company}", _company.CompanyName);

        var result = new WorkerResult { Success = true };
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            // Get the last upload time
            var lastUpload = await GetLastUploadTime();
            _logger.LogInformation("Last upload time: {LastUpload}", lastUpload);

            // Query P4W for completed deliveries
            var completedDeliveries = await _p4wClient.GetCompletedDeliveriesAsync(lastUpload);
            
            if (completedDeliveries == null || completedDeliveries.Count == 0)
            {
                _logger.LogInformation("No completed deliveries to upload");
                return result;
            }

            _logger.LogInformation("Found {Count} completed deliveries to upload", completedDeliveries.Count);

            if (_options.Limit.HasValue)
            {
                completedDeliveries = completedDeliveries.Take(_options.Limit.Value).ToList();
                _logger.LogInformation("Limited to {Limit} deliveries", _options.Limit.Value);
            }

            foreach (var delivery in completedDeliveries)
            {
                try
                {
                    var deliveryId = delivery.GetValueOrDefault("DeliveryId", "")?.ToString() ?? "";
                    
                    if (string.IsNullOrEmpty(deliveryId))
                    {
                        _logger.LogWarning("Delivery missing DeliveryId, skipping");
                        errorCount++;
                        continue;
                    }

                    // Check if already uploaded
                    var uploadStatus = await _dbContext.GoodsDeliveryUploadStatuses
                        .FirstOrDefaultAsync(g => g.CompanyName == _company.CompanyName && g.P4WDeliveryId == deliveryId);

                    if (uploadStatus != null && uploadStatus.Status == "Success")
                    {
                        _logger.LogDebug("Delivery {DeliveryId} already uploaded, skipping", deliveryId);
                        continue;
                    }

                    // Map to SAP Delivery Note structure
                    var sapDeliveryNote = MapToSAPDeliveryNote(delivery);

                    if (!_options.DryRun)
                    {
                        _logger.LogInformation("Uploading delivery {DeliveryId} to SAP", deliveryId);
                        
                        // POST to Service Layer
                        var sapResult = await _serviceLayer.PostAsync<Dictionary<string, object>>("DeliveryNotes", sapDeliveryNote);
                        
                        if (sapResult != null && sapResult.ContainsKey("DocEntry"))
                        {
                            var docEntry = SyncHelper.SafeIntParse(sapResult["DocEntry"]);
                            
                            // Update upload status
                            if (uploadStatus == null)
                            {
                                uploadStatus = new GoodsDeliveryUploadStatus
                                {
                                    CompanyName = _company.CompanyName,
                                    P4WDeliveryId = deliveryId
                                };
                                _dbContext.GoodsDeliveryUploadStatuses.Add(uploadStatus);
                            }

                            uploadStatus.UploadDateTime = DateTime.UtcNow;
                            uploadStatus.Status = "Success";
                            uploadStatus.SAPDocEntry = docEntry;
                            uploadStatus.ErrorMessage = null;

                            // Mark as processed in P4W
                            await _p4wClient.MarkDeliveryAsProcessedAsync(deliveryId);
                            
                            _logger.LogInformation("Successfully uploaded delivery {DeliveryId} as SAP DocEntry {DocEntry}", 
                                deliveryId, docEntry);
                        }
                        else
                        {
                            throw new Exception("Failed to get DocEntry from SAP response");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("[DRY RUN] Would upload delivery {DeliveryId} to SAP", deliveryId);
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading delivery {DeliveryId}", 
                        delivery.GetValueOrDefault("DeliveryId", "Unknown"));
                    
                    // Log error to database
                    var deliveryId = delivery.GetValueOrDefault("DeliveryId", "")?.ToString();
                    if (!string.IsNullOrEmpty(deliveryId) && !_options.DryRun)
                    {
                        var uploadStatus = await _dbContext.GoodsDeliveryUploadStatuses
                            .FirstOrDefaultAsync(g => g.CompanyName == _company.CompanyName && g.P4WDeliveryId == deliveryId);
                        
                        if (uploadStatus == null)
                        {
                            uploadStatus = new GoodsDeliveryUploadStatus
                            {
                                CompanyName = _company.CompanyName,
                                P4WDeliveryId = deliveryId
                            };
                            _dbContext.GoodsDeliveryUploadStatuses.Add(uploadStatus);
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

            _logger.LogInformation("GoodsDeliveryUpload completed - Processed: {Processed}, Errors: {Errors}", 
                processedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in GoodsDeliveryUpload");
            result.Success = false;
            result.ErrorCount = 1;
        }

        return result;
    }

    private async Task<DateTime> GetLastUploadTime()
    {
        var lastUpload = await _dbContext.GoodsDeliveryUploadStatuses
            .Where(g => g.CompanyName == _company.CompanyName && g.Status == "Success")
            .MaxAsync(g => (DateTime?)g.UploadDateTime);

        return lastUpload ?? DateTime.UtcNow.AddDays(-7); // Default to 7 days ago
    }

    private Dictionary<string, object> MapToSAPDeliveryNote(Dictionary<string, object> p4wDelivery)
    {
        // Extract header and lines from P4W delivery
        var lines = p4wDelivery.GetValueOrDefault("Lines", new List<Dictionary<string, object>>()) as List<Dictionary<string, object>> ?? new List<Dictionary<string, object>>();
        
        var mappedLines = lines.Select(line => new Dictionary<string, object>
        {
            ["ItemCode"] = line.GetValueOrDefault("ItemCode", ""),
            ["Quantity"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("Quantity", 0m)),
            ["Price"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("Price", 0m)),
            ["WarehouseCode"] = line.GetValueOrDefault("WarehouseCode", _company.Settings.DefaultWarehouseCode),
            ["BaseType"] = 17, // Sales Order
            ["BaseEntry"] = SyncHelper.SafeIntParse(line.GetValueOrDefault("SODocEntry", 0)),
            ["BaseLine"] = SyncHelper.SafeIntParse(line.GetValueOrDefault("SOLineNum", 0))
        }).ToList();

        return new Dictionary<string, object>
        {
            ["CardCode"] = p4wDelivery.GetValueOrDefault("CustomerCode", ""),
            ["DocDate"] = SyncHelper.FormatSapDate(DateTime.Parse(p4wDelivery.GetValueOrDefault("DeliveryDate", DateTime.Now.ToString())?.ToString() ?? DateTime.Now.ToString())),
            ["Comments"] = $"P4W Delivery: {p4wDelivery.GetValueOrDefault("DeliveryId", "")}, Pick Ticket: {p4wDelivery.GetValueOrDefault("PickTicketId", "")}",
            ["DocumentLines"] = mappedLines
        };
    }
}