using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Utilities;

namespace P4WIntegration.Workers;

public class CustomerSyncWorker : IWorker
{
    private readonly ILogger<CustomerSyncWorker> _logger;
    private readonly ServiceLayerClient _serviceLayer;
    private readonly P4WarehouseClient _p4wClient;
    private readonly IntegrationDbContext _dbContext;
    private readonly CommandLineOptions _options;
    private readonly Company _company;

    public CustomerSyncWorker(
        ILogger<CustomerSyncWorker> logger,
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
        _logger.LogInformation("Starting CustomerSync for company {Company}", _company.CompanyName);

        var result = new WorkerResult { Success = true };
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            // Get last sync date
            var lastSync = await GetLastSyncDate();
            _logger.LogInformation("Last sync date: {LastSync}", lastSync);

            // Build SQL query
            var sqlQuery = BuildCustomerQuery(lastSync);
            
            // Execute query via Service Layer
            var customers = await _serviceLayer.ExecuteSqlQueryAsync(sqlQuery);
            
            if (customers == null || customers.Count == 0)
            {
                _logger.LogInformation("No customers to sync");
                return result;
            }

            _logger.LogInformation("Found {Count} customers to process", customers.Count);

            // Apply limit if specified
            if (_options.Limit.HasValue)
            {
                customers = customers.Take(_options.Limit.Value).ToList();
                _logger.LogInformation("Limited to {Limit} customers", _options.Limit.Value);
            }

            // Process customers in batches
            var batchSize = 200; // Customers are lighter than products
            var batches = new List<Dictionary<string, object>>();
            
            foreach (var customer in customers)
            {
                try
                {
                    var cardCode = customer.GetValueOrDefault("CardCode", "")?.ToString() ?? "";
                    
                    if (string.IsNullOrEmpty(cardCode))
                    {
                        _logger.LogWarning("Customer missing CardCode, skipping");
                        errorCount++;
                        continue;
                    }

                    // Calculate hash for change detection
                    var currentHash = SyncHelper.CalculateHash(customer);

                    // Check if customer has changed
                    var syncStatus = await _dbContext.CustomerSyncStatuses
                        .FirstOrDefaultAsync(c => c.CompanyName == _company.CompanyName && c.CardCode == cardCode);

                    if (syncStatus != null && syncStatus.SyncHash == currentHash)
                    {
                        _logger.LogDebug("Customer {CardCode} unchanged, skipping", cardCode);
                        continue;
                    }

                    // Map to P4W format
                    var p4wCustomer = MapToP4WCustomer(customer);
                    batches.Add(p4wCustomer);

                    // Update sync status in memory
                    if (syncStatus == null)
                    {
                        syncStatus = new CustomerSyncStatus
                        {
                            CompanyName = _company.CompanyName,
                            CardCode = cardCode
                        };
                        _dbContext.CustomerSyncStatuses.Add(syncStatus);
                    }

                    syncStatus.LastSyncDateTime = DateTime.UtcNow;
                    syncStatus.SyncHash = currentHash;
                    syncStatus.Status = "Pending";

                    processedCount++;

                    // Send batch when it reaches the batch size
                    if (batches.Count >= batchSize)
                    {
                        await SendCustomerBatch(batches);
                        batches.Clear();
                        
                        if (!_options.DryRun)
                        {
                            await _dbContext.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing customer {CardCode}", 
                        customer.GetValueOrDefault("CardCode", "Unknown"));
                    errorCount++;
                }
            }

            // Send remaining batch
            if (batches.Count > 0)
            {
                await SendCustomerBatch(batches);
                
                if (!_options.DryRun)
                {
                    await _dbContext.SaveChangesAsync();
                }
            }

            // Update sync state
            if (!_options.DryRun)
            {
                await UpdateSyncState();
            }

            result.RecordsProcessed = processedCount;
            result.ErrorCount = errorCount;
            result.Success = errorCount == 0;
            result.PartialSuccess = errorCount > 0 && processedCount > 0;

            _logger.LogInformation("CustomerSync completed - Processed: {Processed}, Errors: {Errors}", 
                processedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in CustomerSync");
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
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "Customer");

        return syncState?.LastSyncDateTime ?? DateTime.MinValue;
    }

    private string BuildCustomerQuery(DateTime lastSync)
    {
        var query = @"
            SELECT 
                CardCode, CardName, CardType,
                LicTradNum, Phone1, Phone2, Cellular, E_Mail,
                Address, City, County, ZipCode, Country,
                CreditLine, Balance, OrdersBal,
                UpdateDate, CreateDate,
                ValidFor
            FROM OCRD 
            WHERE CardType = 'C'";

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

    private Dictionary<string, object> MapToP4WCustomer(Dictionary<string, object> sapCustomer)
    {
        return new Dictionary<string, object>
        {
            ["CardCode"] = sapCustomer.GetValueOrDefault("CardCode", ""),
            ["CardName"] = sapCustomer.GetValueOrDefault("CardName", ""),
            ["TaxId"] = sapCustomer.GetValueOrDefault("LicTradNum", ""),
            ["Phone1"] = sapCustomer.GetValueOrDefault("Phone1", ""),
            ["Phone2"] = sapCustomer.GetValueOrDefault("Phone2", ""),
            ["Mobile"] = sapCustomer.GetValueOrDefault("Cellular", ""),
            ["Email"] = sapCustomer.GetValueOrDefault("E_Mail", ""),
            ["Address"] = sapCustomer.GetValueOrDefault("Address", ""),
            ["City"] = sapCustomer.GetValueOrDefault("City", ""),
            ["County"] = sapCustomer.GetValueOrDefault("County", ""),
            ["State"] = sapCustomer.GetValueOrDefault("State", ""),
            ["ZipCode"] = sapCustomer.GetValueOrDefault("ZipCode", ""),
            ["Country"] = sapCustomer.GetValueOrDefault("Country", ""),
            ["CreditLimit"] = SyncHelper.SafeDecimalParse(sapCustomer.GetValueOrDefault("CreditLine", 0m)),
            ["Balance"] = SyncHelper.SafeDecimalParse(sapCustomer.GetValueOrDefault("Balance", 0m)),
            ["OrdersBalance"] = SyncHelper.SafeDecimalParse(sapCustomer.GetValueOrDefault("OrdersBal", 0m)),
            ["IsActive"] = sapCustomer.GetValueOrDefault("ValidFor", "Y")?.ToString() == "Y",
            ["IsFrozen"] = sapCustomer.GetValueOrDefault("Frozen", "N")?.ToString() == "Y",
            ["CompanyName"] = _company.CompanyName,
            ["ClientName"] = _company.SapB1.ClientName
        };
    }

    private async Task SendCustomerBatch(List<Dictionary<string, object>> batch)
    {
        if (!_options.DryRun)
        {
            _logger.LogInformation("Sending batch of {Count} customers to P4W", batch.Count);
            var success = await _p4wClient.UpsertCustomerBatchAsync(batch);
            if (!success)
            {
                throw new Exception($"Failed to sync customer batch to P4W");
            }

            // Update status to success for all in batch
            foreach (var customer in batch)
            {
                var cardCode = customer["CardCode"].ToString();
                var syncStatus = await _dbContext.CustomerSyncStatuses
                    .FirstOrDefaultAsync(c => c.CompanyName == _company.CompanyName && c.CardCode == cardCode);
                if (syncStatus != null)
                {
                    syncStatus.Status = "Success";
                }
            }
        }
        else
        {
            _logger.LogInformation("[DRY RUN] Would send batch of {Count} customers to P4W", batch.Count);
        }
    }

    private async Task UpdateSyncState()
    {
        var syncState = await _dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "Customer");

        if (syncState == null)
        {
            syncState = new SyncState
            {
                CompanyName = _company.CompanyName,
                EntityType = "Customer"
            };
            _dbContext.SyncStates.Add(syncState);
        }

        syncState.LastSyncDateTime = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
}