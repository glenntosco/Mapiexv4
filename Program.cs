using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P4WIntegration;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Workers;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using System.Collections.ObjectModel;
using System.Data;

// Generate execution ID for this run
var executionId = Guid.NewGuid();

// Parse command-line arguments
var options = ParseCommandLineArgs(args);

// Load configuration directly from config files
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile(options.ConfigFile, optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// Get settings from configuration
var appSettings = configuration.Get<AppSettings>() ?? new AppSettings();
var companyConfig = configuration.Get<CompanyConfig>() ?? new CompanyConfig();

// Use first company in config (or could be made configurable via environment variable)
var company = companyConfig.Companies.FirstOrDefault();
if (company == null)
{
    Console.WriteLine("No companies configured in config.json");
    return 2; // Configuration error
}

// Build connection string for logging database
var connectionString = $"Server={appSettings.SqlServer.ServerName};" +
                      $"Database=P4I_{company.SapB1.ClientName};" +
                      $"User Id={appSettings.SqlServer.UserId};" +
                      $"Password={appSettings.SqlServer.Password};" +
                      $"TrustServerCertificate={appSettings.SqlServer.TrustServerCertificate};" +
                      $"Encrypt={appSettings.SqlServer.Encrypt};" +
                      $"Connection Timeout={appSettings.SqlServer.ConnectionTimeout}";

// Configure SQL Server sink columns
var columnOptions = new ColumnOptions
{
    AdditionalColumns = new Collection<SqlColumn>
    {
        new SqlColumn { ColumnName = "ExecutionId", DataType = SqlDbType.UniqueIdentifier },
        new SqlColumn { ColumnName = "CompanyName", DataType = SqlDbType.NVarChar, DataLength = 50 },
        new SqlColumn { ColumnName = "Operation", DataType = SqlDbType.NVarChar, DataLength = 50 }
    }
};

columnOptions.Store.Remove(StandardColumn.Properties);
columnOptions.Store.Remove(StandardColumn.MessageTemplate);

// Configure Serilog with debug level for troubleshooting
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ExecutionId", executionId)
    .Enrich.WithProperty("CompanyName", company.CompanyName)
    .Enrich.WithProperty("Operation", "All")
    .WriteTo.Console()
    .WriteTo.File(
        $"logs/p4w-{company.CompanyName}-{DateTime.Now:yyyy-MM-dd}.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .WriteTo.MSSqlServer(
        connectionString: connectionString,
        sinkOptions: new MSSqlServerSinkOptions
        {
            TableName = "IntegrationLogs",
            AutoCreateSqlDatabase = false,
            AutoCreateSqlTable = false
        },
        columnOptions: columnOptions)
    .CreateLogger();

try
{
    Log.Information("Starting P4W Integration - Company: {Company}, ExecutionId: {ExecutionId}",
        company.CompanyName, executionId);

    // Build service collection
    var services = new ServiceCollection();

    // Add configuration
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton(appSettings);
    services.AddSingleton(companyConfig);
    services.AddSingleton(company);

    // Use parsed options from command line
    if (string.IsNullOrEmpty(options.Company))
    {
        options.Company = company.CompanyName;
    }
    services.AddSingleton(options);

    // Add logging
    services.AddLogging(builder =>
    {
        builder.ClearProviders();
        builder.AddSerilog();
    });

    // Add Entity Framework
    services.AddDbContext<IntegrationDbContext>(opt =>
        opt.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.CommandTimeout(appSettings.SqlServer.CommandTimeout);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        }));

    // Add Database Initialization Service
    services.AddScoped<DatabaseInitializationService>();

    // Add Service Layer client
    services.AddScoped<ServiceLayerClient>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<ServiceLayerClient>>();
        return new ServiceLayerClient(
            company.SapB1.ServiceLayerUrl,
            company.SapB1.CompanyDb,
            company.SapB1.UserName,
            company.SapB1.Password,
            logger);
    });

    // Add Azure Blob Service (conditional based on configuration)
    if (appSettings.AzureStorage?.UseAzureStorage == true && 
        !string.IsNullOrWhiteSpace(appSettings.AzureStorage?.ConnectionString) &&
        !appSettings.AzureStorage.ConnectionString.Contains("YOUR_ACCOUNT_KEY_HERE"))
    {
        services.AddScoped<AzureBlobService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<AzureBlobService>>();
            var uploadTimeout = TimeSpan.FromSeconds(appSettings.AzureStorage.UploadTimeoutSeconds > 0 ? appSettings.AzureStorage.UploadTimeoutSeconds : 300);
            return new AzureBlobService(
                appSettings.AzureStorage.ConnectionString,
                appSettings.AzureStorage.ContainerName ?? "product-images",
                appSettings.AzureStorage.BaseUrl ?? "https://p4software.blob.core.windows.net",
                logger,
                uploadTimeout,
                appSettings.AzureStorage.MaxFileSizeBytes > 0 ? appSettings.AzureStorage.MaxFileSizeBytes : 10485760);
        });
    }
    else
    {
        Log.Warning("Azure Blob Storage is not configured. Product image sync will be disabled.");
    }

    // Add workers
    services.AddScoped<ProductSyncWorker>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<ProductSyncWorker>>();
        var serviceLayer = provider.GetRequiredService<ServiceLayerClient>();
        var dbContext = provider.GetRequiredService<IntegrationDbContext>();
        var options = provider.GetRequiredService<CommandLineOptions>();
        var config = provider.GetRequiredService<CompanyConfig>();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var azureBlobService = provider.GetService<AzureBlobService>(); // Optional dependency
        
        return new ProductSyncWorker(logger, serviceLayer, dbContext, options, config, loggerFactory, azureBlobService);
    });
    services.AddScoped<CustomerSyncWorker>();
    services.AddScoped<VendorSyncWorker>();
    services.AddScoped<PurchaseOrderSyncWorker>();
    services.AddScoped<SalesOrderSyncWorker>();
    services.AddScoped<GoodsReceiptUploadWorker>();
    services.AddScoped<GoodsDeliveryUploadWorker>();

    // Build service provider
    var serviceProvider = services.BuildServiceProvider();

    // Initialize database for this company using the DatabaseInitializationService
    using (var scope = serviceProvider.CreateScope())
    {
        var dbInitService = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
        
        try
        {
            await dbInitService.InitializeDatabaseAsync(company);
            Log.Information("Database initialized successfully for company {CompanyName}", company.CompanyName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database for company {CompanyName}", company.CompanyName);
            return 3; // Database initialization error
        }
        
        // Verify database health
        var isHealthy = await dbInitService.VerifyDatabaseHealthAsync(company.SapB1.ClientName);
        if (!isHealthy)
        {
            Log.Error("Database health check failed for company {CompanyName}", company.CompanyName);
            return 3; // Database health check error
        }
    }

    // Create execution history entry
    using (var scope = serviceProvider.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var execution = new ExecutionHistory
        {
            ExecutionId = executionId,
            StartTime = DateTime.UtcNow,
            Operation = "All",
            CompanyName = company.CompanyName,
            Status = "Running",
            CommandLine = "Auto-run from config"
        };
        dbContext.ExecutionHistories.Add(execution);
        await dbContext.SaveChangesAsync();
    }

    // Set up cancellation token for graceful shutdown
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Log.Information("Shutdown signal received, stopping gracefully...");
    };

    // Check if we should run a single operation or continuous mode
    if (options.Operation != null && options.Operation != "All")
    {
        // Single operation mode
        Log.Information("Running single operation: {Operation}", options.Operation);
        
        using (var scope = serviceProvider.CreateScope())
        {
            try
            {
                var result = options.Operation.ToUpperInvariant() switch
                {
                    "PRODUCTSYNC" => await ExecuteWorker<ProductSyncWorker>(scope),
                    "CUSTOMERSYNC" => await ExecuteWorker<CustomerSyncWorker>(scope),
                    "VENDORSYNC" => await ExecuteWorker<VendorSyncWorker>(scope),
                    "PURCHASEORDERSYNC" => await ExecuteWorker<PurchaseOrderSyncWorker>(scope),
                    "SALESORDERSYNC" => await ExecuteWorker<SalesOrderSyncWorker>(scope),
                    "GOODSRECEIPTUPLOAD" => await ExecuteWorker<GoodsReceiptUploadWorker>(scope),
                    "GOODSDELIVERYUPLOAD" => await ExecuteWorker<GoodsDeliveryUploadWorker>(scope),
                    _ => new WorkerResult { Success = false, ErrorCount = 1 }
                };
                
                Log.Information("Operation {Operation} completed - Records: {Records}, Errors: {Errors}", 
                    options.Operation, result.RecordsProcessed, result.ErrorCount);
                    
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing operation {Operation}", options.Operation);
                return 1;
            }
        }
    }
    
    // Track last execution times for each schedule
    var lastExecutionTimes = new Dictionary<string, DateTime>();
    
    Log.Information("Starting continuous integration service - Press Ctrl+C to stop");
    
    // Main continuous execution loop
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            // Check each schedule to see if it should run
            var activeSchedules = companyConfig.Schedules.Where(s => s.Active).ToList();
            var executedAny = false;
            
            foreach (var schedule in activeSchedules)
            {
                var shouldRun = false;
                
                // Check if it's time to run this schedule
                if (!lastExecutionTimes.ContainsKey(schedule.Name))
                {
                    // First run - check RunOnStartup
                    shouldRun = schedule.RunOnStartup;
                }
                else if (TimeSpan.TryParse(schedule.Interval, out var interval))
                {
                    // Check if enough time has passed
                    var timeSinceLastRun = DateTime.UtcNow - lastExecutionTimes[schedule.Name];
                    shouldRun = timeSinceLastRun >= interval;
                }
                
                if (shouldRun)
                {
                    executedAny = true;
                    lastExecutionTimes[schedule.Name] = DateTime.UtcNow;
                    
                    using (var scope = serviceProvider.CreateScope())
                    {
                        try
                        {
                            Log.Information("Executing scheduled task: {TaskName}", schedule.Name);
                            
                            var result = schedule.Name.ToUpperInvariant() switch
                            {
                                "PRODUCTSYNC" => await ExecuteWorker<ProductSyncWorker>(scope),
                                "CUSTOMERSYNC" => await ExecuteWorker<CustomerSyncWorker>(scope),
                                "VENDORSYNC" => await ExecuteWorker<VendorSyncWorker>(scope),
                                "PURCHASEORDERSYNC" => await ExecuteWorker<PurchaseOrderSyncWorker>(scope),
                                "SALESORDERSYNC" => await ExecuteWorker<SalesOrderSyncWorker>(scope),
                                "GOODSRECEIPTUPLOAD" => await ExecuteWorker<GoodsReceiptUploadWorker>(scope),
                                "GOODSDELIVERYUPLOAD" => await ExecuteWorker<GoodsDeliveryUploadWorker>(scope),
                                _ => new WorkerResult { Success = false, ErrorCount = 1 }
                            };
                            
                            Log.Information("Completed {Name} - Records: {Records}, Errors: {Errors}", 
                                schedule.Name, result.RecordsProcessed, result.ErrorCount);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error executing scheduled task {TaskName}", schedule.Name);
                        }
                    }
                }
            }
            
            // If nothing was executed, wait a short time before checking again
            if (!executedAny)
            {
                await Task.Delay(5000, cts.Token); // Check every 5 seconds
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in main execution loop");
            await Task.Delay(10000, cts.Token); // Wait 10 seconds before retrying on error
        }
    }
    
    Log.Information("P4W Integration service stopped");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Fatal error in P4W Integration");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

