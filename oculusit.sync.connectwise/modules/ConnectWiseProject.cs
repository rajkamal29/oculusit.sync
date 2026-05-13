using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.modules;

public sealed class ConnectWiseProject
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public ConnectWiseProjectStatus? Status { get; init; }

    [JsonPropertyName("company")]
    public ConnectWiseProjectCompany? Company { get; init; }

    [JsonPropertyName("contact")]
    public ConnectWiseProjectContact? Contact { get; init; }

    [JsonPropertyName("type")]
    public ConnectWiseProjectType? Type { get; init; }

    [JsonPropertyName("board")]
    public ConnectWiseProjectBoard? Board { get; init; }

    [JsonPropertyName("manager")]
    public ConnectWiseProjectMember? Manager { get; init; }

    [JsonPropertyName("estimatedStart")]
    public DateTime? EstimatedStart { get; init; }

    [JsonPropertyName("estimatedEnd")]
    public DateTime? EstimatedEnd { get; init; }

    [JsonPropertyName("actualStart")]
    public DateTime? ActualStart { get; init; }

    [JsonPropertyName("actualEnd")]
    public DateTime? ActualEnd { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    [JsonPropertyName("billingMethod")]
    public string BillingMethod { get; init; } = string.Empty;

    [JsonPropertyName("billingAmount")]
    public decimal? BillingAmount { get; init; }

    [JsonPropertyName("billingRate")]
    public string BillingRate { get; init; } = string.Empty;

    [JsonPropertyName("estimatedTimeCost")]
    public decimal? EstimatedTimeCost { get; init; }

    [JsonPropertyName("estimatedExpenseCost")]
    public decimal? EstimatedExpenseCost { get; init; }

    [JsonPropertyName("estimatedProductCost")]
    public decimal? EstimatedProductCost { get; init; }

    [JsonPropertyName("actualHours")]
    public decimal? ActualHours { get; init; }

    [JsonPropertyName("budgetHours")]
    public decimal? BudgetHours { get; init; }

    [JsonPropertyName("scheduledHours")]
    public decimal? ScheduledHours { get; init; }

    [JsonPropertyName("percentComplete")]
    public decimal? PercentComplete { get; init; }

    [JsonPropertyName("includeDependenciesFlag")]
    public bool IncludeDependenciesFlag { get; init; }

    [JsonPropertyName("includeEstimatesFlag")]
    public bool IncludeEstimatesFlag { get; init; }

    [JsonPropertyName("currency")]
    public ConnectWiseProjectCurrency? Currency { get; init; }

    [JsonPropertyName("location")]
    public ConnectWiseProjectLocation? Location { get; init; }

    [JsonPropertyName("department")]
    public ConnectWiseProjectDepartment? Department { get; init; }

    [JsonPropertyName("poNumber")]
    public string PoNumber { get; init; } = string.Empty;

    [JsonPropertyName("closedFlag")]
    public bool ClosedFlag { get; init; }

    [JsonPropertyName("billExpenses")]
    public string BillExpenses { get; init; } = string.Empty;

    [JsonPropertyName("_info")]
    public ConnectWiseProjectInfo? Info { get; init; }

    public DateTime? DateEntered => Info?.DateEntered;
    public DateTime? LastUpdated => Info?.LastUpdated;
}

public sealed class ConnectWiseProjectStatus
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectType
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectBoard
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectCompany
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectContact
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectMember
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectCurrency
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = string.Empty;

    [JsonPropertyName("isoCode")]
    public string IsoCode { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectLocation
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectDepartment
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectInfo
{
    [JsonPropertyName("dateEntered")]
    public DateTime? DateEntered { get; init; }

    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; init; }
}
