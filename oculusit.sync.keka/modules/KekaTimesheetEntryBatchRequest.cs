using System.Text.Json.Serialization;

namespace oculusit.sync.keka.modules;

public sealed class KekaTimesheetEntryBatchRequest : List<KekaTimesheetEntryRequest> 
{
}

public sealed class KekaTimesheetEntryRequest
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = string.Empty;

    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = string.Empty;

    [JsonPropertyName("numberOfMinutes")]
    public int NumberOfMinutes { get; init; }

    [JsonPropertyName("date")]
    public DateTime Date { get; init; }

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;

    [JsonPropertyName("startTime")]
    public int StartTime { get; init; }

    [JsonPropertyName("endTime")]
    public int EndTime { get; init; }
}
