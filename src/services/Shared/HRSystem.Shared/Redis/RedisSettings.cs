namespace HRSystem.Shared.Redis;

public class RedisSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 10000;
    public bool UseSsl { get; set; } = true;
    public bool UseEntraIdAuth { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string? ConnectionString { get; set; }
}
