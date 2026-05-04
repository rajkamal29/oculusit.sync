using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

internal sealed class KekaCurrency
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
