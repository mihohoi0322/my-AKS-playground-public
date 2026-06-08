namespace HRSystem.Shared.Cosmos;

public class CosmosSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "hrsystem";
    public string? ConnectionString { get; set; }
}
