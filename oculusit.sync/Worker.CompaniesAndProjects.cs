using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.orchestration;
using Polly.Timeout;
using System.Data;
using System.Text.Json;

namespace oculusit.sync;

public sealed partial class Worker
{
    private readonly Dictionary<string, KekaEmployee?> employeeCache = new();
    private readonly Dictionary<string, string?> kekaClientIdByOculusKekaClientId = new();

    private async Task SyncInitialCompaniesAndProjectSnapshotAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        var cwCompanies = await connectWiseService.GetAllCompaniesAsync(stoppingToken);
        var kekaClients = await kekaClientService.GetAllClientsAsync(stoppingToken);

        var cwProjects = await connectWiseProjectService.GetAllProjectsAsync(stoppingToken);
        var kekaProjects = await kekaProjectService.GetAllProjectsAsync(stoppingToken);

        logger.LogInformation(
            "Building company/project snapshot from {CompanyCount} CW companies, {ClientCount} Keka clients, {ProjectCount} CW projects and {KekaProjectCount} Keka projects.",
            cwCompanies.Count,
            kekaClients.Count,
            cwProjects.Count,
            kekaProjects.Count);

        var companiesById = cwCompanies
            .GroupBy(c => c.Id.ToString())
            .ToDictionary(g => g.Key, g => g.First());

        var projectsById = cwProjects
            .GroupBy(p => p.Id.ToString())
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<InitialCompanyAndProjectEntry>();

        foreach (var client in kekaClients)
        {
            var clientCode = client.Code?.Trim();

            companiesById.TryGetValue(
                clientCode ?? string.Empty,
                out var matchedCompany);

            var clientProjects = kekaProjects
                .Where(p => p.ClientId == client.Id)
                .ToList();

            var initialProjects = new List<InitialProjectEntry>();

            foreach (var kp in clientProjects)
            {
                var projectCode = kp.Code?.Trim();

                projectsById.TryGetValue(
                    projectCode ?? string.Empty,
                    out var matchedProject);

                initialProjects.Add(new InitialProjectEntry
                {
                    ProjectId = matchedProject?.Id.ToString() ?? string.Empty,
                    ProjectName = matchedProject?.Name ?? string.Empty,

                    KekaProjectId = kp.Id,
                    KekaProjectCode = kp.Code ?? string.Empty,
                    KekaProjectName = kp.Name ?? string.Empty
                });
            }

            result.Add(new InitialCompanyAndProjectEntry
            {
                CompanyId = matchedCompany?.Id.ToString() ?? string.Empty,
                CompanyName = matchedCompany?.Name ?? string.Empty,

                KekaClientId = client.Id,
                KekaClientCode = client.Code ?? string.Empty,
                KekaClientName = client.Name ?? string.Empty,

                InitialProjects = initialProjects
            });
        }

        logger.LogInformation(
            "Company/project snapshot built with {Count} companies.",
            result.Count);

        await using var stream = File.Create("InitialCompanyAndProjectSnapshot.json");

        await JsonSerializer.SerializeAsync(
            stream,
            result,
            new JsonSerializerOptions
            {
                WriteIndented = true
            },
            stoppingToken);