CommandLineOptions ParseCommandLineArgs(string[] args)
{
    var options = new CommandLineOptions();
    
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i].ToLower())
        {
            case "--company":
            case "-c":
                if (i + 1 < args.Length)
                    options.Company = args[++i];
                break;
                
            case "--operation":
            case "-o":
                if (i + 1 < args.Length)
                    options.Operation = args[++i];
                break;
                
            case "--dryrun":
            case "--dry-run":
                options.DryRun = true;
                break;
                
            case "--verbose":
            case "-v":
                options.Verbose = true;
                break;
                
            case "--limit":
            case "-l":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var limit))
                    options.Limit = limit;
                break;
                
            case "--force":
            case "-f":
                options.Force = true;
                break;
                
            case "--from":
            case "--from-date":
                if (i + 1 < args.Length && DateTime.TryParse(args[++i], out var fromDate))
                    options.FromDate = fromDate;
                break;
                
            case "--to":
            case "--to-date":
                if (i + 1 < args.Length && DateTime.TryParse(args[++i], out var toDate))
                    options.ToDate = toDate;
                break;
                
            case "--config":
                if (i + 1 < args.Length)
                    options.ConfigFile = args[++i];
                break;
        }
    }
    
    return options;
}

async Task<WorkerResult> ExecuteWorker<TWorker>(IServiceScope scope) where TWorker : IWorker
{
    var worker = scope.ServiceProvider.GetRequiredService<TWorker>();
    return await worker.ExecuteAsync();
}

