using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.modules;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration.mappings;
using Polly.Timeout;
using System.Globalization;
using System.Net;

namespace oculusit.sync.orchestration.services;

public sealed class ProjectOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    IKekaProjectService kekaProjectService,
    IKekaEmployeeService kekaEmployeeService,
    ISyncStateService syncStateService,
    ILogger<ProjectOrchestrationService> logger) : IProjectOrchestrationService
{
    private readonly Dictionary<string, KekaEmployee?> employeeCache = new();

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
        IReadOnlyList<TimeEntryEmployeeDedupeState> allEmployeesState,
        KekaEmployee? defaultProjectManager,
        CancellationToken cancellationToken = default)
    {
        var projects = await connectWiseProjectService.GetAllProjectsAsync(cancellationToken);
        logger.LogInformation("Fetched {Count} projects from ConnectWise. Starting Keka sync.", projects.Count);

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(projectStatusSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings from metadata.", statusMapping.Count);

        // Fetch all existing Keka projects and index by Code (ConnectWise project ID).
        var allKekaProjects = await kekaProjectService.GetAllProjectsAsync(cancellationToken);
        var kekaProjectsByCode = allKekaProjects
            .Where(p => !string.IsNullOrEmpty(p.Code))
            .ToDictionary(p => p.Code!);

        logger.LogInformation("Fetched {Count} existing Keka projects. {Indexed} have a ConnectWise code.",
            allKekaProjects.Count, kekaProjectsByCode.Count);

        // Full sync: only process projects that already exist in Keka (update only, no create).
        var mappedProjects = projects
            .Where(p => kekaProjectsByCode.ContainsKey(p.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        logger.LogInformation(
            "Full sync: {Mapped} of {Total} CW projects have an existing Keka project and will be updated. {Skipped} new projects skipped.",
            mappedProjects.Count, projects.Count, projects.Count - mappedProjects.Count);

        var allKeys = ProjectTaskDefinitions.Select(t => t.Key).ToList();

        var created = 0;
        var updated = 0;
        var failed  = 0;

        var syncedEntries = new List<SyncedProjectEntry>();
        var failedEntries = new List<FailedProjectEntry>();
        var retryEntries  = new List<RetryProjectEntry>();

        foreach (var project in mappedProjects)
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
                    retryEntries.Add(new RetryProjectEntry
                    {
                        Id           = project.Id.ToString(),
                        Name         = project.Name ?? string.Empty,
                        ErrorMessage = $"No Keka client found for ConnectWise company ID {companyId}."
                    });
                    continue;
                }

                var projectManager = allEmployeesState.FirstOrDefault(e => e.EmployeeId == project.Manager?.Id.ToString());
                KekaEmployee? kekaEmployee = null;

                if (projectManager is not null)
                {
                    kekaEmployee = await GetKekaEmployeeAsync(projectManager.Email.Trim(), cancellationToken);
                }
                else
                {
                    logger.LogInformation(
                        "TimeEntries#{MemberId} not found in DB. " +
                        "Project manager member exists in ConnectWise but has no employee checkpoint record.",
                        project.Manager?.Id);
                    kekaEmployee = defaultProjectManager;
                }

                if (kekaEmployee is null)
                {
                    logger.LogWarning(
                        "Full Sync: Keka Employee {Member} not found or some error occurred while searching the employee to update project manager. The project will not have a project manager assigned for ConnectWise project {ProjectId} - {ProjectName}.",
                        project.Manager?.Name, project.Id, project.Name);
                    retryEntries.Add(new RetryProjectEntry
                    {
                        Id = project.Id.ToString(),
                        Name = project.Name ?? string.Empty,
                        ErrorMessage = $"Full Sync: Keka Employee {project.Manager?.Name} not found or some error occurred while searching the employee to update project manager. The project will not have a project manager assigned for ConnectWise project {project.Id} - {project.Name}."
                    });
                }

                if (!kekaProjectsByCode.TryGetValue(project.Id.ToString(), out var existing))
                {
                    // New project — create in Keka then provision all 6 standard tasks.

                    // BillingType sync state provides the default billing type mappings value (billingType → numeric mappedValue).
                    var billingTypeSyncState = await syncStateService.GetAsync(SyncTypes.BillingType, cancellationToken);
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, kekaEmployee, billingTypeSyncState?.BillingType, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation("Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);

                    // Project was just created — no tasks exist yet, skip the Keka existence check.
                    await SyncProjectTasksAsync(
                        kekaProjectId, project.Id.ToString(), project.Name ?? string.Empty,
                        request.StartDate, request.EndDate, allKeys,
                        checkKekaForExistingTasks: false, cancellationToken);

                    created++;
                    syncedEntries.Add(new SyncedProjectEntry
                    {
                        Id            = project.Id.ToString(),
                        KekaClientId  = kekaClientId,
                        KekaProjectId = kekaProjectId
                    });
                }
                else
                {
                    // Project already exists in Keka — update it.
                    var updateRequest = KekaProjectMapper.MapToKekaProjectUpdateRequest(project, kekaEmployee, statusMapping);
                    await kekaProjectService.UpdateProjectAsync(existing.Id, updateRequest, cancellationToken);
                    logger.LogInformation("Updated Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        existing.Id, project.Id, project.Name);
                    updated++;

                    // Use Keka API as source of truth — skip tasks that already exist, create missing ones.
                    await SyncProjectTasksAsync(
                        existing.Id, project.Id.ToString(), project.Name ?? string.Empty,
                        updateRequest.StartDate, updateRequest.EndDate, allKeys,
                        checkKekaForExistingTasks: true, cancellationToken);

                    syncedEntries.Add(new SyncedProjectEntry
                    {
                        Id            = project.Id.ToString(),
                        KekaClientId  = kekaClientId,
                        KekaProjectId = existing.Id
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
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError)
            {
                logger.LogWarning(ex,
                    "Internal Server Error syncing ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
                retryEntries.Add(new RetryProjectEntry
                {
                    Id = project.Id.ToString(),
                    Name = project.Name ?? string.Empty,
                    ErrorMessage = ex.Message
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
        IReadOnlyList<TimeEntryEmployeeDedupeState> allEmployeesState,
        IReadOnlyList<string> retryProjectIds,
        KekaEmployee? defaultProjectManager,
        CancellationToken cancellationToken = default)
    {
        var since = projectSyncState.LastUpdatedAt!.Value;

        var projects = await connectWiseProjectService.GetProjectsSinceAsync(since, cancellationToken);
        logger.LogInformation("Incremental fetch returned {Count} projects updated since {Since}.", projects.Count, since);

        var retryNumericIds = retryProjectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => int.TryParse(id.Trim(), out var parsed) ? parsed : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        IReadOnlyList<ConnectWiseProject> retryProjects = [];
        if (retryNumericIds.Count > 0)
        {
            retryProjects = await connectWiseProjectService.GetProjectsByIdsAsync(retryNumericIds, cancellationToken);
            logger.LogInformation("RetryProjects fetch returned {Count} projects.", retryProjects.Count);
        }

        var mergedProjects = projects
            .Concat(retryProjects)
            .GroupBy(p => p.Id)
            .Select(g => g.Last())
            .ToList();

        if (mergedProjects.Count == 0)
            return new ProjectSyncResult();

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(projectStatusSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings.", statusMapping.Count);

        // CW project ID → Keka project ID — used for the create-vs-update gate.
        var knownProjects = projectSyncState.Projects
            .ToDictionary(p => p.Id, p => p.KekaProjectId);

        var allKeys = ProjectTaskDefinitions.Select(t => t.Key).ToList();

        var created = 0;
        var updated = 0;
        var failed  = 0;

        // Only newly created entries are returned — updates carry forward the existing entry.
        var newEntries    = new List<SyncedProjectEntry>();
        var failedEntries = new List<FailedProjectEntry>();
        var retryEntries  = new List<RetryProjectEntry>();

        foreach (var project in mergedProjects)
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
                    retryEntries.Add(new RetryProjectEntry
                    {
                        Id           = project.Id.ToString(),
                        Name         = project.Name ?? string.Empty,
                        ErrorMessage = $"No Keka client found for ConnectWise company ID {companyId}."
                    });
                    continue;
                }

                var projectManager = allEmployeesState.FirstOrDefault(e => e.EmployeeId == project.Manager?.Id.ToString());
                KekaEmployee? kekaEmployee = null;

                if (projectManager is not null) 
                {
                    kekaEmployee = await GetKekaEmployeeAsync(projectManager.Email.Trim(), cancellationToken);
                }
                else
                {
                    logger.LogInformation(
                        "TimeEntries#{MemberId} not found in DB. " +
                        "Project manager member exists in ConnectWise but has no employee checkpoint record.",
                        project.Manager?.Id);
                    kekaEmployee = defaultProjectManager;
                }

                if (kekaEmployee is null)
                {
                    logger.LogWarning(
                        "Incremental Sync: Keka Employee {Member} not found or some error occurred while searching the employee to update project manager. The project will not have a project manager assigned for ConnectWise project {ProjectId} - {ProjectName}.",
                        project.Manager?.Name, project.Id, project.Name);
                    retryEntries.Add(new RetryProjectEntry
                    {
                        Id = project.Id.ToString(),
                        Name = project.Name ?? string.Empty,
                        ErrorMessage = $"Incremental Sync: Keka Employee {project.Manager?.Name} not found or some error occurred while searching the employee to update project manager. The project will not have a project manager assigned for ConnectWise project {project.Id} - {project.Name}."
                    });
                }

                var projectIdStr = project.Id.ToString();

                if (knownProjects.TryGetValue(projectIdStr, out var existingKekaProjectId)
                    && !string.IsNullOrEmpty(existingKekaProjectId))
                {
                    // Known project — update in Keka.
                    var updateRequest = KekaProjectMapper.MapToKekaProjectUpdateRequest(project, kekaEmployee, statusMapping);
                    await kekaProjectService.UpdateProjectAsync(existingKekaProjectId, updateRequest, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Updated Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        existingKekaProjectId, project.Id, project.Name);
                    updated++;
                    newEntries.Add(new SyncedProjectEntry
                    {
                        Id            = projectIdStr,
                        KekaClientId  = kekaClientId,
                        KekaProjectId = existingKekaProjectId
                    });
                }
                else
                {
                    // New project — create in Keka then provision all 6 standard tasks.

                    // BillingType sync state provides the default billing type mappings (billingType → numeric).
                    var billingTypeSyncState = await syncStateService.GetAsync(SyncTypes.BillingType, cancellationToken);
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, kekaEmployee, billingTypeSyncState?.BillingType, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);

                    // Project was just created — no tasks exist yet, skip the Keka existence check.
                    await SyncProjectTasksAsync(
                        kekaProjectId, projectIdStr, project.Name ?? string.Empty,
                        request.StartDate, request.EndDate, allKeys,
                        checkKekaForExistingTasks: false, cancellationToken);

                    created++;
                    newEntries.Add(new SyncedProjectEntry
                    {
                        Id            = projectIdStr,
                        KekaClientId  = kekaClientId,
                        KekaProjectId = kekaProjectId
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
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError)
            {
                logger.LogWarning(ex,
                    "Incremental: Internal Server Error syncing ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
                retryEntries.Add(new RetryProjectEntry
                {
                    Id = project.Id.ToString(),
                    Name = project.Name ?? string.Empty,
                    ErrorMessage = ex.Message
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
            LastRecordUpdatedAt = mergedProjects[^1].LastUpdated,
            Total     = mergedProjects.Count,
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
    private async Task SyncProjectTasksAsync(
        string kekaProjectId,
        string cwProjectId,
        string cwProjectName,
        DateTime startDate,
        DateTime? endDate,
        IEnumerable<string> keysToCreate,
        bool checkKekaForExistingTasks,
        CancellationToken cancellationToken)
    {
        var taskStartDate = startDate.Date;
        var taskEndDate = endDate?.Date ?? DateTime.MaxValue;

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
                        StartDate       = taskStartDate,
                        EndDate         = taskEndDate,
                        TaskBillingType = billingType
                    },
                    cancellationToken);

                logger.LogInformation(
                    "Created Keka task '{TaskName}' ({Key}, BillingType={BillingType}) for project {CwProjectId} - {CwProjectName}.",
                    name, key, billingType, cwProjectId, cwProjectName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to create Keka task '{TaskName}' ({Key}) for project {CwProjectId} - {CwProjectName}.",
                    name, key, cwProjectId, cwProjectName);
            }
        }
    }

    public async Task<KekaEmployee?> GetKekaEmployeeAsync(string email, CancellationToken cancellationToken)
    {
        if (employeeCache.TryGetValue(email, out var employee))
            return employee;

        employee = await kekaEmployeeService.SearchEmployeeByEmailAsync(email, cancellationToken);

        employeeCache[email] = employee;

        return employee;
    }
}
