using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.modules;

public sealed class ConnectWiseCompany
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public ConnectWiseCompanyStatus? Status { get; init; }

    [JsonPropertyName("type")]
    public ConnectWiseCompanyType? Type { get; init; }

    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; init; } = string.Empty;

    [JsonPropertyName("addressLine2")]
    public string AddressLine2 { get; init; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("zip")]
    public string Zip { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public ConnectWiseCompanyCountry? Country { get; init; }

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; init; } = string.Empty;

    [JsonPropertyName("faxNumber")]
    public string FaxNumber { get; init; } = string.Empty;

    [JsonPropertyName("website")]
    public string Website { get; init; } = string.Empty;

    [JsonPropertyName("invoiceCCEmailAddress")]
    public string InvoiceCCEmailAddress { get; init; } = string.Empty;

    [JsonPropertyName("dateEntered")]
    public DateTime? DateEntered { get; init; }

    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; init; }
}

public sealed class ConnectWiseCompanyStatus
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseCompanyType
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseCompanyCountry
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
