using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Utilities;

namespace P4WIntegration.Workers;

public class SalesOrderSyncWorker : IWorker
{
    private readonly ILogger<SalesOrderSyncWorker> _logger;
    private readonly ServiceLayerClient _serviceLayer;
    private readonly P4WarehouseClient _p4wClient;
    private readonly IntegrationDbContext _dbContext;
    private readonly CommandLineOptions _options;
    private readonly Company _company;

    public SalesOrderSyncWorker(
        ILogger<SalesOrderSyncWorker> logger,
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
        _logger.LogInformation("Starting SalesOrderSync for company {Company}", _company.CompanyName);

        var result = new WorkerResult { Success = true };
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            // For Sales Orders, we sync open orders to create pick tickets
            var sqlQuery = @"
                SELECT 
                    DocEntry, DocNum, DocDate, DocDueDate,
                    CardCode, CardName, DocTotal, DocStatus,
                    Comments, DocCur, DocRate, ShipToCode,
                    Address, Address2
                FROM ORDR 
                WHERE DocStatus = 'O'";

            var salesOrders = await _serviceLayer.ExecuteSqlQueryAsync(sqlQuery);
            
            if (salesOrders == null || salesOrders.Count == 0)
            {
                _logger.LogInformation("No open sales orders to sync");
                return result;
            }

            _logger.LogInformation("Found {Count} open sales orders to process", salesOrders.Count);

            if (_options.Limit.HasValue)
            {
                salesOrders = salesOrders.Take(_options.Limit.Value).ToList();
                _logger.LogInformation("Limited to {Limit} sales orders", _options.Limit.Value);
            }

            foreach (var soHeader in salesOrders)
            {
                try
                {
                    var docEntry = SyncHelper.SafeIntParse(soHeader.GetValueOrDefault("DocEntry", 0));
                    var docNum = SyncHelper.SafeIntParse(soHeader.GetValueOrDefault("DocNum", 0));
                    
                    if (docEntry == 0)
                    {
                        _logger.LogWarning("Sales order missing DocEntry, skipping");
                        errorCount++;
                        continue;
                    }

                    // Get SO lines
                    var linesQuery = $@"
                        SELECT 
                            DocEntry, LineNum, ItemCode, Dscription,
                            Quantity, Price, LineTotal, WhsCode,
                            ShipDate, OpenQty
                        FROM RDR1
                        WHERE DocEntry = {docEntry}";

                    var soLines = await _serviceLayer.ExecuteSqlQueryAsync(linesQuery);

                    if (soLines == null || soLines.Count == 0)
                    {
                        _logger.LogWarning("Sales order {DocNum} has no lines, skipping", docNum);
                        errorCount++;
                        continue;
                    }

                    // Calculate hash for change detection
                    var soData = new { Header = soHeader, Lines = soLines };
                    var currentHash = SyncHelper.CalculateHash(soData);

                    // Check if SO has changed
                    var syncStatus = await _dbContext.SalesOrderSyncStatuses
                        .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.DocEntry == docEntry);

                    if (syncStatus != null && syncStatus.SyncHash == currentHash)
                    {
                        _logger.LogDebug("Sales order {DocNum} unchanged, skipping", docNum);
                        continue;
                    }

                    // Map to P4W pick ticket format
                    var p4wPickTicket = MapToP4WPickTicket(soHeader, soLines);

                    if (!_options.DryRun)
                    {
                        _logger.LogInformation("Creating pick ticket for sales order {DocNum} in P4W", docNum);
                        var success = await _p4wClient.CreatePickTicketAsync(p4wPickTicket);
                        if (!success)
                        {
                            throw new Exception($"Failed to create pick ticket for sales order {docNum} in P4W");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("[DRY RUN] Would create pick ticket for sales order {DocNum} in P4W", docNum);
                    }

                    // Update sync status
                    if (!_options.DryRun)
                    {
                        if (syncStatus == null)
                        {
                            syncStatus = new SalesOrderSyncStatus
                            {
                                CompanyName = _company.CompanyName,
                                DocEntry = docEntry,
                                DocNum = docNum
                            };
                            _dbContext.SalesOrderSyncStatuses.Add(syncStatus);
                        }

                        syncStatus.LastSyncDateTime = DateTime.UtcNow;
                        syncStatus.SyncHash = currentHash;
                        syncStatus.Status = "Success";
                        syncStatus.DocNum = docNum;
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing sales order {DocNum}", 
                        soHeader.GetValueOrDefault("DocNum", "Unknown"));
                    errorCount++;
                }
            }

            if (!_options.DryRun)
            {
                await _dbContext.SaveChangesAsync();
                await UpdateSyncState();
            }

            result.RecordsProcessed = processedCount;
            result.ErrorCount = errorCount;
            result.Success = errorCount == 0;
            result.PartialSuccess = errorCount > 0 && processedCount > 0;

            _logger.LogInformation("SalesOrderSync completed - Processed: {Processed}, Errors: {Errors}", 
                processedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in SalesOrderSync");
            result.Success = false;
            result.ErrorCount = 1;
        }

        return result;
    }

