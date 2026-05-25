using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration.mappings;
using Polly.Timeout;

namespace oculusit.sync.orchestration.services;

public sealed class ProjectOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    IKekaProjectService kekaProjectService,
    ILogger<ProjectOrchestrationService> logger) : IProjectOrchestrationService
{
    public async Task<IReadOnlyList<InitialProjectEntry>> BuildInitialProjectSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var cwProjects = await connectWiseProjectService.GetAllProjectsAsync(cancellationToken);
        var kekaProjects = await kekaProjectService.GetAllProjectsAsync(cancellationToken);

        logger.LogInformation(
            "Building initial project snapshot from {ConnectWiseCount} ConnectWise projects and {KekaCount} Keka projects.",
            cwProjects.Count, kekaProjects.Count);

        var snapshots = new List<InitialProjectEntry>();

        var cwById = cwProjects
            .GroupBy(p => p.Id.ToString())
            .ToDictionary(g => g.Key, g => g.First());

        var kekaByCode = new Dictionary<string, KekaProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var kp in kekaProjects)
        {
            var code = kp.Code?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                continue;

            if (!kekaByCode.TryAdd(code, kp))
            {
                logger.LogWarning(
                    "Duplicate Keka project code {Code} found. Keeping first and ignoring project {ProjectId}.",
                    code, kp.Id);
            }
        }

        foreach (var cp in cwProjects)
        {
            var cwId = cp.Id.ToString();
            if (kekaByCode.TryGetValue(cwId, out var matchedKeka))
            {
                snapshots.Add(new InitialProjectEntry
                {
                    ProjectId       = cwId,
                    ProjectName     = cp.Name ?? string.Empty,
                    KekaProjectId   = matchedKeka.Id,
                    KekaProjectCode = matchedKeka.Code ?? string.Empty,
                    KekaProjectName = matchedKeka.Name ?? string.Empty
                });
            }
            else
            {
                snapshots.Add(new InitialProjectEntry
                {
                    ProjectId       = cwId,
                    ProjectName     = cp.Name ?? string.Empty,
                    KekaProjectId   = string.Empty,
                    KekaProjectCode = string.Empty,
                    KekaProjectName = string.Empty
                });
            }
        }

        foreach (var kp in kekaProjects)
        {
            var code = kp.Code?.Trim();
            if (!string.IsNullOrWhiteSpace(code) && cwById.ContainsKey(code))
                continue;

            snapshots.Add(new InitialProjectEntry
            {
                ProjectId       = string.Empty,
                ProjectName     = string.Empty,
                KekaProjectId   = kp.Id,
                KekaProjectCode = kp.Code ?? string.Empty,
                KekaProjectName = kp.Name ?? string.Empty
            });
        }

        logger.LogInformation("Initial project snapshot built with {Count} rows.", snapshots.Count);
        return snapshots;
    }
    /// <summary>
    /// The 6 standard tasks to create for every Keka project.
    /// Key = short code stored in DynamoDB; Name = display name sent to Keka;
    /// BillingType: 1 = Billable, 0 = Non-Billable.
    /// </summary>
    private static readonly IReadOnlyList<(string Key, string Name, int BillingType)> ProjectTaskDefinitions =
    [
        ("BCH",  "CW: Billable Charge Code",         1),
        ("NBCH", "CW: Non-Billable Charge Code",      0),
        ("BST",  "CW: Billable Service Ticket",       1),
        ("NBST", "CW: Non-Billable Service Ticket",   0),
        ("BPT",  "CW: Billable Project Ticket",       1),
        ("NBPT", "CW: Non-Billable Project Ticket",   0),
    ];
    public async Task<ProjectSyncResult> SyncProjectsAsync(
        SyncState companySyncState,
        SyncState? projectStatusSyncState,
        CancellationToken cancellationToken = default)
    {
        var projects = await connectWiseProjectService.GetAllProjectsAsync(cancellationToken);
        logger.LogInformation("Fetched {Count} projects from ConnectWise. Starting Keka sync.", projects.Count);

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(projectStatusSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings.", statusMapping.Count);

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
        var retryEntries  = new List<RetryProjectEntry>();

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
                    failed++;
                    failedEntries.Add(new FailedProjectEntry
                    {
                        Id           = project.Id.ToString(),
                        Name         = project.Name ?? string.Empty,
                        ErrorMessage = $"No Keka client found for ConnectWise company ID {companyId}."
                    });
                    continue;
                }

                if (!kekaProjectsByCode.TryGetValue(project.Id.ToString(), out var existing))
                {
                    // New project — create in Keka then provision all 6 standard tasks.
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation("Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);

                    // Project was just created — no tasks exist yet, skip the Keka existence check.
                    var failedTaskKeys = await SyncProjectTasksAsync(
                        kekaProjectId, project.Id.ToString(), project.Name ?? string.Empty,
                        request.StartDate, request.EndDate, allKeys,
                        checkKekaForExistingTasks: false, cancellationToken);

                    created++;
                    syncedEntries.Add(new SyncedProjectEntry
                    {
                        Id             = project.Id.ToString(),
                        KekaClientId   = kekaClientId,
                        KekaProjectId  = kekaProjectId,
                        FailedTaskKeys = failedTaskKeys
                    });
                }
                else
                {
                    // Project already exists in Keka — update it.
                    var updateRequest = KekaProjectMapper.MapToKekaProjectUpdateRequest(project, statusMapping);
                    await kekaProjectService.UpdateProjectAsync(existing.Id, updateRequest, cancellationToken);
                    logger.LogInformation("Updated Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        existing.Id, project.Id, project.Name);
                    updated++;

                    // Full sync has no DynamoDB state — use Keka API as source of truth to
                    // skip tasks that already exist and only create the ones that are missing.
                    logger.LogInformation(
                        "Full sync: No DynamoDB task state for project {ProjectId} - {ProjectName}. Checking Keka for existing tasks.",
                        project.Id, project.Name);

                    var failedTaskKeys = await SyncProjectTasksAsync(
                        existing.Id, project.Id.ToString(), project.Name ?? string.Empty,
                        updateRequest.StartDate, updateRequest.EndDate, allKeys,
                        checkKekaForExistingTasks: true, cancellationToken);

                    syncedEntries.Add(new SyncedProjectEntry
                    {
                        Id             = project.Id.ToString(),
                        KekaClientId   = kekaClientId,
                        KekaProjectId  = existing.Id,
                        FailedTaskKeys = failedTaskKeys
                    });
                }
            }
            catch (TimeoutRejectedException tex)
            {
                logger.LogWarning(tex,
                    "Timeout syncing ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
                retryEntries.Add(new RetryProjectEntry
                {
                    Id           = project.Id.ToString(),
                    Name         = project.Name ?? string.Empty,
                    ErrorMessage = tex.Message
                });
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

        return new ProjectSyncResult
        {
            SyncedEntries       = syncedEntries,
            FailedEntries       = failedEntries,
            RetryEntries        = retryEntries,
            LastRecordUpdatedAt = projects[^1].LastUpdated,
            Total     = projects.Count,
            Succeeded = created + updated,
            Failed    = failed
        };
    }

    public async Task<ProjectSyncResult> SyncProjectsIncrementalAsync(
        SyncState projectSyncState,
        SyncState companySyncState,
        SyncState? projectStatusSyncState,
        CancellationToken cancellationToken = default)
    {
        var since = projectSyncState.LastUpdatedAt!.Value;

        var projects = await connectWiseProjectService.GetProjectsSinceAsync(since, cancellationToken);
        logger.LogInformation("Incremental fetch returned {Count} projects updated since {Since}.", projects.Count, since);

        if (projects.Count == 0)
            return new ProjectSyncResult();

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(projectStatusSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings.", statusMapping.Count);

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
        var retryEntries  = new List<RetryProjectEntry>();

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
                    failed++;
                    failedEntries.Add(new FailedProjectEntry
                    {
                        Id           = project.Id.ToString(),
                        Name         = project.Name ?? string.Empty,
                        ErrorMessage = $"No Keka client found for ConnectWise company ID {companyId}."
                    });
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
                    var previouslyFailed = persistedEntry?.FailedTaskKeys ?? [];

                    // Keys to retry = all previously failed task keys.
                    var keysToRetry = previouslyFailed.Count > 0 ? previouslyFailed : [];

                    if (keysToRetry.Count > 0)
                    {
                        logger.LogInformation(
                            "Incremental: Retrying {Count} failed task(s) [{Keys}] for project {ProjectId} - {ProjectName}.",
                            keysToRetry.Count, string.Join(", ", keysToRetry), project.Id, project.Name);

                        // State is source of truth — keysToRetry are derived from persisted FailedTaskKeys.
                        var retriedFailedKeys = await SyncProjectTasksAsync(
                            existingKekaProjectId, projectIdStr, project.Name ?? string.Empty,
                            updateRequest.StartDate, updateRequest.EndDate, keysToRetry,
                            checkKekaForExistingTasks: false, cancellationToken);

                        newEntries.Add(new SyncedProjectEntry
                        {
                            Id             = projectIdStr,
                            KekaClientId   = persistedEntry?.KekaClientId,
                            KekaProjectId  = existingKekaProjectId,
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

                    // Project was just created — no tasks exist yet, skip the Keka existence check.
                    var failedTaskKeys = await SyncProjectTasksAsync(
                        kekaProjectId, projectIdStr, project.Name ?? string.Empty,
                        request.StartDate, request.EndDate, allKeys,
                        checkKekaForExistingTasks: false, cancellationToken);

                    created++;
                    newEntries.Add(new SyncedProjectEntry
                    {
                        Id             = projectIdStr,
                        KekaClientId   = kekaClientId,
                        KekaProjectId  = kekaProjectId,
                        FailedTaskKeys = failedTaskKeys
                    });
                }
            }
            catch (TimeoutRejectedException tex)
            {
                logger.LogWarning(tex,
                    "Incremental: Timeout syncing ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
                retryEntries.Add(new RetryProjectEntry
                {
                    Id           = project.Id.ToString(),
                    Name         = project.Name ?? string.Empty,
                    ErrorMessage = tex.Message
                });
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

        return new ProjectSyncResult
        {
            SyncedEntries       = newEntries,
            FailedEntries       = failedEntries,
            RetryEntries        = retryEntries,
            LastRecordUpdatedAt = projects[^1].LastUpdated,
            Total     = projects.Count,
            Succeeded = created + updated,
            Failed    = failed
        };
    }

    /// <summary>
    /// Attempts to create only the tasks identified by <paramref name="keysToCreate"/> under the given
    /// Keka project. Before creating, fetches existing tasks from Keka and skips any whose display name
    /// already exists (case-insensitive). Returns a tuple of:
    /// <list type="bullet">
    ///   <item><c>taskIds</c> — short-code → Keka task ID for every task that succeeded or already existed.</item>
    ///   <item><c>failedKeys</c> — short-codes for every task that failed.</item>
    /// </list>
    /// Each task is attempted independently; a failure on one does not abort the others.
    /// Task IDs are not stored — Keka API is the source of truth for existing tasks.
    /// </summary>
    private async Task<List<string>> SyncProjectTasksAsync(
        string kekaProjectId,
        string cwProjectId,
        string cwProjectName,
        DateTime startDate,
        DateTime endDate,
        IEnumerable<string> keysToCreate,
        bool checkKekaForExistingTasks,
        CancellationToken cancellationToken)
    {
        var failedKeys = new List<string>();

        var definitionsByKey = ProjectTaskDefinitions.ToDictionary(t => t.Key, t => (t.Name, t.BillingType));

        // Only call the Keka API when there is no DynamoDB state to rely on.
        // When state exists the caller has already filtered keysToCreate to only the failed/missing ones.
        HashSet<string> existingNames = [];
        if (checkKekaForExistingTasks)
        {
            IReadOnlyList<KekaTask> existingTasks;
            try
            {
                existingTasks = await kekaProjectService.GetTasksByProjectAsync(kekaProjectId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not fetch existing tasks for Keka project {KekaProjectId} ({CwProjectId}). Proceeding without skip-check.",
                    kekaProjectId, cwProjectId);
                existingTasks = [];
            }

            existingNames = existingTasks
                .Select(t => t.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var key in keysToCreate)
        {
            if (!definitionsByKey.TryGetValue(key, out var def))
            {
                logger.LogWarning("Unknown task key '{Key}' for project {CwProjectId} — skipping.", key, cwProjectId);
                continue;
            }

            var (name, billingType) = def;

            // Skip creation if a task with the same name already exists in Keka.
            if (existingNames.Contains(name))
            {
                logger.LogInformation(
                    "Task '{TaskName}' ({Key}) already exists in Keka for project {CwProjectId} - {CwProjectName}. Skipping creation.",
                    name, key, cwProjectId, cwProjectName);
                continue;
            }

            try
            {
                await kekaProjectService.CreateTaskAsync(
                    kekaProjectId,
                    new KekaTaskRequest
                    {
                        ProjectId       = kekaProjectId,
                        Name            = name,
                        StartDate       = startDate,
                        EndDate         = endDate,
                        TaskBillingType = billingType
                    },
                    cancellationToken);

                logger.LogInformation(
                    "Created Keka task '{TaskName}' ({Key}, BillingType={BillingType}) for project {CwProjectId} - {CwProjectName}.",
                    name, key, billingType, cwProjectId, cwProjectName);
            }
            catch (Exception ex)
            {
                failedKeys.Add(key);
                logger.LogError(ex,
                    "Failed to create Keka task '{TaskName}' ({Key}) for project {CwProjectId} - {CwProjectName}.",
                    name, key, cwProjectId, cwProjectName);
            }
        }

        return failedKeys;
    }
}
