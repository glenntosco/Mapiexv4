namespace P4WIntegration.Models;

public class CommandLineOptions
{
    public string Company { get; set; } = string.Empty;
    public string Operation { get; set; } = "All";
    public bool DryRun { get; set; }
    public bool Verbose { get; set; }
    public int? Limit { get; set; }
    public string ConfigFile { get; set; } = "config.json";
    public bool Force { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}