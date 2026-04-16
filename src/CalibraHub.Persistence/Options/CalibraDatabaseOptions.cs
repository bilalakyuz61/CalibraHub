namespace CalibraHub.Persistence.Options;

public sealed class CalibraDatabaseOptions
{
    public const string SectionName = "CalibraDatabase";
    public string ConnectionString { get; init; } = string.Empty;
    public string Schema { get; init; } = "dbo";
    public bool AutoCreateDatabaseOnStartup { get; init; } = true;
}
