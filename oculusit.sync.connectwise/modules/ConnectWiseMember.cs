using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.modules;

public sealed class ConnectWiseMember
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    [JsonPropertyName("officeEmail")]
    public string? OfficeEmail { get; init; }

    [JsonPropertyName("defaultEmail")]
    public string? DefaultEmail { get; init; }

    [JsonPropertyName("inactiveFlag")]
    public bool? InactiveFlag { get; init; }

    [JsonIgnore]
    public string Email => (OfficeEmail ?? DefaultEmail ?? string.Empty).Trim();
}
