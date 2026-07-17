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

    [JsonPropertyName("workRole")]
    public ConnectWiseTimeEntryRef? WorkRole { get; init; }

    [JsonPropertyName("workType")]
    public ConnectWiseTimeEntryRef? WorkType { get; init; }

    [JsonPropertyName("chargeToType")]
    public string ChargeToType { get; init; } = string.Empty;

    [JsonPropertyName("chargeToId")]
    public int? ChargeToId { get; init; }

    [JsonPropertyName("timeStart")]
    public DateTime? TimeStart { get; init; }

    [JsonPropertyName("timeEnd")]
    public DateTime? TimeEnd { get; init; }

    [JsonPropertyName("actualHours")]
    public decimal? ActualHours { get; init; }

    [JsonPropertyName("hoursBilled")]
    public decimal? HoursBilled { get; init; }

    [JsonPropertyName("hoursDeduct")]
    public decimal? HoursDeduct { get; init; }

    [JsonPropertyName("agreement")]
    public ConnectWiseTimeEntryRef? Agreement { get; init; }

    [JsonPropertyName("ticket")]
    public ConnectWiseTimeEntryRef? Ticket { get; init; }

    [JsonPropertyName("phase")]
    public ConnectWiseTimeEntryRef? Phase { get; init; }

    [JsonPropertyName("billableOption")]
    public string BillableOption { get; init; } = string.Empty;

    [JsonPropertyName("taxable")]
    public bool? Taxable { get; init; }

    [JsonPropertyName("invoiceId")]
    public int? InvoiceId { get; init; }

    [JsonPropertyName("enteredBy")]
    public string? EnteredBy { get; init; }

    [JsonPropertyName("enteredDate")]
    public DateTime? EnteredDate { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    [JsonPropertyName("internalNotes")]
    public string? InternalNotes { get; init; }

    [JsonPropertyName("emailResourceFlag")]
    public bool? EmailResourceFlag { get; init; }

    [JsonPropertyName("emailContactFlag")]
    public bool? EmailContactFlag { get; init; }

    [JsonPropertyName("emailCcFlag")]
    public bool? EmailCcFlag { get; init; }

    [JsonPropertyName("hourlyRate")]
    public decimal? HourlyRate { get; init; }

    [JsonPropertyName("mobileGuid")]
    public string? MobileGuid { get; init; }

    [JsonPropertyName("_info")]
    public ConnectWiseTimeEntryInfo? Info { get; init; }

    public DateTime? LastUpdated => Info?.LastUpdated;

    [JsonPropertyName("status")]
    public required string Status { get; init; }
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

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    [JsonPropertyName("dateEntered")]
    public DateTime? DateEntered { get; init; }

    [JsonPropertyName("enteredBy")]
    public string? EnteredBy { get; init; }
}

/// <summary>Generic id/name reference used for workRole, workType, agreement, ticket, phase, etc.</summary>
public sealed class ConnectWiseTimeEntryRef
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
