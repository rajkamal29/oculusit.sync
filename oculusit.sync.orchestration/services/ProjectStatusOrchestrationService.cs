using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;

namespace oculusit.sync.orchestration.services;

public sealed class ProjectStatusOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    ILogger<ProjectStatusOrchestrationService> logger) : IProjectStatusOrchestrationService
{
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
            var mappedValue = existingById.TryGetValue(id, out var prev) ? prev.MappedValue : string.Empty;

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
