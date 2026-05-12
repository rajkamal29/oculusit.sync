using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;

namespace oculusit.sync.orchestration.services;

public sealed class MetadataOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    ILogger<MetadataOrchestrationService> logger) : IMetadataOrchestrationService
{
    public async Task<IReadOnlyList<ProjectStatusEntry>> SyncProjectStatusesAsync(
        IReadOnlyList<ProjectStatusEntry> existing,
        CancellationToken cancellationToken = default)
    {
        var cwStatuses = await connectWiseProjectService.GetAllProjectStatusesAsync(cancellationToken);

        logger.LogInformation("Fetched {Count} project statuses from ConnectWise.", cwStatuses.Count);

        // Index existing entries by ID so we can preserve their MappedValue on update.
        var existingById = existing.ToDictionary(e => e.Id);

        // Incoming IDs — anything not in this set will be deleted (simply not included in output).
        var incomingIds = cwStatuses.Select(s => s.Id.ToString()).ToHashSet();

        var deleted = existingById.Keys.Count(k => !incomingIds.Contains(k));

        var merged = cwStatuses.Select(s =>
        {
            var id = s.Id.ToString();

            // Preserve MappedValue if the entry already exists; leave empty for new entries.
            var mappedValue = existingById.TryGetValue(id, out var prev) ? prev.MappedValue : string.Empty;

            return new ProjectStatusEntry
            {
                Id          = id,
                Value       = s.Name,
                MappedValue = mappedValue
            };
        }).ToList();

        var added   = merged.Count(e => !existingById.ContainsKey(e.Id));
        var updated = merged.Count(e => existingById.TryGetValue(e.Id, out var prev) && prev.Value != e.Value);

        logger.LogInformation(
            "Project status metadata sync: Added={Added}, Updated={Updated}, Deleted={Deleted}, Total={Total}.",
            added, updated, deleted, merged.Count);

        return merged;
    }
}
