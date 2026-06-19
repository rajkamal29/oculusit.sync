using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.modules;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration.mappings;
using Polly.Timeout;

namespace oculusit.sync.orchestration.services;

public sealed class CompanyOrchestrationService(
    IConnectWiseCompanyService connectWiseService,
    IKekaClientService kekaClientService,
    IKekaCurrencyService kekaCurrencyService,
    IKekaProjectService kekaProjectService,
    IKekaEmployeeService kekaEmployeeService,
    ISyncStateService syncStateService,
    ILogger<CompanyOrchestrationService> logger) : ICompanyOrchestrationService
{
    private string? defaultProjectManagerEmployeeIdCache = string.Empty;

    public async Task<CompanySyncResult> SyncCompaniesToKekaAsync(DefaultProjectEntry? defaultProject, CancellationToken cancellationToken = default)
    {
        var companies = await connectWiseService.GetAllCompaniesAsync(cancellationToken);
        logger.LogInformation("Fetched {Count} companies from ConnectWise. Starting Keka sync.", companies.Count);

        var kekaClientIdByCompanyId = await BuildKekaClientLookupAsync(cancellationToken);

        // Persist existing CW→Keka mappings before processing so that any
        // companies already mapped in Keka are recorded in DynamoDB upfront.
        // Only include entries whose CW company ID exists in the current ConnectWise companies list.
        var cwCompanyIds = companies
            .Select(c => c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingMappedEntries = kekaClientIdByCompanyId
            .Where(kv => cwCompanyIds.Contains(kv.Key))
            .Select(kv => new SyncedCompanyEntry { Id = kv.Key, ClientId = kv.Value })
            .ToList();

        if (existingMappedEntries.Count > 0)
        {
            await syncStateService.UpsertCompaniesAsync(SyncTypes.Company, existingMappedEntries, DateTime.UtcNow, cancellationToken);

            logger.LogInformation(
                "Full sync: pre-populated Company sync state with {Count} existing CW→Keka mappings.",
                existingMappedEntries.Count);
        }

        // Full sync: only process companies that already exist in Keka (update only, no create).
        var mappedCompanies = companies
            .Where(c => kekaClientIdByCompanyId.ContainsKey(c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        logger.LogInformation(
            "Full sync: {Mapped} of {Total} CW companies have an existing Keka client and will be updated. {Skipped} new companies skipped.",
            mappedCompanies.Count, companies.Count, companies.Count - mappedCompanies.Count);

        return await ProcessCompaniesAsync(
            mappedCompanies,
            kekaClientIdByCompanyId,
            defaultProject,
            syncLabel: "Full", 
            cancellationToken: cancellationToken);
    }

    public async Task<CompanySyncResult> SyncCompaniesIncrementalAsync(
        SyncState syncState,
        DefaultProjectEntry? defaultProject,
        IReadOnlyList<string> retryCompanyIds,
        CancellationToken cancellationToken = default)
    {
        var since = syncState.LastUpdatedAt!.Value;

        var companies = await connectWiseService.GetCompaniesSinceAsync(since, cancellationToken);
        logger.LogInformation("Incremental fetch returned {Count} companies updated since {Since}.", companies.Count, since);

        var retryNumericIds = retryCompanyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => int.TryParse(id.Trim(), out var parsed) ? parsed : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        IReadOnlyList<ConnectWiseCompany> retryCompanies = [];
        if (retryNumericIds.Count > 0)
        {
            retryCompanies = await connectWiseService.GetCompaniesByIdsAsync(retryNumericIds, cancellationToken);
            logger.LogInformation("RetryCompanies fetch returned {Count} companies.", retryCompanies.Count);
        }

        var mergedCompanies = companies
            .Concat(retryCompanies)
            .GroupBy(c => c.Id)
            .Select(g => g.Last())
            .ToList();

        if (mergedCompanies.Count == 0)
            return new CompanySyncResult();

        var kekaIdByCompanyId = syncState.Companies
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.ClientId))
            .ToDictionary(e => e.Id, e => e.ClientId, StringComparer.OrdinalIgnoreCase);

        return await ProcessCompaniesAsync(
            mergedCompanies,
            kekaIdByCompanyId,
            defaultProject,
            syncLabel: "Incremental",
            cancellationToken: cancellationToken);
    }

    private async Task<Dictionary<string, string>> BuildKekaClientLookupAsync(CancellationToken cancellationToken)
    {
        var allKekaClients = await kekaClientService.GetAllClientsAsync(cancellationToken);
        var kekaClientsByCode = allKekaClients
            .Where(c => !string.IsNullOrEmpty(c.Code))
            .ToDictionary(c => c.Code!);

        logger.LogInformation("Fetched {Count} existing Keka clients. {Indexed} have a ConnectWise code.",
            allKekaClients.Count, kekaClientsByCode.Count);

        return kekaClientsByCode.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<CompanySyncResult> ProcessCompaniesAsync(
        IReadOnlyList<ConnectWiseCompany> companies,
        IReadOnlyDictionary<string, string> kekaClientIdByCompanyId,
        DefaultProjectEntry? defaultProject,
        string syncLabel,
        CancellationToken cancellationToken)
    {
        if (companies.Count == 0)
            return new CompanySyncResult();

        var usdCurrencyId = await kekaCurrencyService.GetUsdCurrencyIdAsync(cancellationToken);
        if (usdCurrencyId is null)
            logger.LogWarning("USD currency ID not found in Keka. billingCurrencyId will be omitted.");

        var created = 0;
        var updated = 0;
        var failed = 0;

        var syncedEntries = new List<SyncedCompanyEntry>();
        var failedEntries = new List<FailedCompanyEntry>();
        var retryEntries = new List<RetryCompanyEntry>();

        foreach (var company in companies)
        {
            try
            {
                var companyId = company.Id.ToString();
                var request = KekaClientMapper.MapToKekaClientRequest(company, usdCurrencyId);
                var companyDateEntered = company.DateEntered!.Value;

                string? kekaEmployeeId = null;

                if (defaultProject?.ProjectManager is not null)
                {
                    kekaEmployeeId = await GetKekaEmployeeAsync(defaultProject.ProjectManager.Email, cancellationToken);
                }
                else
                {
                    logger.LogWarning("Default Project manager doesnot exists in database.");
                }

                if (!kekaClientIdByCompanyId.TryGetValue(companyId, out var kekaClientId))
                {
                    kekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                    created++;

                    logger.LogInformation("{SyncLabel}: Created Keka client for ConnectWise company {CompanyId} - {CompanyName}",
                        syncLabel, company.Id, company.Name);

                    try
                    {
                        await CreateDefaultProjectAsync(companyId, kekaClientId, companyDateEntered, kekaEmployeeId, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "{SyncLabel}: Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            syncLabel, company.Id, kekaClientId);
                        retryEntries.Add(new RetryCompanyEntry
                        {
                            Id = companyId,
                            Name = company.Name ?? string.Empty,
                            ErrorMessage = tex.Message
                        });
                    }
                    catch(Exception ex)
                    {
                        logger.LogError(ex,
                            "{SyncLabel}: Error creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            syncLabel, company.Id, kekaClientId);
                    }

                    syncedEntries.Add(new SyncedCompanyEntry
                    {
                        Id = companyId,
                        ClientId = kekaClientId,
                        DateEntered = companyDateEntered
                    });
                }
                else
                {
                    var updateRequest = KekaClientMapper.MapToKekaClientUpdateRequest(company);
                    await kekaClientService.UpdateClientAsync(kekaClientId, updateRequest, cancellationToken);
                    updated++;

                    logger.LogInformation("{SyncLabel}: Updated Keka client {KekaClientId} for ConnectWise company {CompanyId} - {CompanyName}",
                        syncLabel, kekaClientId, company.Id, company.Name);

                    try
                    {
                        await CreateDefaultProjectAsync(companyId, kekaClientId, companyDateEntered, kekaEmployeeId, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "{SyncLabel}: Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            syncLabel, company.Id, kekaClientId);
                        retryEntries.Add(new RetryCompanyEntry
                        {
                            Id = companyId,
                            Name = company.Name ?? string.Empty,
                            ErrorMessage = tex.Message
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "{SyncLabel}: Error creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            syncLabel, company.Id, kekaClientId);
                    }

                    syncedEntries.Add(new SyncedCompanyEntry
                    {
                        Id = companyId,
                        ClientId = kekaClientId,
                        DateEntered = companyDateEntered
                    });
                }
            }
            catch (TimeoutRejectedException tex)
            {
                failed++;
                logger.LogWarning(tex,
                    "{SyncLabel}: Timeout syncing ConnectWise company {CompanyId} - {CompanyName} to Keka.",
                    syncLabel, company.Id, company.Name);

                retryEntries.Add(new RetryCompanyEntry
                {
                    Id = company.Id.ToString(),
                    Name = company.Name ?? string.Empty,
                    ErrorMessage = tex.Message
                });
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "{SyncLabel}: Failed to sync ConnectWise company {CompanyId} - {CompanyName} to Keka",
                    syncLabel, company.Id, company.Name);

                failedEntries.Add(new FailedCompanyEntry
                {
                    Id = company.Id.ToString(),
                    Name = company.Name ?? string.Empty,
                    ErrorMessage = ex.Message
                });
            }
        }

        logger.LogInformation(
            "{SyncLabel} company sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed}",
            syncLabel, created, updated, failed);

        return new CompanySyncResult
        {
            SyncedEntries = syncedEntries,
            FailedEntries = failedEntries,
            RetryEntries = retryEntries,
            LastRecordUpdatedAt = companies[^1].LastUpdated,
            Total = companies.Count,
            Succeeded = created + updated,
            Failed = failed
        };
    }

    private static readonly (string Name, int TaskBillingType)[] DefaultTasks =
    [
        ("CW: Billable Charge Code",     1),
        ("CW: Non-Billable Charge Code", 0),
        ("CW: Billable Service Ticket",  1),
        ("CW: Non-Billable Service Ticket", 0),
    ];

    private async Task CreateDefaultProjectAsync(
        string companyId,
        string kekaClientId,
        DateTime startDate,
        string? kekaEmployeeId,
        CancellationToken cancellationToken)
    {
        var projectCode = $"{companyId}-CWDP";
        const string defaultProjectName = "CW: Default Project";

        var clientProjects = await kekaProjectService.GetProjectsByClientIdAsync(kekaClientId, cancellationToken);
        var existingDefaultProject = clientProjects.FirstOrDefault(p =>
            string.Equals(p.Code, projectCode, StringComparison.OrdinalIgnoreCase));

        // BillingType sync state provides the default billing type mappings (billingType → numeric mappedValue).
        var billingTypeSyncState = await syncStateService.GetAsync(SyncTypes.BillingType, cancellationToken);

        string kekaProjectId;

        if (existingDefaultProject is null)
        {
            var projectRequest = new KekaProjectRequest
            {
                ClientId   = kekaClientId,
                Name       = defaultProjectName,
                Code       = projectCode,
                Status     = 0,
                StartDate  = startDate,
                EndDate    = null,
                IsBillable = true,
                BillingType = int.Parse(billingTypeSyncState?.BillingType ?? "0"),
                ProjectManager = new List<string> { kekaEmployeeId ?? string.Empty }
            };

            kekaProjectId = await kekaProjectService.CreateProjectAsync(projectRequest, cancellationToken);
            logger.LogInformation(
                "Created default Keka project {ProjectId} for client {ClientId}.",
                kekaProjectId, kekaClientId);
        }
        else
        {
            kekaProjectId = existingDefaultProject.Id;
            logger.LogInformation(
                "Default Keka project already exists for client {ClientId}. ProjectId: {ProjectId}.",
                kekaClientId, kekaProjectId);
        }

        if (string.IsNullOrWhiteSpace(kekaProjectId))
        {
            logger.LogWarning(
                "Default project ID is empty for client {ClientId}. Skipping default task creation.",
                kekaClientId);
            return;
        }

        var taskStartDate = startDate.Date;
        var taskEndDate = DateTime.MaxValue;

        var existingTasks = await kekaProjectService.GetTasksByProjectAsync(kekaProjectId, cancellationToken);
        var existingTaskNames = existingTasks
            .Select(t => t.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (taskName, billingType) in DefaultTasks)
        {
            if (existingTaskNames.Contains(taskName))
            {
                logger.LogDebug(
                    "Default task '{TaskName}' already exists under project {ProjectId}. Skipping.",
                    taskName, kekaProjectId);
                continue;
            }

            var taskRequest = new KekaTaskRequest
            {
                ProjectId      = kekaProjectId,
                Name           = taskName,
                StartDate      = taskStartDate,
                EndDate        = taskEndDate,
                TaskBillingType = billingType
            };

            var taskId = await kekaProjectService.CreateTaskAsync(kekaProjectId, taskRequest, cancellationToken);
            logger.LogInformation(
                "Created default task '{TaskName}' ({TaskId}) under project {ProjectId}.",
                taskName, taskId, kekaProjectId);
        }
    }

    private async Task<string?> GetKekaEmployeeAsync(string email, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(defaultProjectManagerEmployeeIdCache))
            return defaultProjectManagerEmployeeIdCache;

        var employee = await kekaEmployeeService.SearchEmployeeByEmailAsync(email, cancellationToken);

        defaultProjectManagerEmployeeIdCache = employee?.Id;
        return defaultProjectManagerEmployeeIdCache;
    }
}
