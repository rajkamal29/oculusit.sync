using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaProjectRequest
{
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("isBillable")]
    public bool IsBillable { get; init; }

    /// <summary>Project billing type.</summary>
    [JsonPropertyName("billingType")]
    public int BillingType { get; init; }

    [JsonPropertyName("projectManager")]
    public IReadOnlyList<string>? ProjectManager { get; init; }
}

public sealed class KekaProjectUpdateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; init; }

    [JsonPropertyName("projectManager")]
    public IReadOnlyList<string>? ProjectManager { get; init; }
}

public sealed class KekaTaskRequest
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    /// <summary>0 = Non-Billable, 1 = Billable.</summary>
    [JsonPropertyName("taskBillingType")]
    public int TaskBillingType { get; init; }
}

public sealed class KekaTaskUpdateRequest
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("taskBillingType")]
    public int? TaskBillingType { get; init; }

    [JsonPropertyName("assignedTo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AssignedTo { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("estimatedHours")]
    public double? EstimatedHours { get; init; }

    [JsonPropertyName("phaseId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhaseId { get; init; }
}
