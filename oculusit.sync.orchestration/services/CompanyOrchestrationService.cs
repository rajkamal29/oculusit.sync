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
    ISyncStateService syncStateService,
    ILogger<CompanyOrchestrationService> logger) : ICompanyOrchestrationService
{
    public async Task<IReadOnlyList<InitialCompanyEntry>> BuildInitialCompanySnapshotAsync(CancellationToken cancellationToken = default)
    {
        var companies = await connectWiseService.GetAllCompaniesAsync(cancellationToken);
        var clients = await kekaClientService.GetAllClientsAsync(cancellationToken);

        logger.LogInformation(
            "Building initial company snapshot from {CompanyCount} ConnectWise companies and {ClientCount} Keka clients.",
            companies.Count, clients.Count);

        var snapshots = new List<InitialCompanyEntry>();

        // CW company id -> company
        var companiesById = companies
            .GroupBy(c => c.Id.ToString())
            .ToDictionary(g => g.Key, g => g.First());

        // Keka client code -> first client (deterministic), with duplicate-code warning
        var clientsByCode = new Dictionary<string, KekaClient>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in clients)
        {
            var code = client.Code?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                continue;

            if (!clientsByCode.TryAdd(code, client))
            {
                logger.LogWarning(
                    "Duplicate Keka client code {Code} found. Keeping the first client and ignoring client {ClientId}.",
                    code, client.Id);
            }
        }

        // Left side: all ConnectWise companies
        foreach (var company in companies)
        {
            var companyId = company.Id.ToString();

            if (clientsByCode.TryGetValue(companyId, out var matchedClient))
            {
                snapshots.Add(new InitialCompanyEntry
                {
                    CompanyId   = companyId,
                    CompanyName = company.Name ?? string.Empty,
                    ClientId    = matchedClient.Id,
                    ClientCode  = matchedClient.Code ?? string.Empty,
                    ClientName  = matchedClient.Name ?? string.Empty
                });
            }
            else
            {
                snapshots.Add(new InitialCompanyEntry
                {
                    CompanyId   = companyId,
                    CompanyName = company.Name ?? string.Empty,
                    ClientId    = string.Empty,
                    ClientCode  = string.Empty,
                    ClientName  = string.Empty
                });
            }
        }

        // Right-only side: Keka clients whose code doesn't match any CW company id.
        foreach (var client in clients)
        {
            var code = client.Code?.Trim();
            if (!string.IsNullOrWhiteSpace(code) && companiesById.ContainsKey(code))
                continue;

            snapshots.Add(new InitialCompanyEntry
            {
                CompanyId   = string.Empty,
                CompanyName = string.Empty,
                ClientId    = client.Id,
                ClientCode  = code ?? string.Empty,
                ClientName  = client.Name ?? string.Empty
            });
        }

        logger.LogInformation("Initial company snapshot built with {Count} rows.", snapshots.Count);
        return snapshots;
    }

    public async Task<CompanySyncResult> SyncCompaniesToKekaAsync(CancellationToken cancellationToken = default)
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

        return await ProcessCompaniesAsync(
            companies,
            kekaClientIdByCompanyId,
            syncLabel: "Full",
            cancellationToken: cancellationToken);
    }

    public async Task<CompanySyncResult> SyncCompaniesIncrementalAsync(
        SyncState syncState,
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

                if (!kekaClientIdByCompanyId.TryGetValue(companyId, out var kekaClientId))
                {
                    kekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                    created++;

                    logger.LogInformation("{SyncLabel}: Created Keka client for ConnectWise company {CompanyId} - {CompanyName}",
                        syncLabel, company.Id, company.Name);

                    try
                    {
                        await CreateDefaultProjectAsync(companyId, kekaClientId, companyDateEntered, cancellationToken);
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
                        await CreateDefaultProjectAsync(companyId, kekaClientId, companyDateEntered, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var endDate = DateTime.MaxValue;
        var projectCode = $"{companyId}-CWDP";
        const string defaultProjectName = "CW: Default Project";

        var clientProjects = await kekaProjectService.GetProjectsByClientIdAsync(kekaClientId, cancellationToken);
        var existingDefaultProject = clientProjects.FirstOrDefault(p =>
            string.Equals(p.Code, projectCode, StringComparison.OrdinalIgnoreCase));

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
                EndDate    = endDate,
                IsBillable = true
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
        var taskEndDate = endDate.Date;

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
}
