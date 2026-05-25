using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public enum KekaProjectAllocationBillingType
{
    Billable = 1,
    NonBillable = 0
}

public sealed class KekaProjectAllocationRequest
{
    [JsonPropertyName("employeeId")]
    public required string EmployeeId { get; init; }

    [JsonPropertyName("allocationPercentage")]
    public required int AllocationPercentage { get; init; }

    [JsonPropertyName("billingRoleId")]
    public required string BillingRoleId { get; init; }

    [JsonPropertyName("billingRate")]
    public double? BillingRate { get; init; }

    [JsonPropertyName("startDate")]
    public required DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("billingType")]
    public KekaProjectAllocationBillingType BillingType { get; init; }
}
