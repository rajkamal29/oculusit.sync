using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.core.configurations;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;

namespace oculusit.sync.core.services;

public sealed class DynamoDbSyncStateService(
    IAmazonDynamoDB dynamoDb,
    IOptions<DynamoDbConfiguration> options,
    ILogger<DynamoDbSyncStateService> logger) : ISyncStateService
{
    private const string KeyAttribute           = "syncType";
    private const string LastUpdatedAtAttribute = "lastUpdatedAt";
    private const string CompaniesAttribute     = "companies";
    private const string FailedCompaniesAttribute = "failedCompanies";
    private const string IdAttribute            = "id";
    private const string CompanyNameAttribute   = "companyName";
    private const string ClientIdAttribute      = "clientId";
    private const string ErrorMessageAttribute  = "errorMessage";

    private readonly string _tableName = options.Value.TableName;

    public async Task<SyncState?> GetAsync(string syncType, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Reading DynamoDB sync state for syncType={SyncType} from table {Table}.", syncType, _tableName);

        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = syncType }
            }
        };

        var response = await dynamoDb.GetItemAsync(request, cancellationToken);

        if (!response.IsItemSet)
        {
            logger.LogDebug("No sync state found for syncType={SyncType}.", syncType);
            return null;
        }

        DateTime? lastUpdatedAt = null;
        if (response.Item.TryGetValue(LastUpdatedAtAttribute, out var tsAttr)
            && DateTime.TryParse(tsAttr.S, out var parsed))
        {
            lastUpdatedAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        var companies = new List<SyncedCompanyEntry>();
        if (response.Item.TryGetValue(CompaniesAttribute, out var listAttr) && listAttr.L is { Count: > 0 })
        {
            foreach (var entry in listAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(ClientIdAttribute, out var clientIdAttr);
                companies.Add(new SyncedCompanyEntry
                {
                    Id       = idAttr?.S ?? string.Empty,
                    ClientId = clientIdAttr?.S ?? string.Empty
                });
            }
        }

        var failedCompanies = new List<FailedCompanyEntry>();
        if (response.Item.TryGetValue(FailedCompaniesAttribute, out var failedListAttr) && failedListAttr.L is { Count: > 0 })
        {
            foreach (var entry in failedListAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(CompanyNameAttribute, out var companyNameAttr);
                entry.M.TryGetValue(ErrorMessageAttribute, out var errorAttr);
                failedCompanies.Add(new FailedCompanyEntry
                {
                    Id           = idAttr?.S ?? string.Empty,
                    CompanyName  = companyNameAttr?.S ?? string.Empty,
                    ErrorMessage = errorAttr?.S ?? string.Empty
                });
            }
        }

        return new SyncState
        {
            SyncType         = syncType,
            LastUpdatedAt    = lastUpdatedAt,
            Companies        = companies,
            FailedCompanies  = failedCompanies
        };
    }

    public async Task SaveAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving DynamoDB sync state for syncType={SyncType} to table {Table}.", state.SyncType, _tableName);

        var item = new Dictionary<string, AttributeValue>
        {
            [KeyAttribute] = new AttributeValue { S = state.SyncType }
        };

        if (state.LastUpdatedAt.HasValue)
            item[LastUpdatedAtAttribute] = new AttributeValue { S = state.LastUpdatedAt.Value.ToString("o") };

        if (state.Companies.Count > 0)
        {
            item[CompaniesAttribute] = new AttributeValue
            {
                L = state.Companies.Select(c => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [IdAttribute]       = new AttributeValue { S = c.Id },
                        [ClientIdAttribute] = new AttributeValue { S = c.ClientId }
                    }
                }).ToList()
            };
        }

        if (state.FailedCompanies.Count > 0)
        {
            item[FailedCompaniesAttribute] = new AttributeValue
            {
                L = state.FailedCompanies.Select(f => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [IdAttribute]           = new AttributeValue { S = f.Id },
                        [CompanyNameAttribute]  = new AttributeValue { S = f.CompanyName },
                        [ErrorMessageAttribute] = new AttributeValue { S = f.ErrorMessage }
                    }
                }).ToList()
            };
        }

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item      = item
        };

        await dynamoDb.PutItemAsync(request, cancellationToken);

        logger.LogInformation("Saved sync state for syncType={SyncType}, lastUpdatedAt={LastUpdatedAt}, companies={Companies}, failedCompanies={FailedCompanies}.",
            state.SyncType, state.LastUpdatedAt, state.Companies.Count, state.FailedCompanies.Count);
    }

    public async Task AppendSyncStateAsync(
        string syncType,
        SyncState incrementalState,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Appending incremental sync state for syncType={SyncType}. companies={Companies}, failedCompanies={FailedCompanies}",
            syncType, incrementalState.Companies.Count, incrementalState.FailedCompanies.Count);

        var companyItems = incrementalState.Companies.Select(c => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]       = new AttributeValue { S = c.Id },
                [ClientIdAttribute] = new AttributeValue { S = c.ClientId }
            }
        }).ToList();

        var failedItems = incrementalState.FailedCompanies.Select(f => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]           = new AttributeValue { S = f.Id },
                [CompanyNameAttribute]  = new AttributeValue { S = f.CompanyName },
                [ErrorMessageAttribute] = new AttributeValue { S = f.ErrorMessage }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = syncType }
            },
            UpdateExpression = "SET #companies = list_append(if_not_exists(#companies, :empty), :newCompanies), #failedCompanies = list_append(if_not_exists(#failedCompanies, :empty), :newFailed), #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#companies"] = CompaniesAttribute,
                ["#failedCompanies"] = FailedCompaniesAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":newCompanies"] = new AttributeValue { L = companyItems },
                [":newFailed"] = new AttributeValue { L = failedItems },
                [":empty"] = new AttributeValue { L = [] },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Appended incremental sync state for syncType={SyncType}. companies={Companies}, failedCompanies={FailedCompanies}, lastUpdatedAt={LastUpdatedAt}.",
            syncType, incrementalState.Companies.Count, incrementalState.FailedCompanies.Count, lastUpdatedAt);
    }

    public async Task RemoveFailedCompaniesAsync(
        string syncType,
        IReadOnlyList<string> failedCompanyIds,
        CancellationToken cancellationToken = default)
    {
        if (failedCompanyIds.Count == 0)
        {
            logger.LogDebug("No failed company IDs provided for removal.");
            return;
        }

        logger.LogDebug("Removing {Count} failed company entries from DynamoDB for syncType={SyncType}.", failedCompanyIds.Count, syncType);

        // First, get the current state to filter out the removed IDs
        var currentState = await GetAsync(syncType, cancellationToken);
        if (currentState?.FailedCompanies.Count == 0)
        {
            logger.LogDebug("No failed companies in sync state to remove.");
            return;
        }

        // Create a HashSet of IDs to remove for O(1) lookup
        var idsToRemove = new HashSet<string>(failedCompanyIds);

        // Filter out the removed companies
        var remainingFailedCompanies = currentState!.FailedCompanies
            .Where(f => !idsToRemove.Contains(f.Id))
            .ToList();

        // Update the state with remaining failed companies
        var updateExpression = remainingFailedCompanies.Count > 0
            ? "SET #failedCompanies = :failedCompanies"
            : "REMOVE #failedCompanies";

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = syncType }
            },
            UpdateExpression = updateExpression,
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#failedCompanies"] = FailedCompaniesAttribute
            }
        };

        if (remainingFailedCompanies.Count > 0)
        {
            var failedItems = remainingFailedCompanies.Select(f => new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    [IdAttribute]           = new AttributeValue { S = f.Id },
                    [CompanyNameAttribute]  = new AttributeValue { S = f.CompanyName },
                    [ErrorMessageAttribute] = new AttributeValue { S = f.ErrorMessage }
                }
            }).ToList();

            updateRequest.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":failedCompanies"] = new AttributeValue { L = failedItems }
            };
        }

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Removed {Count} failed company entries from DynamoDB for syncType={SyncType}. Remaining: {Remaining}.",
            failedCompanyIds.Count, syncType, remainingFailedCompanies.Count);
    }
}