// This method is currently unused but preserved for future scheduled worker execution
    // async Task<WorkerResult> ExecuteActiveWorkersFromConfig(IServiceScope scope, CompanyConfig config)
    // {
    //     var totalRecords = 0;
    //     var totalErrors = 0;
    //     var allSuccess = true;
    // 
    //     // Get active schedules from config
    //     var activeSchedules = config.Schedules.Where(s => s.Active && s.RunOnStartup).ToList();
    // 
    //     Log.Information("Found {Count} active schedules to execute", activeSchedules.Count);
    // 
    //     foreach (var schedule in activeSchedules)
    //     {
    //         try
    //         {
    //             Log.Information("Executing {Name} worker", schedule.Name);
    //             
    //             // Map schedule name to worker type
    //             var result = schedule.Name.ToUpperInvariant() switch
    //             {
    //                 "PRODUCTSYNC" => await ExecuteWorker<ProductSyncWorker>(scope),
    //                 "CUSTOMERSYNC" => await ExecuteWorker<CustomerSyncWorker>(scope),
    //                 "VENDORSYNC" => await ExecuteWorker<VendorSyncWorker>(scope),
    //                 "PURCHASEORDERSYNC" => await ExecuteWorker<PurchaseOrderSyncWorker>(scope),
    //                 "SALESORDERSYNC" => await ExecuteWorker<SalesOrderSyncWorker>(scope),
    //                 "GOODSRECEIPTUPLOAD" => await ExecuteWorker<GoodsReceiptUploadWorker>(scope),
    //                 "GOODSDELIVERYUPLOAD" => await ExecuteWorker<GoodsDeliveryUploadWorker>(scope),
    //                 _ => new WorkerResult { Success = false, ErrorCount = 1 }
    //             };
    //             
    //             totalRecords += result.RecordsProcessed;
    //             totalErrors += result.ErrorCount;
    //             
    //             if (!result.Success)
    //                 allSuccess = false;
    //                 
    //             Log.Information("Completed {Name} - Records: {Records}, Errors: {Errors}", 
    //                 schedule.Name, result.RecordsProcessed, result.ErrorCount);
    //         }
    //         catch (Exception ex)
    //         {
    //             Log.Error(ex, "Error executing worker {WorkerName}", schedule.Name);
    //             totalErrors++;
    //             allSuccess = false;
    //         }
    //     }
    // 
    //     return new WorkerResult
    //     {
    //         Success = allSuccess,
    //         PartialSuccess = !allSuccess && totalRecords > 0,
    //         RecordsProcessed = totalRecords,
    //         ErrorCount = totalErrors
    //     };
    // }

// Worker interfaces and result class
public interface IWorker
{
    Task<WorkerResult> ExecuteAsync();
}

public class WorkerResult
{
    public bool Success { get; set; }
    public bool PartialSuccess { get; set; }
    public int RecordsProcessed { get; set; }
    public int ErrorCount { get; set; }
}