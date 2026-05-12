using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration.mappings;

namespace oculusit.sync.orchestration.services;

public sealed class ProjectOrchestrationService(
    IConnectWiseProjectService connectWiseProjectService,
    IKekaProjectService kekaProjectService,
    ILogger<ProjectOrchestrationService> logger) : IProjectOrchestrationService
{
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
                    // New project — create in Keka.
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation("Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);
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

        // Build lookups from persisted sync state — avoids a full Keka fetch on every incremental run.
        var knownProjects = projectSyncState.Projects
            .ToDictionary(p => p.Id, p => p.KekaProjectId);

        var kekaClientIdByCompanyId = companySyncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var statusMapping = KekaProjectMapper.BuildStatusMapping(metadataSyncState?.ProjectStatuses ?? []);
        logger.LogInformation("Loaded {Count} project status mappings from metadata.", statusMapping.Count);

        var created = 0;
        var updated = 0;
        var failed  = 0;

        // Only newly created entries are returned — updates don't change the mapping.
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
                }
                else
                {
                    // New project — create in Keka and record the mapping.
                    var request = KekaProjectMapper.MapToKekaProjectRequest(project, kekaClientId, statusMapping);
                    var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Created Keka project {KekaProjectId} for ConnectWise project {ProjectId} - {ProjectName}.",
                        kekaProjectId, project.Id, project.Name);
                    created++;
                    newEntries.Add(new SyncedProjectEntry
                    {
                        Id            = projectIdStr,
                        KekaClientId  = kekaClientId,
                        KekaProjectId = kekaProjectId
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
}