    private Dictionary<string, object> MapToP4WPickTicket(Dictionary<string, object> header, List<Dictionary<string, object>> lines)
    {
        var mappedLines = lines.Select(line => new Dictionary<string, object>
        {
            ["LineNum"] = SyncHelper.SafeIntParse(line.GetValueOrDefault("LineNum", 0)),
            ["ItemCode"] = line.GetValueOrDefault("ItemCode", ""),
            ["Description"] = line.GetValueOrDefault("Dscription", ""),
            ["Quantity"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("Quantity", 0m)),
            ["Price"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("Price", 0m)),
            ["LineTotal"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("LineTotal", 0m)),
            ["WarehouseCode"] = line.GetValueOrDefault("WhsCode", ""),
            ["ShipDate"] = SyncHelper.ParseSapDate(line.GetValueOrDefault("ShipDate", "")?.ToString()),
            ["OpenQuantity"] = SyncHelper.SafeDecimalParse(line.GetValueOrDefault("OpenQty", 0m))
        }).ToList();

        return new Dictionary<string, object>
        {
            ["DocEntry"] = SyncHelper.SafeIntParse(header.GetValueOrDefault("DocEntry", 0)),
            ["DocNum"] = SyncHelper.SafeIntParse(header.GetValueOrDefault("DocNum", 0)),
            ["DocDate"] = SyncHelper.ParseSapDate(header.GetValueOrDefault("DocDate", "")?.ToString()),
            ["DocDueDate"] = SyncHelper.ParseSapDate(header.GetValueOrDefault("DocDueDate", "")?.ToString()),
            ["CardCode"] = header.GetValueOrDefault("CardCode", ""),
            ["CardName"] = header.GetValueOrDefault("CardName", ""),
            ["DocTotal"] = SyncHelper.SafeDecimalParse(header.GetValueOrDefault("DocTotal", 0m)),
            ["Comments"] = header.GetValueOrDefault("Comments", ""),
            ["ShipToCode"] = header.GetValueOrDefault("ShipToCode", ""),
            ["ShipToAddress"] = header.GetValueOrDefault("Address", ""),
            ["ShipToAddress2"] = header.GetValueOrDefault("Address2", ""),
            ["Lines"] = mappedLines,
            ["CompanyName"] = _company.CompanyName,
            ["ClientName"] = _company.SapB1.ClientName,
            ["Priority"] = "Normal",
            ["PickTicketType"] = "SalesOrder"
        };
    }

    private async Task UpdateSyncState()
    {
        var syncState = await _dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "SalesOrder");

        if (syncState == null)
        {
            syncState = new SyncState
            {
                CompanyName = _company.CompanyName,
                EntityType = "SalesOrder"
            };
            _dbContext.SyncStates.Add(syncState);
        }

        syncState.LastSyncDateTime = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
}