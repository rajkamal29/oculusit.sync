using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.modules;

public sealed class ConnectWiseTimeEntry
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>
    /// Enriched after time-entry fetch by calling ConnectWise /system/members/{id}.
    /// Not part of the /time/entries payload.
    /// </summary>
    public string? MemberEmail { get; set; }

    [JsonPropertyName("member")]
    public ConnectWiseTimeEntryMember? Member { get; init; }

    [JsonPropertyName("company")]
    public ConnectWiseTimeEntryCompany? Company { get; init; }

    [JsonPropertyName("project")]
    public ConnectWiseTimeEntryProject? Project { get; set; }

    [JsonPropertyName("chargeToType")]
    public string ChargeToType { get; init; } = string.Empty;

    [JsonPropertyName("chargeToId")]
    public int? ChargeToId { get; init; }

    [JsonPropertyName("timeStart")]
    public DateTime? TimeStart { get; init; }

    [JsonPropertyName("timeEnd")]
    public DateTime? TimeEnd { get; init; }

    [JsonPropertyName("hoursActual")]
    public decimal? HoursActual { get; init; }

    [JsonPropertyName("billableOption")]
    public string BillableOption { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    [JsonPropertyName("_info")]
    public ConnectWiseTimeEntryInfo? Info { get; init; }

    public DateTime? LastUpdated => Info?.LastUpdated;
}

public sealed class ConnectWiseTimeEntryMember
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseTimeEntryCompany
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseTimeEntryProject
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class ConnectWiseTimeEntryInfo
{
    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; init; }
}
