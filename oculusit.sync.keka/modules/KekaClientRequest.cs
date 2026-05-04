using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaClientRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("phone")]
    public string Phone { get; init; } = string.Empty;

    [JsonPropertyName("website")]
    public string Website { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("billingInfo")]
    public KekaBillingInfo? BillingInfo { get; init; }
}
