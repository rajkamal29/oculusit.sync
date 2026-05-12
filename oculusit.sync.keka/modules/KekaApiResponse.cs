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
    [JsonPropertyName("data")]
    public List<T>? Data { get; init; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }
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
