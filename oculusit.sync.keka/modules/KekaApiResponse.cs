using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

/// <summary>
/// Wraps all Keka API responses that return a single object under a "data" key.
/// </summary>
internal sealed class KekaDataResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

/// <summary>
/// Wraps the Keka API response for client creation.
/// data contains the newly created Keka client ID.
/// </summary>
internal sealed class KekaCreateClientResponse
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

/// <summary>
/// Wraps the Keka API response for client update.
/// data is true if the update was applied successfully.
/// </summary>
internal sealed class KekaUpdateClientResponse
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];

    [JsonPropertyName("data")]
    public bool Data { get; init; }
}

internal sealed class KekaDataListResponse<T>
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("data")]
    public List<T>? Data { get; init; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Wraps the Keka API response for project creation.
/// data contains the newly created Keka project ID.
/// </summary>
internal sealed class KekaCreateProjectResponse
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

/// <summary>
/// Wraps the Keka API response for project update.
/// data is true if the update was applied successfully.
/// </summary>
internal sealed class KekaUpdateProjectResponse
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];

    [JsonPropertyName("data")]
    public bool Data { get; init; }
}

/// <summary>
/// Wraps the Keka API response for project task creation.
/// data contains the newly created Keka task ID.
/// </summary>
internal sealed class KekaCreateTaskResponse
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

/// <summary>
/// Wraps the Keka API response for project task update.
/// data is true if the update was applied successfully.
/// </summary>
internal sealed class KekaUpdateTaskResponse
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];

    [JsonPropertyName("data")]
    public bool Data { get; init; }
}

/// <summary>
/// Wraps the Keka API response for fetching employee by email.
/// </summary>
internal sealed class KekaGetEmployeeResponse
{
    [JsonPropertyName("data")]
    public KekaEmployee? Data { get; init; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string>? Errors { get; init; }
}

/// <summary>
/// Represents a Keka project allocation.
/// </summary>
public sealed class KekaProjectAllocation
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("employee")]
    public KekaProjectAllocationEmployee? Employee { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("allocationPercentage")]
    public int AllocationPercentage { get; init; }

    [JsonPropertyName("billingRole")]
    public KekaProjectAllocationBillingRole? BillingRole { get; init; }

    [JsonPropertyName("billingRate")]
    public KekaProjectAllocationBillingRate? BillingRate { get; init; }

    [JsonPropertyName("billingType")]
    public int BillingType { get; init; }

    [JsonPropertyName("isShadow")]
    public bool IsShadow { get; init; }
}

public sealed class KekaProjectAllocationEmployee
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public sealed class KekaProjectAllocationBillingRole
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class KekaProjectAllocationBillingRate
{
    [JsonPropertyName("unit")]
    public int Unit { get; init; }

    [JsonPropertyName("rate")]
    public double Rate { get; init; }
}

public sealed class KekaProjectAllocationListResponse
{
    [JsonPropertyName("data")]
    public List<KekaProjectAllocation>? Data { get; init; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }

    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; init; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string>? Errors { get; init; }
}

/// <summary>
/// Wraps the Keka API response for project allocation creation.
/// data contains the newly created allocation identifier.
/// </summary>
public sealed class KekaCreateProjectAllocationResponse
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

/// <summary>
/// Wraps the Keka API response for timesheet entry creation.
/// data is true when entry is accepted.
/// </summary>
public sealed class KekaAddTimesheetEntryResponse
{
    [JsonPropertyName("data")]
    public bool Data { get; init; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Represents a Keka rate card entry.
/// </summary>
public sealed class KekaRateCard
{
    [JsonPropertyName("billingRoleId")]
    public string BillingRoleId { get; init; } = string.Empty;

    [JsonPropertyName("roleName")]
    public string RoleName { get; init; } = string.Empty;

    [JsonPropertyName("rateCardId")]
    public string RateCardId { get; init; } = string.Empty;

    [JsonPropertyName("rateCategoryId")]
    public string RateCategoryId { get; init; } = string.Empty;

    [JsonPropertyName("rateUnit")]
    public int RateUnit { get; init; }

    [JsonPropertyName("billRate")]
    public decimal BillRate { get; init; }

    [JsonPropertyName("approxCostRate")]
    public decimal ApproxCostRate { get; init; }

    [JsonPropertyName("rateCardName")]
    public string RateCardName { get; init; } = string.Empty;
}