using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.modules;

public sealed class ConnectWiseProjectTeamMember
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("projectId")]
    public int ProjectId { get; init; }

    [JsonPropertyName("hours")]
    public decimal? Hours { get; init; }

    [JsonPropertyName("member")]
    public ConnectWiseProjectTeamMemberMember? Member { get; init; }

    [JsonPropertyName("projectRole")]
    public ConnectWiseProjectTeamMemberRole? ProjectRole { get; init; }

    [JsonPropertyName("_info")]
    public ConnectWiseProjectTeamMemberInfo? Info { get; init; }

    public DateTime? LastUpdated => Info?.LastUpdated;
}

public sealed class ConnectWiseProjectTeamMemberMember
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("_info")]
    public ConnectWiseProjectTeamMemberMemberInfo? Info { get; init; }
}

public sealed class ConnectWiseProjectTeamMemberMemberInfo
{
    [JsonPropertyName("member_href")]
    public string MemberHref { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectTeamMemberRole
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;
}

public sealed class ConnectWiseProjectTeamMemberInfo
{
    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; init; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }
}
