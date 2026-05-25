using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
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

        // Fetch USD currency ID once before the loop
        var usdCurrencyId = await kekaCurrencyService.GetUsdCurrencyIdAsync(cancellationToken);
        if (usdCurrencyId is null)
            logger.LogWarning("USD currency ID not found in Keka. billingCurrencyId will be omitted.");

        // Fetch all Keka clients once and index by ConnectWise company ID (code).
        // Clients with a null code are ignored for sync purposes.
        var allKekaClients = await kekaClientService.GetAllClientsAsync(cancellationToken);
        var kekaClientsByCode = allKekaClients
            .Where(c => !string.IsNullOrEmpty(c.Code))
            .ToDictionary(c => c.Code!);

        logger.LogInformation("Fetched {Count} existing Keka clients. {Indexed} have a ConnectWise code.",
            allKekaClients.Count, kekaClientsByCode.Count);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var failed  = 0;

        var syncedEntries = new List<SyncedCompanyEntry>();
        var failedCompaniesEntries = new List<FailedCompanyEntry>();
        var retryEntries = new List<RetryCompanyEntry>();
        var defaultProjectRetryEntries = new List<DefaultProjectRetryEntry>();

        foreach (var company in companies)
        {
            try
            {
                var request = KekaClientMapper.MapToKekaClientRequest(company, usdCurrencyId);
                var companyDateEntered = company.DateEntered!.Value;

                if (!kekaClientsByCode.TryGetValue(company.Id.ToString(), out var existing))
                {
                    var kekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                    logger.LogInformation("Created Keka client for ConnectWise company {CompanyId} - {CompanyName}",
                        company.Id, company.Name);
                    created++;

                    try
                    {
                        await CreateDefaultProjectAsync(company.Id.ToString(), kekaClientId, companyDateEntered, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            company.Id, kekaClientId);
                        defaultProjectRetryEntries.Add(new DefaultProjectRetryEntry
                        {
                            CompanyId = company.Id.ToString(),
                            ClientId = kekaClientId,
                            ErrorMessage = tex.Message
                        });
                    }

                    syncedEntries.Add(new SyncedCompanyEntry
                    {
                        Id          = company.Id.ToString(),
                        ClientId    = kekaClientId,
                        DateEntered = companyDateEntered
                    });
                    continue;
                }
                else 
                {
                    var updateRequest = KekaClientMapper.MapToKekaClientUpdateRequest(company);
                    await kekaClientService.UpdateClientAsync(existing.Id, updateRequest, cancellationToken);
                    logger.LogInformation("Updated Keka client {KekaClientId} for ConnectWise company {CompanyId} - {CompanyName}",
                        existing.Id, company.Id, company.Name);
                    updated++;

                    try
                    {
                        await CreateDefaultProjectAsync(company.Id.ToString(), existing.Id, companyDateEntered, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            company.Id, existing.Id);
                        defaultProjectRetryEntries.Add(new DefaultProjectRetryEntry
                        {
                            CompanyId = company.Id.ToString(),
                            ClientId = existing.Id,
                            ErrorMessage = tex.Message
                        });
                    }
                }

                syncedEntries.Add(new SyncedCompanyEntry
                {
                    Id          = company.Id.ToString(),
                    ClientId    = existing.Id,
                    DateEntered = companyDateEntered
                });
            }
            catch (TimeoutRejectedException tex)
            {
                logger.LogWarning(tex,
                    "Timeout syncing ConnectWise company {CompanyId} - {CompanyName} to Keka.",
                    company.Id, company.Name);
                failed++;
                retryEntries.Add(new RetryCompanyEntry
                {
                    Id           = company.Id.ToString(),
                    Name         = company.Name ?? string.Empty,
                    ErrorMessage = tex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync ConnectWise company {CompanyId} - {CompanyName} to Keka",
                    company.Id, company.Name);
                failed++;
                failedCompaniesEntries.Add(new FailedCompanyEntry
                {
                    Id           = company.Id.ToString(),
                    Name         = company.Name ?? string.Empty,
                    ErrorMessage = ex.Message
                });
            }
        }

        logger.LogInformation(
            "Keka sync complete. Created: {Created}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
            created, updated, skipped, failed);

        return new CompanySyncResult
        {
            SyncedEntries              = syncedEntries,
            FailedEntries              = failedCompaniesEntries,
            RetryEntries               = retryEntries,
            DefaultProjectRetryEntries = defaultProjectRetryEntries,
            LastRecordUpdatedAt        = companies[^1].LastUpdated,
            Total     = companies.Count,
            Succeeded = created + updated,
            Failed    = failed
        };
    }

    public async Task<CompanySyncResult> SyncCompaniesIncrementalAsync(
        SyncState syncState, CancellationToken cancellationToken = default)
    {
        var since = syncState.LastUpdatedAt!.Value;

        var companies = await connectWiseService.GetCompaniesSinceAsync(since, cancellationToken);
        logger.LogInformation("Incremental fetch returned {Count} companies updated since {Since}.", companies.Count, since);

        if (companies.Count == 0)
            return new CompanySyncResult();

        var usdCurrencyId = await kekaCurrencyService.GetUsdCurrencyIdAsync(cancellationToken);
        if (usdCurrencyId is null)
            logger.LogWarning("USD currency ID not found in Keka. billingCurrencyId will be omitted.");

        // Build a lookup from ConnectWise company ID → Keka client ID using the
        // persisted sync state. This avoids fetching all Keka clients on every run.
        var kekaIdByCompanyId = syncState.Companies
            .ToDictionary(e => e.Id, e => e.ClientId);

        var created = 0;
        var updated = 0;
        var failed  = 0;

        // Only newly created entries are returned — updates don't change the mapping.
        var newEntries = new List<SyncedCompanyEntry>();
        var failedCompaniesEntries = new List<FailedCompanyEntry>();
        var retryEntries = new List<RetryCompanyEntry>();
        var defaultProjectRetryEntries = new List<DefaultProjectRetryEntry>();

        foreach (var company in companies)
        {
            try
            {
                var request = KekaClientMapper.MapToKekaClientRequest(company, usdCurrencyId);
                var companyIdStr = company.Id.ToString();
                var companyDateEntered = company.DateEntered!.Value;

                if (kekaIdByCompanyId.TryGetValue(companyIdStr, out var kekaClientId))
                {
                    // Known company — update the existing Keka client directly by ID.
                    var updateRequest = KekaClientMapper.MapToKekaClientUpdateRequest(company);
                    await kekaClientService.UpdateClientAsync(kekaClientId, updateRequest, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Updated Keka client {KekaClientId} for ConnectWise company {CompanyId} - {CompanyName}",
                        kekaClientId, company.Id, company.Name);
                    updated++;
                }
                else
                {
                    // New company — create a Keka client and record the new mapping.
                    var newKekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                    logger.LogInformation(
                        "Incremental: Created Keka client {KekaClientId} for ConnectWise company {CompanyId} - {CompanyName}",
                        newKekaClientId, company.Id, company.Name);
                    created++;

                    try
                    {
                        await CreateDefaultProjectAsync(company.Id.ToString(), newKekaClientId, companyDateEntered, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "Incremental: Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            company.Id, newKekaClientId);
                        defaultProjectRetryEntries.Add(new DefaultProjectRetryEntry
                        {
                            CompanyId = company.Id.ToString(),
                            ClientId = newKekaClientId,
                            ErrorMessage = tex.Message
                        });
                    }

                    newEntries.Add(new SyncedCompanyEntry
                    {
                        Id          = companyIdStr,
                        ClientId    = newKekaClientId,
                        DateEntered = companyDateEntered
                    });
                }
            }
            catch (TimeoutRejectedException tex)
            {
                logger.LogWarning(tex,
                    "Incremental: Timeout syncing ConnectWise company {CompanyId} - {CompanyName} to Keka.",
                    company.Id, company.Name);
                failed++;
                retryEntries.Add(new RetryCompanyEntry
                {
                    Id           = company.Id.ToString(),
                    Name         = company.Name ?? string.Empty,
                    ErrorMessage = tex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Incremental: Failed to sync ConnectWise company {CompanyId} - {CompanyName} to Keka",
                    company.Id, company.Name);
                failed++;
                failedCompaniesEntries.Add(new FailedCompanyEntry
                {
                    Id           = company.Id.ToString(),
                    Name         = company.Name ?? string.Empty,
                    ErrorMessage = ex.Message
                });
            }
        }

        logger.LogInformation(
            "Incremental Keka sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed}",
            created, updated, failed);

        return new CompanySyncResult
        {
            SyncedEntries              = newEntries,
            FailedEntries              = failedCompaniesEntries,
            RetryEntries               = retryEntries,
            DefaultProjectRetryEntries = defaultProjectRetryEntries,
            LastRecordUpdatedAt        = companies[^1].LastUpdated,
            Total     = companies.Count,
            Succeeded = created + updated,
            Failed    = failed
        };
    }

    public async Task<CompanySyncResult> RetryCompaniesAsync(
        IReadOnlyList<string> companyIds,
        CancellationToken cancellationToken = default)
    {
        if (companyIds.Count == 0)
            return new CompanySyncResult();

        var targetIds = companyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targetIds.Count == 0)
            return new CompanySyncResult();

        var companies = await connectWiseService.GetAllCompaniesAsync(cancellationToken);
        var retryCompanies = companies
            .Where(c => targetIds.Contains(c.Id.ToString()))
            .ToList();

        if (retryCompanies.Count == 0)
        {
            logger.LogInformation("No retry companies matched current ConnectWise dataset.");
            return new CompanySyncResult();
        }

        logger.LogInformation("Retrying {Count} companies from retry/failure SyncState before normal sync.", retryCompanies.Count);

        var usdCurrencyId = await kekaCurrencyService.GetUsdCurrencyIdAsync(cancellationToken);
        if (usdCurrencyId is null)
            logger.LogWarning("USD currency ID not found in Keka. billingCurrencyId will be omitted.");

        var allKekaClients = await kekaClientService.GetAllClientsAsync(cancellationToken);
        var kekaClientsByCode = allKekaClients
            .Where(c => !string.IsNullOrEmpty(c.Code))
            .ToDictionary(c => c.Code!);

        var syncedEntries = new List<SyncedCompanyEntry>();
        var failedEntries = new List<FailedCompanyEntry>();
        var retryEntries = new List<RetryCompanyEntry>();
        var defaultProjectRetryEntries = new List<DefaultProjectRetryEntry>();

        var created = 0;
        var updated = 0;
        var failed = 0;

        foreach (var company in retryCompanies)
        {
            try
            {
                var request = KekaClientMapper.MapToKekaClientRequest(company, usdCurrencyId);
                var companyDateEntered = company.DateEntered!.Value;

                if (!kekaClientsByCode.TryGetValue(company.Id.ToString(), out var existing))
                {
                    var kekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                    created++;

                    try
                    {
                        await CreateDefaultProjectAsync(company.Id.ToString(), kekaClientId, companyDateEntered, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "Retry: Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            company.Id, kekaClientId);
                        defaultProjectRetryEntries.Add(new DefaultProjectRetryEntry
                        {
                            CompanyId = company.Id.ToString(),
                            ClientId = kekaClientId,
                            ErrorMessage = tex.Message
                        });
                    }

                    syncedEntries.Add(new SyncedCompanyEntry
                    {
                        Id = company.Id.ToString(),
                        ClientId = kekaClientId,
                        DateEntered = companyDateEntered
                    });
                }
                else
                {
                    var updateRequest = KekaClientMapper.MapToKekaClientUpdateRequest(company);
                    await kekaClientService.UpdateClientAsync(existing.Id, updateRequest, cancellationToken);
                    updated++;

                    try
                    {
                        await CreateDefaultProjectAsync(company.Id.ToString(), existing.Id, companyDateEntered, cancellationToken);
                    }
                    catch (TimeoutRejectedException tex)
                    {
                        logger.LogWarning(tex,
                            "Retry: Timeout creating default project for ConnectWise company {CompanyId} and Keka client {ClientId}.",
                            company.Id, existing.Id);
                        defaultProjectRetryEntries.Add(new DefaultProjectRetryEntry
                        {
                            CompanyId = company.Id.ToString(),
                            ClientId = existing.Id,
                            ErrorMessage = tex.Message
                        });
                    }

                    syncedEntries.Add(new SyncedCompanyEntry
                    {
                        Id = company.Id.ToString(),
                        ClientId = existing.Id,
                        DateEntered = companyDateEntered
                    });
                }
            }
            catch (TimeoutRejectedException tex)
            {
                failed++;
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
                failedEntries.Add(new FailedCompanyEntry
                {
                    Id = company.Id.ToString(),
                    Name = company.Name ?? string.Empty,
                    ErrorMessage = ex.Message
                });
            }
        }

        return new CompanySyncResult
        {
            SyncedEntries = syncedEntries,
            FailedEntries = failedEntries,
            RetryEntries = retryEntries,
            DefaultProjectRetryEntries = defaultProjectRetryEntries,
            LastRecordUpdatedAt = retryCompanies[^1].LastUpdated,
            Total = retryCompanies.Count,
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
        var endDate = startDate.AddYears(10);
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
