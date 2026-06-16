using oculusit.sync.connectwise.modules;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;

namespace oculusit.sync.orchestration.mappings;

public static class KekaProjectMapper
{
    // Maximum length of Keka's Group.Description column.
    private const int MaxDescriptionLength = 100;

    // Default Keka status when no project status mapping is found: 0 = InProgress.
    private const int DefaultStatus = 0;

    public static KekaProjectRequest MapToKekaProjectRequest(
        ConnectWiseProject project,
        string kekaClientId,
        KekaEmployee? kekaEmployee,
        IReadOnlyDictionary<string, int> statusMapping)
    {
        var (startDate, endDate) = ValidateDates(project);

        return new KekaProjectRequest
        {
            ClientId    = kekaClientId,
            Name        = project.Name,
            Description = TruncateDescription(project.Description),
            Code        = project.Id.ToString(),
            Status      = MapStatus(project.Status?.Name, statusMapping),
            StartDate   = startDate,
            EndDate     = endDate,
            IsBillable  = true,
            BillingType = BillingType.FixedBid,
            ProjectManager = new List<string> { kekaEmployee?.Id ?? string.Empty }
        };
    }

    public static KekaProjectUpdateRequest MapToKekaProjectUpdateRequest(
        ConnectWiseProject project,
        KekaEmployee? kekaEmployee,
        IReadOnlyDictionary<string, int> statusMapping)
    {
        var (startDate, endDate) = ValidateDates(project);

        return new KekaProjectUpdateRequest
        {
            Name        = project.Name,
            Description = TruncateDescription(project.Description),
            Code        = project.Id.ToString(),
            Status      = MapStatus(project.Status?.Name, statusMapping),
            StartDate   = startDate,
            EndDate     = endDate,
            IsBillable  = true,
            ProjectManager = new List<string> { kekaEmployee?.Id ?? string.Empty }
        };
    }

    /// <summary>
    /// Resolves start and end dates for the project using the following priority:
    /// <list type="number">
    ///   <item>ActualStart / ActualEnd when both are present.</item>
    ///   <item>EstimatedStart / EstimatedEnd when either actual date is missing.</item>
    /// </list>
    /// Throws <see cref="InvalidOperationException"/> when the resolved estimated dates are also null.
    /// </summary>
    /// <returns>A tuple of (startDate, endDate) when both values are valid.</returns>
    private static (DateTime startDate, DateTime endDate) ValidateDates(ConnectWiseProject project)
    {
        DateTime? startDate;
        DateTime? endDate;

        if (project.ActualStart.HasValue && project.ActualEnd.HasValue)
        {
            startDate = project.ActualStart;
            endDate   = project.ActualEnd;
        }
        else
        {
            startDate = project.EstimatedStart;
            endDate   = project.EstimatedEnd;
        }

        var invalidFields = new List<string>();

        if (startDate is null)
            invalidFields.Add("EstimatedStart");

        if (invalidFields.Count > 0)
            throw new InvalidOperationException(
                $"Project {project.Id} - '{project.Name}' has an invalid or missing date field(s): {string.Join(", ", invalidFields)}.");

        return (startDate!.Value, endDate!.Value);
    }

    /// <summary>
    /// Builds a case-insensitive status lookup dictionary from the persisted project status entries.
    /// Key = ConnectWise status name (Value field), mapped value = Keka numeric status (MappedValue field).
    /// Entries with a missing or non-numeric MappedValue are skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, int> BuildStatusMapping(
        IReadOnlyList<ProjectStatusEntry> projectStatuses)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in projectStatuses)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value)
                && int.TryParse(entry.MappedValue, out var numeric))
            {
                dict.TryAdd(entry.Value.Trim(), numeric);
            }
        }

        return dict;
    }

    /// <summary>
    /// Resolves the Keka numeric status by looking up the ConnectWise status name in the
    /// project status-derived dictionary. Falls back to <see cref="DefaultStatus"/> (InProgress = 0)
    /// when the status is absent or unmapped.
    /// </summary>
    private static int MapStatus(string? cwStatus, IReadOnlyDictionary<string, int> statusMapping)
    {
        if (string.IsNullOrWhiteSpace(cwStatus))
            return DefaultStatus;

        return statusMapping.TryGetValue(cwStatus.Trim(), out var mapped)
            ? mapped
            : DefaultStatus;
    }

    /// <summary>
    /// Trims whitespace and truncates the description to <see cref="MaxDescriptionLength"/>
    /// characters to stay within Keka's column limit. Returns null for blank input.
    /// </summary>
    private static string? TruncateDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        return value.Length <= MaxDescriptionLength
            ? value
            : value[..MaxDescriptionLength];
    }
}
