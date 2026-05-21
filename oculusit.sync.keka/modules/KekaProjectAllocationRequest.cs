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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string EmployeeId { get; init; }

    [JsonPropertyName("allocationPercentage")]
    public int AllocationPercentage { get; init; }

    [JsonPropertyName("billingRoleId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string BillingRoleId { get; init; }

    [JsonPropertyName("billingRate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BillingRate { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("billingType")]
    public KekaProjectAllocationBillingType BillingType { get; init; }
}
