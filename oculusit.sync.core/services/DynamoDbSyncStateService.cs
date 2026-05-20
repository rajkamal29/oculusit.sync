using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.core.configurations;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using System.Globalization;

namespace oculusit.sync.core.services;

public sealed class DynamoDbSyncStateService(
    IAmazonDynamoDB dynamoDb,
    IOptions<DynamoDbConfiguration> options,
    ILogger<DynamoDbSyncStateService> logger) : ISyncStateService
{
    private const string KeyAttribute              = "syncType";
    private const string LastUpdatedAtAttribute    = "lastUpdatedAt";
    private const string CompaniesAttribute        = "companies";
    private const string ProjectsAttribute         = "projects";
    private const string BillingRolesAttribute     = "billingRoles";
    private const string FailedProjectsAttribute   = "failedProjects";
    private const string ProjectStatusesAttribute       = "projectStatuses";
    private const string FailedProjectStatusesAttribute = "failedProjectStatuses";
    private const string IdAttribute               = "id";
    private const string ClientIdAttribute         = "clientId";
    private const string KekaClientIdAttribute     = "kekaClientId";
    private const string KekaProjectIdAttribute    = "kekaProjectId";
    private const string FailedTaskKeysAttribute   = "failedTaskKeys";
    private const string NameAttribute             = "name";
    private const string ErrorMessageAttribute     = "errorMessage";
    private const string ValueAttribute            = "value";
    private const string MappedValueAttribute      = "mappedValue";
    private const string SummaryAttribute           = "summary";
    private const string TotalAttribute             = "total";
    private const string SucceededAttribute         = "succeeded";
    private const string FailedAttribute            = "failed";

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
            && DateTime.TryParseExact(tsAttr.S, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            lastUpdatedAt = parsed;
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

        var projects = new List<SyncedProjectEntry>();
        if (response.Item.TryGetValue(ProjectsAttribute, out var projectsAttr) && projectsAttr.L is { Count: > 0 })
        {
            foreach (var entry in projectsAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(KekaClientIdAttribute, out var kekaClientIdAttr);
                entry.M.TryGetValue(KekaProjectIdAttribute, out var kekaProjectIdAttr);

                var failedTaskKeys = new List<string>();
                if (entry.M.TryGetValue(FailedTaskKeysAttribute, out var failedTaskKeysAttr) && failedTaskKeysAttr.SS is { Count: > 0 })
                    failedTaskKeys.AddRange(failedTaskKeysAttr.SS);

                projects.Add(new SyncedProjectEntry
                {
                    Id             = idAttr?.S ?? string.Empty,
                    KekaClientId   = kekaClientIdAttr?.S,
                    KekaProjectId  = kekaProjectIdAttr?.S,
                    FailedTaskKeys = failedTaskKeys
                });
            }
        }

        var billingRoles = new List<BillingRoleEntry>();
        if (response.Item.TryGetValue(BillingRolesAttribute, out var billingRolesAttr) && billingRolesAttr.L is { Count: > 0 })
        {
            foreach (var entry in billingRolesAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(NameAttribute, out var nameAttr);
                billingRoles.Add(new BillingRoleEntry
                {
                    Id = idAttr?.S ?? string.Empty,
                    Name = nameAttr?.S ?? string.Empty
                });
            }
        }

        return new SyncState
        {
            SyncType              = syncType,
            LastUpdatedAt         = lastUpdatedAt,
            Companies             = companies,
            Projects              = projects,
            BillingRoles          = billingRoles,
            ProjectStatuses       = ReadProjectStatuses(response.Item),
            FailedProjectStatuses = ReadFailedMetadata(response.Item)
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

        if (state.Projects.Count > 0)
        {
            item[ProjectsAttribute] = new AttributeValue
            {
                L = state.Projects.Select(p =>
                {
                    var m = new Dictionary<string, AttributeValue>
                    {
                        [IdAttribute]            = new AttributeValue { S = p.Id },
                        [KekaClientIdAttribute]  = new AttributeValue { S = p.KekaClientId ?? string.Empty },
                        [KekaProjectIdAttribute] = new AttributeValue { S = p.KekaProjectId ?? string.Empty }
                    };

                    if (p.FailedTaskKeys.Count > 0)
                        m[FailedTaskKeysAttribute] = new AttributeValue { SS = [.. p.FailedTaskKeys] };

                    return new AttributeValue { M = m };
                }).ToList()
            };
        }

        if (state.ProjectStatuses.Count > 0)
        {
            item[ProjectStatusesAttribute] = new AttributeValue
            {
                L = state.ProjectStatuses.Select(s => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [IdAttribute]          = new AttributeValue { S = s.Id },
                        [ValueAttribute]       = new AttributeValue { S = s.Value },
                        [MappedValueAttribute] = new AttributeValue { S = s.MappedValue }
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

        logger.LogInformation("Saved sync state for syncType={SyncType}, lastUpdatedAt={LastUpdatedAt}, companies={Count}.",
            state.SyncType, state.LastUpdatedAt, state.Companies.Count);
    }

    public async Task AppendCompaniesAsync(
        string syncType,
        IReadOnlyList<SyncedCompanyEntry> newEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Appending {Count} company entries to DynamoDB for syncType={SyncType}.", newEntries.Count, syncType);

        var newItems = newEntries.Select(c => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]       = new AttributeValue { S = c.Id },
                [ClientIdAttribute] = new AttributeValue { S = c.ClientId }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = syncType }
            },
            // list_append appends newItems to the existing companies list.
            // if_not_exists handles the edge case where companies attribute doesn't exist yet.
            UpdateExpression = "SET #companies = list_append(if_not_exists(#companies, :empty), :newItems), #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#companies"]     = CompaniesAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":newItems"]      = new AttributeValue { L = newItems },
                [":empty"]         = new AttributeValue { L = [] },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Appended {Count} company entries and updated lastUpdatedAt={LastUpdatedAt} for syncType={SyncType}.",
            newEntries.Count, lastUpdatedAt, syncType);
    }

    public async Task AppendProjectsAsync(
        string syncType,
        IReadOnlyList<SyncedProjectEntry> newEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Appending {Count} project entries to DynamoDB for syncType={SyncType}.", newEntries.Count, syncType);

        var newItems = newEntries.Select(p =>
        {
            var m = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]            = new AttributeValue { S = p.Id },
                [KekaClientIdAttribute]  = new AttributeValue { S = p.KekaClientId ?? string.Empty },
                [KekaProjectIdAttribute] = new AttributeValue { S = p.KekaProjectId ?? string.Empty }
            };

            if (p.FailedTaskKeys.Count > 0)
                m[FailedTaskKeysAttribute] = new AttributeValue { SS = [.. p.FailedTaskKeys] };

            return new AttributeValue { M = m };
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = syncType }
            },
            UpdateExpression = "SET #projects = list_append(if_not_exists(#projects, :empty), :newItems), #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#projects"]      = ProjectsAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":newItems"]      = new AttributeValue { L = newItems },
                [":empty"]         = new AttributeValue { L = [] },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Appended {Count} project entries and updated lastUpdatedAt={LastUpdatedAt} for syncType={SyncType}.",
            newEntries.Count, lastUpdatedAt, syncType);
    }

    public async Task SaveFailedProjectsAsync(
        IReadOnlyList<FailedProjectEntry> failedEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} failed project entries to DynamoDB (syncType={SyncType}).", failedEntries.Count, SyncTypes.Failures);

        var failedItems = failedEntries.Select(p => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]           = new AttributeValue { S = p.Id },
                [NameAttribute]         = new AttributeValue { S = p.Name },
                [ErrorMessageAttribute] = new AttributeValue { S = p.ErrorMessage }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Failures }
            },
            // Full replace every run so the Failures record always reflects the latest state.
            UpdateExpression = "SET #projects = :projects, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#projects"]      = ProjectsAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":projects"]      = new AttributeValue { L = failedItems },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} failed project entries to Failures record, lastUpdatedAt={LastUpdatedAt}.",
            failedEntries.Count, lastUpdatedAt);
    }

    public async Task SaveFailedCompaniesAsync(
        IReadOnlyList<FailedCompanyEntry> failedEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} failed company entries to DynamoDB (syncType={SyncType}).", failedEntries.Count, SyncTypes.Failures);

        var failedItems = failedEntries.Select(c => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]           = new AttributeValue { S = c.Id },
                [NameAttribute]         = new AttributeValue { S = c.Name },
                [ErrorMessageAttribute] = new AttributeValue { S = c.ErrorMessage }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Failures }
            },
            // Full replace every run so the Failures record always reflects the latest state.
            UpdateExpression = "SET #companies = :companies, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#companies"]     = CompaniesAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":companies"]     = new AttributeValue { L = failedItems },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} failed company entries to Failures record, lastUpdatedAt={LastUpdatedAt}.",
            failedEntries.Count, lastUpdatedAt);
    }

    public async Task SaveCompanySummaryAsync(
        CompanySyncSummary summary,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving company sync summary to DynamoDB (syncType={SyncType}).", SyncTypes.Company);

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Company }
            },
            UpdateExpression = "SET #summary = :summary, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#summary"]       = SummaryAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":summary"] = new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [TotalAttribute]     = new AttributeValue { N = summary.Total.ToString() },
                        [SucceededAttribute] = new AttributeValue { N = summary.Succeeded.ToString() },
                        [FailedAttribute]    = new AttributeValue { N = summary.Failed.ToString() }
                    }
                },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation(
            "Saved company sync summary: Total={Total}, Succeeded={Succeeded}, Failed={Failed}, lastUpdatedAt={LastUpdatedAt}.",
            summary.Total, summary.Succeeded, summary.Failed, lastUpdatedAt);
    }

    public async Task SaveProjectSummaryAsync(
        ProjectSyncSummary summary,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving project sync summary to DynamoDB (syncType={SyncType}).", SyncTypes.Project);

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Project }
            },
            UpdateExpression = "SET #summary = :summary, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#summary"]       = SummaryAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":summary"] = new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [TotalAttribute]     = new AttributeValue { N = summary.Total.ToString() },
                        [SucceededAttribute] = new AttributeValue { N = summary.Succeeded.ToString() },
                        [FailedAttribute]    = new AttributeValue { N = summary.Failed.ToString() }
                    }
                },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation(
            "Saved project sync summary: Total={Total}, Succeeded={Succeeded}, Failed={Failed}, lastUpdatedAt={LastUpdatedAt}.",
            summary.Total, summary.Succeeded, summary.Failed, lastUpdatedAt);
    }

    public async Task SaveRetryCompaniesAsync(
        IReadOnlyList<RetryCompanyEntry> retryEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} retry company entries to DynamoDB (syncType={SyncType}).", retryEntries.Count, SyncTypes.Retry);

        var items = retryEntries.Select(c => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]           = new AttributeValue { S = c.Id },
                [NameAttribute]         = new AttributeValue { S = c.Name },
                [ErrorMessageAttribute] = new AttributeValue { S = c.ErrorMessage }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Retry }
            },
            UpdateExpression = "SET #companies = :companies, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#companies"]     = CompaniesAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":companies"]     = new AttributeValue { L = items },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} retry company entries to Retry record, lastUpdatedAt={LastUpdatedAt}.",
            retryEntries.Count, lastUpdatedAt);
    }

    public async Task SaveRetryProjectsAsync(
        IReadOnlyList<RetryProjectEntry> retryEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} retry project entries to DynamoDB (syncType={SyncType}).", retryEntries.Count, SyncTypes.Retry);

        var items = retryEntries.Select(p => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]           = new AttributeValue { S = p.Id },
                [NameAttribute]         = new AttributeValue { S = p.Name },
                [ErrorMessageAttribute] = new AttributeValue { S = p.ErrorMessage }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Retry }
            },
            UpdateExpression = "SET #projects = :projects, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#projects"]      = ProjectsAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":projects"]      = new AttributeValue { L = items },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} retry project entries to Retry record, lastUpdatedAt={LastUpdatedAt}.",
            retryEntries.Count, lastUpdatedAt);
    }

    public async Task SaveMetadataAsync(
        IReadOnlyList<ProjectStatusEntry> entries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} project status metadata entries to DynamoDB.", entries.Count);

        var statusItems = entries.Select(s => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                [IdAttribute]          = new AttributeValue { S = s.Id },
                [ValueAttribute]       = new AttributeValue { S = s.Value },
                [MappedValueAttribute] = new AttributeValue { S = s.MappedValue }
            }
        }).ToList();

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Metadata }
            },
            // Full replace every run — merging of MappedValue is handled in orchestration before this call.
            UpdateExpression = "SET #statuses = :statuses, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#statuses"]      = ProjectStatusesAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":statuses"]      = new AttributeValue { L = statusItems },
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} project status metadata entries.", entries.Count);
    }

    public async Task SaveFailedMetadataAsync(
        FailedMetadataEntry? failure,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving failed metadata entry (null = reset) to DynamoDB.");

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.Metadata }
            },
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#failedStatuses"] = FailedProjectStatusesAttribute,
                ["#lastUpdatedAt"]  = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":lastUpdatedAt"] = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        if (failure is null)
        {
            // Reset — remove the field so a clean run leaves no stale error.
            updateRequest.UpdateExpression = "REMOVE #failedStatuses SET #lastUpdatedAt = :lastUpdatedAt";
        }
        else
        {
            updateRequest.UpdateExpression = "SET #failedStatuses = :failure, #lastUpdatedAt = :lastUpdatedAt";
            updateRequest.ExpressionAttributeValues[":failure"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    [ErrorMessageAttribute] = new AttributeValue { S = failure.ErrorMessage }
                }
            };
        }

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation(failure is null
            ? "Cleared failedProjectStatuses on Metadata record."
            : "Saved failedProjectStatuses with error: {Error}.", failure?.ErrorMessage);
    }

    private static IReadOnlyList<ProjectStatusEntry> ReadProjectStatuses(
        Dictionary<string, AttributeValue> item)
    {
        var statuses = new List<ProjectStatusEntry>();

        if (!item.TryGetValue(ProjectStatusesAttribute, out var attr) || attr.L is not { Count: > 0 })
            return statuses;

        foreach (var entry in attr.L)
        {
            if (entry.M is null) continue;
            entry.M.TryGetValue(IdAttribute, out var idAttr);
            entry.M.TryGetValue(ValueAttribute, out var valueAttr);
            entry.M.TryGetValue(MappedValueAttribute, out var mappedAttr);
            statuses.Add(new ProjectStatusEntry
            {
                Id           = idAttr?.S ?? string.Empty,
                Value        = valueAttr?.S ?? string.Empty,
                MappedValue  = mappedAttr?.S ?? string.Empty
            });
        }

        return statuses;
    }

    private static Task<IReadOnlyList<FailedProjectEntry>> ReadFailedProjectsAsync(
        Dictionary<string, AttributeValue> item)
    {
        var failed = new List<FailedProjectEntry>();

        if (item.TryGetValue(FailedProjectsAttribute, out var attr) && attr.L is { Count: > 0 })
        {
            foreach (var entry in attr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(NameAttribute, out var nameAttr);
                entry.M.TryGetValue(ErrorMessageAttribute, out var errAttr);
                failed.Add(new FailedProjectEntry
                {
                    Id           = idAttr?.S ?? string.Empty,
                    Name         = nameAttr?.S ?? string.Empty,
                    ErrorMessage = errAttr?.S ?? string.Empty
                });
            }
        }

        return Task.FromResult<IReadOnlyList<FailedProjectEntry>>(failed);
    }

    private static FailedMetadataEntry? ReadFailedMetadata(Dictionary<string, AttributeValue> item)
    {
        if (!item.TryGetValue(FailedProjectStatusesAttribute, out var attr) || attr.M is null)
            return null;

        attr.M.TryGetValue(ErrorMessageAttribute, out var errAttr);
        return new FailedMetadataEntry { ErrorMessage = errAttr?.S ?? string.Empty };
    }
}
