namespace P4WIntegration.Configuration;

public class CompanyConfig
{
    public List<Company> Companies { get; set; } = new();
    public List<Schedule> Schedules { get; set; } = new();
}

public class Company
{
    public string CompanyName { get; set; } = string.Empty;
    public SapB1Config SapB1 { get; set; } = new();
    public string P4WarehouseApiKey { get; set; } = string.Empty;
    public CompanySettings Settings { get; set; } = new();
}

public class SapB1Config
{
    public string ServiceLayerUrl { get; set; } = string.Empty;
    public string CompanyDb { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CompanySettings
{
    public int ProductBatchSize { get; set; } = 100;
    public string DefaultWarehouseCode { get; set; } = "01";
    public int LogRetentionDays { get; set; } = 30;
    public bool UseSqlStateManager { get; set; } = true;
    public bool UseUnitOfMeasure { get; set; } = false;
}

public class Schedule
{
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool RunOnStartup { get; set; }
    public string Interval { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string SyncMode { get; set; } = "Delta";
    public bool ForceSync { get; set; }
    public int BatchSize { get; set; } = 50;
}