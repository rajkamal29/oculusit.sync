namespace oculusit.sync.core.models;

/// <summary>
/// Well-known SyncType partition key values used in DynamoDB sync state records.
/// </summary>
public static class SyncTypes
{
    public const string Company             = "Company";
    public const string InitialCompany      = "InitialCompany";
    public const string Project             = "Project";
    public const string InitialProject      = "InitialProject";
    public const string Metadata            = "Metadata";
    public const string DefaultProjectRetry = "DefaultProjectRetry";

    /// <summary>Dedicated record that logs all company sync failures with id, name, and error message.</summary>
    public const string Failures = "Failures";

    /// <summary>Dedicated record for company transient timeout failures that should be retried on the next run.</summary>
    public const string RetryCompanies = "RetryCompanies";

    /// <summary>Dedicated record for project transient timeout failures that should be retried on the next run.</summary>
    public const string Retry = "Retry";
}
