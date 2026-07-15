using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaEmployee
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("employeeNumber")]
    public string? EmployeeNumber { get; init; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    [JsonPropertyName("middleName")]
    public string? MiddleName { get; init; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("jobTitle")]
    public KekaEmployeeInfo? JobTitle { get; init; }

    [JsonPropertyName("reportsTo")]
    public KekaManagerInfo? ReportsTo { get; init; }

    [JsonPropertyName("l2Manager")]
    public KekaManagerInfo? L2Manager { get; init; }

    [JsonPropertyName("dottedLineManager")]
    public KekaManagerInfo? DottedLineManager { get; init; }

    [JsonPropertyName("gender")]
    public int? Gender { get; init; }

    [JsonPropertyName("joiningDate")]
    public DateTime? JoiningDate { get; init; }

    [JsonPropertyName("dateOfBirth")]
    public DateTime? DateOfBirth { get; init; }

    [JsonPropertyName("exitStatus")]
    public int? ExitStatus { get; init; }

    [JsonPropertyName("groups")]
    public IReadOnlyList<KekaGroupInfo> Groups { get; init; } = [];
}

public sealed class KekaEmployeeInfo
{
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

public sealed class KekaManagerInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public sealed class KekaGroupInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("groupType")]
    public int? GroupType { get; init; }
}

public class KekaEmployeeRequest
{
    [JsonPropertyName("employeeNumber")]
    public string? EmployeeNumber { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("middleName")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("mobileNumber")]
    public string? MobileNumber { get; set; }

    [JsonPropertyName("gender")]
    public int? Gender { get; set; }

    [JsonPropertyName("dateOfBirth")]
    public DateTime? DateOfBirth { get; set; }

    [JsonPropertyName("dateJoined")]
    public DateTime? DateJoined { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("businessUnit")]
    public string? BusinessUnit { get; set; }

    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("secondaryJobTitle")]
    public string? SecondaryJobTitle { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("legalEntity")]
    public string? LegalEntity { get; set; }

    [JsonPropertyName("nationality")]
    public string? Nationality { get; set; }

    [JsonPropertyName("reportingManager")]
    public string? ReportingManager { get; set; }
}

public class KekaEmployeeUpdateRequest
{
    [JsonPropertyName("employeeNumber")]
    public string? EmployeeNumber { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("reportingManager")]
    public string? ReportingManager { get; set; }
}


/// <summary>
/// Wraps the Keka API response for employee creation.
/// data contains the newly created Keka employee ID.
/// </summary>
public sealed class KekaCreateEmployeeResponse
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];

    [JsonPropertyName("data")]
    public string? Data { get; init; }
}