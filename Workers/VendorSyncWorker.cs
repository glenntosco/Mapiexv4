using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Utilities;

namespace P4WIntegration.Workers;

public class VendorSyncWorker : IWorker
{
    private readonly ILogger<VendorSyncWorker> _logger;
    private readonly ServiceLayerClient _serviceLayer;
    private readonly P4WarehouseClient _p4wClient;
    private readonly IntegrationDbContext _dbContext;
    private readonly CommandLineOptions _options;
    private readonly Company _company;

    public VendorSyncWorker(
        ILogger<VendorSyncWorker> logger,
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
        _logger.LogInformation("Starting VendorSync for company {Company}", _company.CompanyName);

        var result = new WorkerResult { Success = true };
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            var lastSync = await GetLastSyncDate();
            _logger.LogInformation("Last sync date: {LastSync}", lastSync);

            var sqlQuery = BuildVendorQuery(lastSync);
            var vendors = await _serviceLayer.ExecuteSqlQueryAsync(sqlQuery);
            
            if (vendors == null || vendors.Count == 0)
            {
                _logger.LogInformation("No vendors to sync");
                return result;
            }

            _logger.LogInformation("Found {Count} vendors to process", vendors.Count);

            if (_options.Limit.HasValue)
            {
                vendors = vendors.Take(_options.Limit.Value).ToList();
                _logger.LogInformation("Limited to {Limit} vendors", _options.Limit.Value);
            }

            foreach (var vendor in vendors)
            {
                try
                {
                    var cardCode = vendor.GetValueOrDefault("CardCode", "")?.ToString() ?? "";
                    
                    if (string.IsNullOrEmpty(cardCode))
                    {
                        _logger.LogWarning("Vendor missing CardCode, skipping");
                        errorCount++;
                        continue;
                    }

                    var currentHash = SyncHelper.CalculateHash(vendor);
                    var syncStatus = await _dbContext.VendorSyncStatuses
                        .FirstOrDefaultAsync(v => v.CompanyName == _company.CompanyName && v.CardCode == cardCode);

                    if (syncStatus != null && syncStatus.SyncHash == currentHash)
                    {
                        _logger.LogDebug("Vendor {CardCode} unchanged, skipping", cardCode);
                        continue;
                    }

                    var p4wVendor = MapToP4WVendor(vendor);

                    if (!_options.DryRun)
                    {
                        _logger.LogInformation("Syncing vendor {CardCode} to P4W", cardCode);
                        var success = await _p4wClient.UpsertVendorAsync(p4wVendor);
                        if (!success)
                        {
                            throw new Exception($"Failed to sync vendor {cardCode} to P4W");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("[DRY RUN] Would sync vendor {CardCode} to P4W", cardCode);
                    }

                    if (!_options.DryRun)
                    {
                        if (syncStatus == null)
                        {
                            syncStatus = new VendorSyncStatus
                            {
                                CompanyName = _company.CompanyName,
                                CardCode = cardCode
                            };
                            _dbContext.VendorSyncStatuses.Add(syncStatus);
                        }

                        syncStatus.LastSyncDateTime = DateTime.UtcNow;
                        syncStatus.SyncHash = currentHash;
                        syncStatus.Status = "Success";
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing vendor {CardCode}", 
                        vendor.GetValueOrDefault("CardCode", "Unknown"));
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

            _logger.LogInformation("VendorSync completed - Processed: {Processed}, Errors: {Errors}", 
                processedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in VendorSync");
            result.Success = false;
            result.ErrorCount = 1;
        }

        return result;
    }

    private async Task<DateTime> GetLastSyncDate()
    {
        if (_options.Force)
            return DateTime.MinValue;

        if (_options.FromDate.HasValue)
        {
            return _options.FromDate.Value;
        }

        var syncState = await _dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "Vendor");

        return syncState?.LastSyncDateTime ?? DateTime.MinValue;
    }

    private string BuildVendorQuery(DateTime lastSync)
    {
        var query = @"
            SELECT 
                CardCode, CardName, CardType,
                LicTradNum, Phone1, Phone2, Cellular, E_Mail,
                Address, City, County, ZipCode, Country,
                UpdateDate, CreateDate,
                ValidFor
            FROM OCRD 
            WHERE CardType = 'S'";

        if (lastSync > DateTime.MinValue)
        {
            query += $" AND (UpdateDate >= '{SyncHelper.FormatSapDate(lastSync)}' OR CreateDate >= '{SyncHelper.FormatSapDate(lastSync)}')";
        }

        if (_options.ToDate.HasValue)
        {
            query += $" AND UpdateDate <= '{SyncHelper.FormatSapDate(_options.ToDate.Value)}'";
        }

        return query;
    }

    private Dictionary<string, object> MapToP4WVendor(Dictionary<string, object> sapVendor)
    {
        return new Dictionary<string, object>
        {
            ["CardCode"] = sapVendor.GetValueOrDefault("CardCode", ""),
            ["CardName"] = sapVendor.GetValueOrDefault("CardName", ""),
            ["TaxId"] = sapVendor.GetValueOrDefault("LicTradNum", ""),
            ["Phone1"] = sapVendor.GetValueOrDefault("Phone1", ""),
            ["Phone2"] = sapVendor.GetValueOrDefault("Phone2", ""),
            ["Mobile"] = sapVendor.GetValueOrDefault("Cellular", ""),
            ["Email"] = sapVendor.GetValueOrDefault("E_Mail", ""),
            ["Address"] = sapVendor.GetValueOrDefault("Address", ""),
            ["City"] = sapVendor.GetValueOrDefault("City", ""),
            ["State"] = sapVendor.GetValueOrDefault("State", ""),
            ["ZipCode"] = sapVendor.GetValueOrDefault("ZipCode", ""),
            ["Country"] = sapVendor.GetValueOrDefault("Country", ""),
            ["IsActive"] = sapVendor.GetValueOrDefault("ValidFor", "Y")?.ToString() == "Y",
            ["IsFrozen"] = sapVendor.GetValueOrDefault("Frozen", "N")?.ToString() == "Y",
            ["CompanyName"] = _company.CompanyName,
            ["ClientName"] = _company.SapB1.ClientName
        };
    }

    private async Task UpdateSyncState()
    {
        var syncState = await _dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "Vendor");

        if (syncState == null)
        {
            syncState = new SyncState
            {
                CompanyName = _company.CompanyName,
                EntityType = "Vendor"
            };
            _dbContext.SyncStates.Add(syncState);
        }

        syncState.LastSyncDateTime = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
}