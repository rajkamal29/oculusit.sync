using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.modules;

/// <summary>
/// Represents an audit trail entry for a ConnectWise timesheet.
/// </summary>
public sealed class ConnectWiseTimesheetAuditTrail
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("member")]
    public ConnectWiseAuditMember? Member { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("oldValue")]
    public string? OldValue { get; init; }

    [JsonPropertyName("newValue")]
    public string? NewValue { get; init; }

    [JsonPropertyName("_info")]
    public Dictionary<string, string>? Info { get; init; }
}

public sealed class ConnectWiseAuditMember
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    [JsonPropertyName("_info")]
    public Dictionary<string, string>? Info { get; init; }
}

public sealed class ConnectWiseAuditMemberInfo
{
    [JsonPropertyName("member_href")]
    public string? MemberHref { get; init; }
}
