using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaProject
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("isBillable")]
    public bool IsBillable { get; init; }

    [JsonPropertyName("billingType")]
    public int BillingType { get; init; }

    [JsonPropertyName("projectManagers")]
    public IReadOnlyList<KekaProjectManager> ProjectManagers { get; init; } = [];

    [JsonPropertyName("customAttributes")]
    public Dictionary<string, object?> CustomAttributes { get; init; } = [];
}

public sealed class KekaProjectManager
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; init; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;
}

public sealed class KekaTask
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }
}
