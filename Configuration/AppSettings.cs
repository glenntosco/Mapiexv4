namespace P4WIntegration.Configuration;

public class AppSettings
{
    public SqlServerSettings SqlServer { get; set; } = new();
    public AzureStorageSettings AzureStorage { get; set; } = new();
}

public class SqlServerSettings
{
    public string ServerName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MasterDatabase { get; set; } = "master";
    public bool TrustServerCertificate { get; set; } = true;
    public bool Encrypt { get; set; } = true;
    public int CommandTimeout { get; set; } = 120;
    public int ConnectionTimeout { get; set; } = 30;
}

public class AzureStorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool UseAzureStorage { get; set; }
    public int UploadTimeoutSeconds { get; set; } = 300;
    public int MaxFileSizeBytes { get; set; } = 10485760;
}