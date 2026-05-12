namespace oculusit.sync.core.models;

/// <summary>
/// Well-known SyncType partition key values used in DynamoDB sync state records.
/// </summary>
public static class SyncTypes
{
    public const string Company  = "Company";
    public const string Project  = "Project";
    public const string Metadata = "Metadata";
}
