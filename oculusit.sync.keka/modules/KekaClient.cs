using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaClient
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("billingAddress")]
    public KekaBillingAddress? BillingAddress { get; init; }

    [JsonPropertyName("clientContacts")]
    public IReadOnlyList<KekaClientContact> ClientContacts { get; init; } = [];

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("additionalFields")]
    public IReadOnlyList<KekaAdditionalField> AdditionalFields { get; init; } = [];
}

public sealed class KekaClientContact
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }
}

public sealed class KekaAdditionalField
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

public sealed class KekaBillingAddress
{
    [JsonPropertyName("addressLine1")]
    public string? AddressLine1 { get; init; }

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; init; }

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; init; } = "US";

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("zip")]
    public string? Zip { get; init; }
}

public sealed class KekaBillingInfo
{
    [JsonPropertyName("billingCurrencyId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BillingCurrencyId { get; init; }

    [JsonPropertyName("billingAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KekaBillingAddress? BillingAddress { get; init; }
}

public sealed class KekaBillingRole
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("billingRate")]
    public KekaBillingRate? BillingRate { get; init; }
}

public sealed class KekaBillingRate
{
    [JsonPropertyName("unit")]
    public int Unit { get; init; }

    [JsonPropertyName("rate")]
    public decimal Rate { get; init; }
}
