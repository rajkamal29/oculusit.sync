using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.services;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration.mappings;

namespace oculusit.sync.orchestration.services;

public sealed class CompanyOrchestrationService(
    IConnectWiseService connectWiseService,
    IKekaClientService kekaClientService,
    IKekaCurrencyService kekaCurrencyService,
    ILogger<CompanyOrchestrationService> logger) : ICompanyOrchestrationService
{
    public async Task<IReadOnlyList<SyncedCompanyEntry>> SyncCompaniesToKekaAsync(CancellationToken cancellationToken = default)
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

        foreach (var company in companies)
        {
            try
            {
                var request = KekaClientMapper.MapToKekaClientRequest(company, usdCurrencyId);

                if (!kekaClientsByCode.TryGetValue(company.Id.ToString(), out var existing))
                {
                    var kekaClientId = await kekaClientService.CreateClientAsync(request, cancellationToken);
                    logger.LogInformation("Created Keka client for ConnectWise company {CompanyId} - {CompanyName}",
                        company.Id, company.Name);
                    created++;
                    syncedEntries.Add(new SyncedCompanyEntry
                    {
                        Id       = company.Id.ToString(),
                        ClientId = kekaClientId
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
                }
              
                syncedEntries.Add(new SyncedCompanyEntry
                {
                    Id       = company.Id.ToString(),
                    ClientId = existing.Id
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync ConnectWise company {CompanyId} - {CompanyName} to Keka",
                    company.Id, company.Name);
                failed++;
            }
        }

        logger.LogInformation(
            "Keka sync complete. Created: {Created}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
            created, updated, skipped, failed);

        return syncedEntries;
    }

    public async Task<IReadOnlyList<SyncedCompanyEntry>> SyncCompaniesIncrementalAsync(
        SyncState syncState, CancellationToken cancellationToken = default)
    {
        var since = syncState.LastUpdatedAt!.Value;

        var companies = await connectWiseService.GetCompaniesSinceAsync(since, cancellationToken);
        logger.LogInformation("Incremental fetch returned {Count} companies updated since {Since}.", companies.Count, since);

        if (companies.Count == 0)
            return [];

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

        foreach (var company in companies)
        {
            try
            {
                var request = KekaClientMapper.MapToKekaClientRequest(company, usdCurrencyId);
                var companyIdStr = company.Id.ToString();

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
                    newEntries.Add(new SyncedCompanyEntry
                    {
                        Id       = companyIdStr,
                        ClientId = newKekaClientId
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Incremental: Failed to sync ConnectWise company {CompanyId} - {CompanyName} to Keka",
                    company.Id, company.Name);
                failed++;
            }
        }

        logger.LogInformation(
            "Incremental Keka sync complete. Created: {Created}, Updated: {Updated}, Failed: {Failed}",
            created, updated, failed);

        return newEntries;
    }
}