        logger.LogInformation(
            "Saved InitialCompany and project snapshot with {Count} rows.",
            result.Count);
    }

    private async Task SyncCompaniesAndProjectsAsync(
        DateTime syncStartedAt,
        CancellationToken stoppingToken)
    {
        logger.LogInformation("Running full company sync.");
            
        var syncedEntries = await SyncCompaniesToKekaAsync(syncStartedAt, stoppingToken);
            
        logger.LogInformation(
            "Full OculusIT keka client sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}.",
            syncedEntries.Total, syncedEntries.Succeeded, syncedEntries.Failed);

        //------------------------------------PROJECT SYNC------------------------------------------

        logger.LogInformation("Running full project sync.");

        var result = await SyncProjectsToKekaAsync(syncStartedAt, stoppingToken);
        var lastUpdatedAt = result.LastRecordUpdatedAt ?? syncStartedAt;

        logger.LogInformation(
            "Full OculusIT keka project sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
            result.Total, result.Succeeded, result.Failed, lastUpdatedAt);
    }

    public async Task<CompanySyncResult> SyncCompaniesToKekaAsync(DateTime syncStartedAt, CancellationToken cancellationToken = default)
    {
        var allKekaClients = await oculusITKekaClientAndProjectService.GetAllOculusITKekaClientsAsync(cancellationToken);

        logger.LogInformation("Fetched {Count} clients from OculusIt Keka. Starting Keka sync.", allKekaClients.Count);

        if (allKekaClients.Count == 0)
            return new CompanySyncResult();

        var allDemoClients = await kekaClientService.GetAllClientsAsync(cancellationToken);

        var usdCurrencyId = await kekaCurrencyService.GetUsdCurrencyIdAsync(cancellationToken);
        if (usdCurrencyId is null)
            logger.LogWarning("USD currency ID not found in Keka. billingCurrencyId will be omitted.");

        var created = 0;
        var updated = 0;
        var failed = 0;
        var skipped = 0;

        var syncedEntries = new List<SyncedCompanyEntry>();

        foreach (var client in allKekaClients)
        {
            try
            {
                var demoClient = allDemoClients.FirstOrDefault(dc => dc.Name == client.Name);
                if (demoClient != null)
                {
                    logger.LogInformation("{ClientName} Client already exist.", client.Name);
                    skipped++;
                    kekaClientIdByOculusKekaClientId.Add(client?.Id.ToString() ?? string.Empty, demoClient.Id);
                    continue;
                }

                var request = MapToKekaClientRequest(client, usdCurrencyId);

                var kekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                created++;

                logger.LogInformation("OculusIT Keka Portal Sync: Created Keka client for Oculus Keka client {ClientId} - {ClientName}",
                    client.Id, client.Name);

                syncedEntries.Add(new SyncedCompanyEntry
                {
                    Id = client?.Code ?? string.Empty,
                    ClientId = kekaClientId
                });
                kekaClientIdByOculusKekaClientId.Add(client?.Id.ToString() ?? string.Empty, kekaClientId);
            }
            catch (TimeoutRejectedException tex)
            {
                failed++;
                logger.LogWarning(tex,
                    "OculusIT Keka Portal Sync: Timeout syncing OculusIT keka client {ClientId} - {ClientName} to demo keka portal.",
                    client.Id, client.Name);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "OculusIT Keka Portal Sync: Failed to sync OculusIT keka client {ClientId} - {ClientName} to demo keka portal.",
                    client.Id, client.Name);
            }
        }

        logger.LogInformation(
            "OculusIT Keka Portal Sync: Client sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed} , Skipped: {Skipped}",
            created, updated, failed, skipped);

        return new CompanySyncResult
        {
            SyncedEntries = syncedEntries,
            LastRecordUpdatedAt = syncStartedAt,
            Total = allKekaClients.Count,
            Succeeded = created + updated,
            Failed = failed
        };
    }

    public static KekaClientRequest MapToKekaClientRequest(
        KekaClient client,
        string? usdCurrencyId = null)
    {
        var addressLine1 = NullIfEmpty(client?.BillingAddress?.AddressLine1);
        var addressLine2 = NullIfEmpty(client?.BillingAddress?.AddressLine2);
        var city = NullIfEmpty(client?.BillingAddress?.City);
        var state = NullIfEmpty(client?.BillingAddress?.State);
        var zip = NullIfEmpty(client?.BillingAddress?.Zip);
        // CountryCode is mandatory in Keka — always falls back to "US" when not resolvable.
        var countryCode = NullIfEmpty(client?.BillingAddress?.CountryCode);

        // BillingAddress is always built so the mandatory countryCode is always present.
        // If neither a currency ID nor any address detail exists, omit billing info entirely.
        var hasBillingData = usdCurrencyId is not null
                          || addressLine1 is not null || city is not null
                          || state is not null || zip is not null;

        KekaBillingAddress? billingAddress = hasBillingData
            ? new KekaBillingAddress
            {
                AddressLine1 = addressLine1,
                AddressLine2 = addressLine2,
                CountryCode = countryCode ?? "US",
                City = city,
                State = state,
                Zip = zip
            }
            : null;

        KekaBillingInfo? billingInfo = hasBillingData
            ? new KekaBillingInfo
            {
                BillingCurrencyId = usdCurrencyId,
                BillingAddress = billingAddress
            }
            : null;

        return new KekaClientRequest
        {
            Name = client?.Name ?? string.Empty,
            Description = NullIfEmpty(client?.Description),
            Code = client?.Code ?? string.Empty,
            BillingInfo = billingInfo
        };
    }

    public async Task<orchestration.ProjectSyncResult> SyncProjectsToKekaAsync(
        DateTime syncStartedAt,
        CancellationToken cancellationToken = default)
    {
        var allKekaProjects = await oculusITKekaClientAndProjectService.GetAllOculusITKekaProjectsAsync(cancellationToken);
        var allDemoProjects = await kekaProjectService.GetAllProjectsAsync(cancellationToken);

        logger.LogInformation("Fetched {Count} all OculusIT Keka projects.",
            allKekaProjects.Count);

        // Full OculusIT Keka portal project sync.
        var created = 0;
        var updated = 0;
        var failed = 0;
        var skipped = 0;

        var syncedEntries = new List<SyncedProjectEntry>();

        foreach (var project in allKekaProjects)
        {
            try
            {
                var oculusKekaClientId = project.ClientId.ToString();

                if (!kekaClientIdByOculusKekaClientId.TryGetValue(oculusKekaClientId, out var kekaClientId))
                {
                    logger.LogWarning(
                        "No Keka client found for OculusIT Keka portal client ID {ClientId} on project {ProjectId} - {ProjectName}. Skipping.",
                        kekaClientId, project.Id, project.Name);
                    failed++;
                    continue;
                }

                if (allDemoProjects.Any(dc => dc.Name == project.Name))
                {
                    logger.LogInformation("{ProjectName} Project already exist.", project.Name);
                    skipped++;
                    continue;
                }

                var kekaEmployees = (await Task.WhenAll(
                    project.ProjectManagers.Select(pm =>
                        GetKekaEmployeeAsync(pm.Email.Trim(), cancellationToken))))
                    .Where(e => e is not null)
                    .Cast<KekaEmployee>()
                    .ToList();

                var projectManagerIds = kekaEmployees
                    .Select(e => e.Id)
                    .ToList();

                var request = MapToKekaProjectRequest(project, kekaClientId, projectManagerIds);
                var kekaProjectId = await kekaProjectService.CreateProjectAsync(request, cancellationToken);
                logger.LogInformation("Created Keka project {KekaProjectId} for OculusIT Keka portal project {ProjectId} - {ProjectName}.",
                    kekaProjectId, project.Id, project.Name);

                created++;
                syncedEntries.Add(new SyncedProjectEntry
                {
                    Id = project.Id.ToString(),
                    KekaClientId = kekaClientId,
                    KekaProjectId = kekaProjectId
                });
            }
            catch (TimeoutRejectedException tex)
            {
                logger.LogWarning(tex,
                    "Timeout syncing ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync ConnectWise project {ProjectId} - {ProjectName} to Keka.",
                    project.Id, project.Name);
                failed++;
            }
        }

        logger.LogInformation(
            "Full project sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed}, Skipped: {Skipped}.",
            created, updated, failed, skipped);

        return new orchestration.ProjectSyncResult
        {
            SyncedEntries = syncedEntries,
            LastRecordUpdatedAt = syncStartedAt,
            Total = allKekaProjects.Count,
            Succeeded = created + updated,
            Failed = failed
        };
    }

    public async Task<KekaEmployee?> GetKekaEmployeeAsync(string email, CancellationToken cancellationToken)
    {
        if (employeeCache.TryGetValue(email, out var employee))
            return employee;

        employee = await kekaEmployeeService.SearchEmployeeByEmailAsync(email, cancellationToken);

        employeeCache[email] = employee;

        return employee;
    }

    public static KekaProjectRequest MapToKekaProjectRequest(
        KekaProject project,
        string kekaClientId,
        List<string>? projectManagersId)
    {
        return new KekaProjectRequest
        {
            ClientId = kekaClientId,
            Name = project.Name,
            Description = project.Description,
            Code = project?.Code?.ToString() ?? string.Empty,
            Status = project.Status,
            StartDate = project.StartDate ?? DateTime.MinValue,
            EndDate = project.EndDate,
            IsBillable = project.IsBillable,
            BillingType = project.BillingType,
            ProjectManager = projectManagersId
        };
    }

    private static string? NullIfEmpty(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;
}
