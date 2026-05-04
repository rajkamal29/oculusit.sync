using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaClient
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

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

public sealed class KekaBillingInfo
{
    [JsonPropertyName("billingCurrencyId")]
    public object? BillingCurrencyId { get; init; }

    [JsonPropertyName("billingAddress")]
    public KekaBillingAddress? BillingAddress { get; init; }
}

public sealed class KekaBillingAddress
{
    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; init; } = string.Empty;

    [JsonPropertyName("addressLine2")]
    public string AddressLine2 { get; init; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; init; } = "US";

    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("zip")]
    public string Zip { get; init; } = string.Empty;
}