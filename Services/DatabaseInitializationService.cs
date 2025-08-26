using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;

namespace P4WIntegration.Services;

public class DatabaseInitializationService
{
    private readonly ILogger<DatabaseInitializationService> _logger;
    private readonly AppSettings _appSettings;
    private readonly IServiceProvider _serviceProvider;

    public DatabaseInitializationService(
        ILogger<DatabaseInitializationService> logger,
        AppSettings appSettings,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _appSettings = appSettings;
        _serviceProvider = serviceProvider;
    }

    public async Task InitializeDatabaseAsync(Company company)
    {
        var databaseName = $"P4I_{company.SapB1.ClientName}";
        
        _logger.LogInformation("Initializing database {DatabaseName} for company {CompanyName}", 
            databaseName, company.CompanyName);
        
        try
        {
            // Build connection string for this company's database
            var connectionString = BuildConnectionString(company.SapB1.ClientName);
            
            // Create database if it doesn't exist and apply migrations
            await EnsureDatabaseExistsAsync(connectionString, databaseName);
            
            // Seed initial data if needed
            await SeedInitialDataAsync(connectionString, company.CompanyName);
            
            _logger.LogInformation("Successfully initialized database {DatabaseName} for company {CompanyName}", 
                databaseName, company.CompanyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database {DatabaseName} for company {CompanyName}", 
                databaseName, company.CompanyName);
            throw;
        }
    }

    private string BuildConnectionString(string clientName)
    {
        return $"Server={_appSettings.SqlServer.ServerName};" +
               $"Database=P4I_{clientName};" +
               $"User Id={_appSettings.SqlServer.UserId};" +
               $"Password={_appSettings.SqlServer.Password};" +
               $"TrustServerCertificate={_appSettings.SqlServer.TrustServerCertificate};" +
               $"MultipleActiveResultSets=true;" +
               $"Connection Timeout={_appSettings.SqlServer.ConnectionTimeout};" +
               $"Encrypt={_appSettings.SqlServer.Encrypt}";
    }

    private async Task EnsureDatabaseExistsAsync(string connectionString, string databaseName)
    {
        // Create DbContext with the company-specific connection string
        var optionsBuilder = new DbContextOptionsBuilder<IntegrationDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.CommandTimeout(_appSettings.SqlServer.CommandTimeout);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        });
        
        using var dbContext = new IntegrationDbContext(optionsBuilder.Options);
        
        try
        {
            // Try to apply migrations first (creates database if it doesn't exist)
            _logger.LogInformation("Attempting to apply migrations for database {DatabaseName}", databaseName);
            await dbContext.Database.MigrateAsync();
            _logger.LogInformation("Successfully applied migrations to database {DatabaseName}", databaseName);
        }
        catch (Exception migrationEx)
        {
            _logger.LogWarning(migrationEx, "Could not apply migrations for database {DatabaseName}, attempting EnsureCreated", databaseName);
            
            try
            {
                // Fallback to EnsureCreated if migrations fail
                await dbContext.Database.EnsureCreatedAsync();
                _logger.LogInformation("Database {DatabaseName} created using EnsureCreated", databaseName);
            }
            catch (Exception ensureEx)
            {
                _logger.LogError(ensureEx, "Failed to create database {DatabaseName}", databaseName);
                throw;
            }
        }
    }

    private async Task SeedInitialDataAsync(string connectionString, string companyName)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IntegrationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        
        using var dbContext = new IntegrationDbContext(optionsBuilder.Options);
        
        try
        {
            // Check if initial data already exists
            var hasData = await dbContext.SyncStates.AnyAsync(s => s.CompanyName == companyName && s.EntityType == "InitialDataSeeded");
            
            if (!hasData)
            {
                // Add initial sync state
                var initialState = new SyncState
                {
                    CompanyName = companyName,
                    EntityType = "InitialDataSeeded",
                    LastSyncDateTime = DateTime.UtcNow
                };
                
                dbContext.SyncStates.Add(initialState);
                
                // Add initial log entry
                var systemLog = new IntegrationLog
                {
                    Level = "Information",
                    Message = $"Database P4I_{companyName} created and initialized",
                    CompanyName = companyName,
                    Operation = "DatabaseInitialization",
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid(),
                    ExecutionId = Guid.NewGuid()
                };
                
                dbContext.IntegrationLogs.Add(systemLog);
                
                await dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Seeded initial data for company {CompanyName}", companyName);
            }
            else
            {
                _logger.LogDebug("Initial data already exists for company {CompanyName}", companyName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed initial data for company {CompanyName}", companyName);
            // Non-critical error, don't throw
        }
    }

    public async Task<bool> VerifyDatabaseHealthAsync(string clientName)
    {
        try
        {
            var connectionString = BuildConnectionString(clientName);
            var optionsBuilder = new DbContextOptionsBuilder<IntegrationDbContext>();
            optionsBuilder.UseSqlServer(connectionString);
            
            using var dbContext = new IntegrationDbContext(optionsBuilder.Options);
            
            // Test basic connectivity
            var canConnect = await dbContext.Database.CanConnectAsync();
            
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database P4I_{ClientName}", clientName);
                return false;
            }
            
            // Verify we can query tables through Entity Framework
            try
            {
                // Use Entity Framework to verify tables are accessible
                _ = await dbContext.SyncStates.CountAsync();
                _ = await dbContext.IntegrationLogs.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot query tables in database P4I_{ClientName}", clientName);
                return false;
            }
            
            _logger.LogDebug("Database health check passed for P4I_{ClientName}", clientName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed for P4I_{ClientName}", clientName);
            return false;
        }
    }
}