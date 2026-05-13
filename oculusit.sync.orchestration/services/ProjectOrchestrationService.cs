using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration.mappings;

namespace oculusit.sync.orchestration.services;

public sealed class ProjectOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    IKekaProjectService kekaProjectService,
    ILogger<ProjectOrchestrationService> logger) : IProjectOrchestrationService
{
    /// <summary>
    /// The 6 standard tasks to create for every Keka project.
    /// Key = short code stored in DynamoDB; Value = display name sent to Keka.
    /// </summary>
    private static readonly IReadOnlyList<(string Key, string Name)> ProjectTaskDefinitions =
    [
        ("BCH",  "CW: Billable Charge Code"),
        ("NBCH", "CW: Non-Billable Charge Code"),
        ("BST",  "CW: Billable Service Ticket"),
        ("NBST", "CW: Non-Billable Service Ticket"),
        ("BPT",  "CW: Billable Project Ticket"),
        ("NBPT", "CW: Non-Billable Project Ticket"),
    ];
    public async Task<ProjectSyncResult> SyncProjectsAsync(
        SyncState companySyncState,
        SyncState? metadataSyncState,
        CancellationToken cancellationToken = default)
    {
        var projects = await connectWiseProjectService.GetAllProjectsAsync(cancellationToken);
        logger.LogInformation("Fetched {Count} projects from ConnectWise. Starting Keka sync.", projects.Count);

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(metadataSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings from metadata.", statusMapping.Count);

        // Fetch all existing Keka projects and index by Code (ConnectWise project ID).
        var allKekaProjects = await kekaProjectService.GetAllProjectsAsync(cancellationToken);
        var kekaProjectsByCode = allKekaProjects
            .Where(p => !string.IsNullOrEmpty(p.Code))
            .ToDictionary(p => p.Code!);

        logger.LogInformation("Fetched {Count} existing Keka projects. {Indexed} have a ConnectWise code.",
            allKekaProjects.Count, kekaProjectsByCode.Count);

        // Also build a lookup of existing persisted project entries by CW project ID (for update task-gap detection).
        // This is passed in via the full-sync flow's projectSyncState — not available here, so we use an empty set.
        // For full sync the kekaProjectsByCode lookup already handles create-vs-update logic.
        var allKeys = ProjectTaskDefinitions.Select(t => t.Key).ToList();

        var created = 0;
        var updated = 0;
        var failed  = 0;

        var syncedEntries = new List<SyncedProjectEntry>();
        var failedEntries = new List<FailedProjectEntry>();

        foreach (var project in projects)
        {
            try
            {
                var companyId = project.Company?.Id.ToString();

                if (!kekaClientIdByCompanyId.TryGetValue(companyId ?? string.Empty, out var kekaClientId))
                {
                    logger.LogWarning(
                        "No Keka client found for ConnectWise company ID {CompanyId} on project {ProjectId} - {ProjectName}. Skipping.",
                        companyId, project.Id, project.Name);
                    continue;
                }

                if (!kekaProjectsByCode.TryGetValue(project.Id.ToString(), out var existing))
                {
                    // New project — create in Keka then provision all 6 standard tasks.
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation("Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);

                    var (taskIds, failedTaskKeys) = await SyncProjectTasksAsync(
                        kekaProjectId, project.Id.ToString(), project.Name ?? string.Empty, allKeys, cancellationToken);

                    created++;
                    syncedEntries.Add(new SyncedProjectEntry
                    {
                        Id             = project.Id.ToString(),
                        KekaClientId   = kekaClientId,
                        KekaProjectId  = kekaProjectId,
                        KekaTaskIds    = taskIds,
                        FailedTaskKeys = failedTaskKeys
                    });
                }
                else
                {
                    // Existing project — update in Keka.
                    var updateRequest = KekaProjectMapper.MapToKekaProjectUpdateRequest(project, statusMapping);
                    await kekaProjectService.UpdateProjectAsync(existing.Id, updateRequest, cancellationToken);
                    logger.LogInformation("Updated Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        existing.Id, project.Id, project.Name);
                    updated++;
                    syncedEntries.Add(new SyncedProjectEntry
                    {
                        Id            = project.Id.ToString(),
                        KekaClientId  = kekaClientId,
                        KekaProjectId = existing.Id
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
                failedEntries.Add(new FailedProjectEntry
                {
                    Id           = project.Id.ToString(),
                    Name         = project.Name ?? string.Empty,
                    ErrorMessage = ex.Message
                });
            }
        }

        logger.LogInformation(
            "Full project sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed}.",
            created, updated, failed);

        return new ProjectSyncResult { SyncedEntries = syncedEntries, FailedEntries = failedEntries };
    }

    public async Task<ProjectSyncResult> SyncProjectsIncrementalAsync(
        SyncState projectSyncState,
        SyncState companySyncState,
        SyncState? metadataSyncState,
        CancellationToken cancellationToken = default)
    {
        var since = projectSyncState.LastUpdatedAt!.Value;

        var projects = await connectWiseProjectService.GetProjectsSinceAsync(since, cancellationToken);
        logger.LogInformation("Incremental fetch returned {Count} projects updated since {Since}.", projects.Count, since);

        if (projects.Count == 0)
            return new ProjectSyncResult();

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(metadataSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings from metadata.", statusMapping.Count);

        // Full lookup of persisted entries keyed by CW project ID — used for both create-vs-update
        // detection and task-gap detection on the update path.
        var knownProjectEntries = projectSyncState.Projects
            .ToDictionary(p => p.Id);

        // Flat project-ID → KekaProjectId lookup for the create-vs-update gate.
        var knownProjects = knownProjectEntries
            .ToDictionary(kv => kv.Key, kv => kv.Value.KekaProjectId);

        var allKeys = ProjectTaskDefinitions.Select(t => t.Key).ToList();

        var created = 0;
        var updated = 0;
        var failed  = 0;

        // Only newly created entries are returned — updates carry forward the existing entry.
        var newEntries    = new List<SyncedProjectEntry>();
        var failedEntries = new List<FailedProjectEntry>();

        foreach (var project in projects)
        {
            try
            {
                var companyId = project.Company?.Id.ToString();

                if (!kekaClientIdByCompanyId.TryGetValue(companyId ?? string.Empty, out var kekaClientId))
                {
                    logger.LogWarning(
                        "No Keka client found for ConnectWise company ID {CompanyId} on project {ProjectId} - {ProjectName}. Skipping.",
                        companyId, project.Id, project.Name);
                    continue;
                }

                var projectIdStr = project.Id.ToString();

                if (knownProjects.TryGetValue(projectIdStr, out var existingKekaProjectId)
                    && !string.IsNullOrEmpty(existingKekaProjectId))
                {
                    // Known project — update in Keka.
                    var updateRequest = KekaProjectMapper.MapToKekaProjectUpdateRequest(project, statusMapping);
                    await kekaProjectService.UpdateProjectAsync(existingKekaProjectId, updateRequest, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Updated Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        existingKekaProjectId, project.Id, project.Name);
                    updated++;

                    // Check if any tasks are missing (never created or previously failed) and retry them.
                    knownProjectEntries.TryGetValue(projectIdStr, out var persistedEntry);
                    var existingTaskIds    = persistedEntry?.KekaTaskIds   ?? [];
                    var previouslyFailed   = persistedEntry?.FailedTaskKeys ?? [];

                    // Keys to retry = all standard keys that have no saved task ID yet.
                    var missingKeys = allKeys
                        .Where(k => !existingTaskIds.ContainsKey(k))
                        .ToList();

                    if (missingKeys.Count > 0)
                    {
                        logger.LogInformation(
                            "Incremental: Retrying {Count} missing task(s) [{Keys}] for project {ProjectId} - {ProjectName}.",
                            missingKeys.Count, string.Join(", ", missingKeys), project.Id, project.Name);

                        var (retriedTaskIds, retriedFailedKeys) = await SyncProjectTasksAsync(
                            existingKekaProjectId, projectIdStr, project.Name ?? string.Empty, missingKeys, cancellationToken);

                        // Merge newly created task IDs with existing ones.
                        var mergedTaskIds = new Dictionary<string, string>(existingTaskIds);
                        foreach (var kv in retriedTaskIds)
                            mergedTaskIds[kv.Key] = kv.Value;

                        // Persist an updated entry back via newEntries so the Worker appends/overwrites it.
                        newEntries.Add(new SyncedProjectEntry
                        {
                            Id             = projectIdStr,
                            KekaClientId   = persistedEntry?.KekaClientId,
                            KekaProjectId  = existingKekaProjectId,
                            KekaTaskIds    = mergedTaskIds,
                            FailedTaskKeys = retriedFailedKeys   // reset to only the latest failures
                        });
                    }
                }
                else
                {
                    // New project — create in Keka then provision all 6 standard tasks.
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);

                    var (taskIds, failedTaskKeys) = await SyncProjectTasksAsync(
                        kekaProjectId, projectIdStr, project.Name ?? string.Empty, allKeys, cancellationToken);

                    created++;
                    newEntries.Add(new SyncedProjectEntry
                    {
                        Id             = projectIdStr,
                        KekaClientId   = kekaClientId,
                        KekaProjectId  = kekaProjectId,
                        KekaTaskIds    = taskIds,
                        FailedTaskKeys = failedTaskKeys
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Incremental: Failed to sync ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
                failedEntries.Add(new FailedProjectEntry
                {
                    Id           = project.Id.ToString(),
                    Name         = project.Name ?? string.Empty,
                    ErrorMessage = ex.Message
                });
            }
        }

        logger.LogInformation(
            "Incremental project sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed}.",
            created, updated, failed);

        return new ProjectSyncResult { SyncedEntries = newEntries, FailedEntries = failedEntries };
    }

    /// <summary>
    /// Attempts to create only the tasks identified by <paramref name="keysToCreate"/> under the given
    /// Keka project. Returns a tuple of:
    /// <list type="bullet">
    ///   <item><c>taskIds</c> — short-code → Keka task ID for every task that succeeded.</item>
    ///   <item><c>failedKeys</c> — short-codes for every task that failed.</item>
    /// </list>
    /// Each task is attempted independently; a failure on one does not abort the others.
    /// </summary>
    private async Task<(Dictionary<string, string> taskIds, List<string> failedKeys)> SyncProjectTasksAsync(
        string kekaProjectId,
        string cwProjectId,
        string cwProjectName,
        IEnumerable<string> keysToCreate,
        CancellationToken cancellationToken)
    {
        var taskIds    = new Dictionary<string, string>();
        var failedKeys = new List<string>();

        // Build a lookup so we can resolve the display name for each key.
        var definitionsByKey = ProjectTaskDefinitions.ToDictionary(t => t.Key, t => t.Name);

        foreach (var key in keysToCreate)
        {
            if (!definitionsByKey.TryGetValue(key, out var name))
            {
                logger.LogWarning("Unknown task key '{Key}' for project {CwProjectId} — skipping.", key, cwProjectId);
                continue;
            }

            try
            {
                var taskId = await kekaProjectService.CreateTaskAsync(
                    new KekaTaskRequest { ProjectId = kekaProjectId, Name = name },
                    cancellationToken);

                taskIds[key] = taskId;
                logger.LogInformation(
                    "Created Keka task '{TaskName}' ({Key}) → {TaskId} for project {CwProjectId} - {CwProjectName}.",
                    name, key, taskId, cwProjectId, cwProjectName);
            }
            catch (Exception ex)
            {
                failedKeys.Add(key);
                logger.LogError(ex,
                    "Failed to create Keka task '{TaskName}' ({Key}) for project {CwProjectId} - {CwProjectName}.",
                    name, key, cwProjectId, cwProjectName);
            }
        }

        return (taskIds, failedKeys);
    }
}
