using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;

namespace oculusit.sync.orchestration.services;

public sealed class ProjectStatusOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    ILogger<ProjectStatusOrchestrationService> logger) : IProjectStatusOrchestrationService
{
    // ConnectWise status name → Keka integer value
    // Completed=1, InProgress=0, Cancelled=2, NotStarted=3, OnHold=4
    private static readonly Dictionary<string, string> CwStatusMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Completed"]                        = "1",
            ["Closed"]                           = "1",
            ["Closed - Not Implemented"]         = "1",
            ["4. Closed"]                        = "1",

            ["In Progress"]                      = "0",
            ["2. In Progress"]                   = "0",
            ["Discovery/Scoping"]                = "0",
            ["Implementation"]                   = "0",
            ["Implementation - Phase I"]         = "0",
            ["Implementation - Phase IV"]        = "0",
            ["Implementation - Phase VI"]        = "0",
            ["Waiting on client Signoff"]        = "0",
            ["Waiting Client Reply"]             = "0",
            ["Soft Launched"]                    = "0",
            ["UAT"]                              = "0",
            ["Internal QA"]                      = "0",
            ["Not initiated by Sales or CS"]     = "0",

            ["Terminated"]                       = "2",

            ["Not Started"]                      = "0",
            ["1. New"]                           = "0",

            ["On-Hold"]                          = "4",
        };

    private static string ResolveDefaultMappedValue(string? cwStatusName) =>
        !string.IsNullOrWhiteSpace(cwStatusName) &&
        CwStatusMappings.TryGetValue(cwStatusName, out var mapped)
            ? mapped
            : string.Empty;

    public async Task<ProjectStatusSyncResult> SyncProjectStatusesAsync(
        IReadOnlyList<ProjectStatusEntry> existing,
        CancellationToken cancellationToken = default)
    {
        var cwStatuses = await connectWiseProjectService.GetAllProjectStatusesAsync(cancellationToken);

        logger.LogInformation("Fetched {Count} project statuses from ConnectWise.", cwStatuses.Count);

        // Index existing entries by ID so we can preserve MappedValue and detect name changes.
        var existingById = existing.ToDictionary(e => e.Id);

        // IDs coming from ConnectWise — anything not in this set is considered deleted.
        var incomingIds = cwStatuses.Select(s => s.Id.ToString()).ToHashSet();

        var deleted = existingById.Keys.Count(k => !incomingIds.Contains(k));

        var merged = cwStatuses.Select(s =>
        {
            var id = s.Id.ToString();
            // Preserve any manually set mapped value; auto-resolve for new or blank entries.
            var mappedValue = existingById.TryGetValue(id, out var prev) && !string.IsNullOrEmpty(prev.MappedValue)
                ? prev.MappedValue
                : ResolveDefaultMappedValue(s.Name);

            return new ProjectStatusEntry
            {
                Id          = id,
                Value       = s.Name,
                MappedValue = mappedValue
            };
        }).ToList();

        var added   = merged.Count(e => !existingById.ContainsKey(e.Id));
        var updated = merged.Count(e =>
            existingById.TryGetValue(e.Id, out var prev) && prev.Value != e.Value);

        var hasChanges = added > 0 || updated > 0 || deleted > 0;

        logger.LogInformation(
            "Project status sync comparison: Added={Added}, Updated={Updated}, Deleted={Deleted}, Total={Total}. HasChanges={HasChanges}.",
            added, updated, deleted, merged.Count, hasChanges);

        return new ProjectStatusSyncResult
        {
            Entries    = merged,
            HasChanges = hasChanges,
            Added      = added,
            Updated    = updated,
            Deleted    = deleted
        };
    }
}
