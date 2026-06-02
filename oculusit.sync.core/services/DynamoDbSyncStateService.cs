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
    private const string InitialCompaniesAttribute = "initialCompanies";
    private const string ProjectsAttribute         = "projects";
    private const string InitialProjectsAttribute  = "initialProjects";
    private const string FailedProjectsAttribute   = "failedProjects";
    private const string FailedCompaniesAttribute  = "failedCompanies";
    private const string ProjectManagerAttribute   = "projectManager";
    private const string EmailAttribute            = "email";
    private const string ProjectStatusesAttribute       = "projectStatuses";
    private const string FailedProjectStatusesAttribute = "failedProjectStatuses";
    private const string IdAttribute               = "id";
    private const string CompanyIdAttribute        = "companyId";
    private const string CompanyNameAttribute      = "companyName";
    private const string ClientNameAttribute       = "clientName";
    private const string CompanyCodeAttribute      = "companyCode";
    private const string LegacyNameAttribute       = "name";
    private const string ProjectIdAttribute        = "projectId";
    private const string ProjectNameAttribute      = "projectName";
    private const string ClientIdAttribute         = "clientId";
    private const string KekaClientIdAttribute     = "kekaClientId";
    private const string KekaProjectIdAttribute    = "kekaProjectId";
    private const string KekaProjectCodeAttribute  = "kekaProjectCode";
    private const string KekaProjectNameAttribute  = "kekaProjectName";
    private const string FailedTaskKeysAttribute   = "failedTaskKeys";
    private const string NameAttribute             = "name";
    private const string ErrorMessageAttribute     = "errorMessage";
    private const string ValueAttribute            = "value";
    private const string MappedValueAttribute      = "mappedValue";
    private const string SummaryAttribute           = "summary";
    private const string TotalAttribute             = "total";
    private const string SucceededAttribute         = "succeeded";
    private const string FailedAttribute            = "failed";
    private const string DedupeKeyAttribute         = "dedupeKey";

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

        DefaultProjectEntry? defaultProject = null;
        if (response.Item.TryGetValue(ProjectManagerAttribute, out var projectManagerAttr)
            && projectManagerAttr.M is { Count: > 0 })
        {
            var email = projectManagerAttr.M.TryGetValue(EmailAttribute, out var emailAttr) ? emailAttr.S ?? string.Empty : string.Empty;
            var name = projectManagerAttr.M.TryGetValue(NameAttribute, out var managerNameAttr) ? managerNameAttr.S ?? string.Empty : string.Empty;

            if (!string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(name))
            {
                defaultProject = new DefaultProjectEntry
                {
                    ProjectManager = new DefaultProjectManagerEntry
                    {
                        Email = email,
                        Name = name
                    }
                };
            }
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

        var failedProjects = new List<FailedProjectEntry>();
        if (response.Item.TryGetValue(FailedProjectsAttribute, out var failedProjectsAttr) && failedProjectsAttr.L is { Count: > 0 })
        {
            foreach (var entry in failedProjectsAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(NameAttribute, out var nameAttr);
                entry.M.TryGetValue(ErrorMessageAttribute, out var errorAttr);

                if (string.IsNullOrWhiteSpace(idAttr?.S))
                    continue;

                failedProjects.Add(new FailedProjectEntry
                {
                    Id = idAttr.S,
                    Name = nameAttr?.S ?? string.Empty,
                    ErrorMessage = errorAttr?.S ?? string.Empty
                });
            }
        }

        var failedCompanies = new List<FailedCompanyEntry>();
        if (response.Item.TryGetValue(FailedCompaniesAttribute, out var failedCompaniesAttr) && failedCompaniesAttr.L is { Count: > 0 })
        {
            foreach (var entry in failedCompaniesAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(IdAttribute, out var idAttr);
                entry.M.TryGetValue(NameAttribute, out var nameAttr);
                entry.M.TryGetValue(ErrorMessageAttribute, out var errorAttr);

                if (string.IsNullOrWhiteSpace(idAttr?.S))
                    continue;

                failedCompanies.Add(new FailedCompanyEntry
                {
                    Id = idAttr.S,
                    Name = nameAttr?.S ?? string.Empty,
                    ErrorMessage = errorAttr?.S ?? string.Empty
                });
            }
        }

        var initialCompanies = new List<InitialCompanyEntry>();
        if (response.Item.TryGetValue(InitialCompaniesAttribute, out var initialCompaniesAttr) && initialCompaniesAttr.L is { Count: > 0 })
        {
            foreach (var entry in initialCompaniesAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(CompanyIdAttribute, out var companyIdAttr);
                entry.M.TryGetValue(CompanyNameAttribute, out var companyNameAttr);
                entry.M.TryGetValue(ClientIdAttribute, out var clientIdAttr);
                entry.M.TryGetValue(CompanyCodeAttribute, out var clientCodeAttr);
                entry.M.TryGetValue(ClientNameAttribute, out var clientNameAttr);
                if (string.IsNullOrWhiteSpace(clientNameAttr?.S) && entry.M.TryGetValue(LegacyNameAttribute, out var legacyNameAttr))
                    clientNameAttr = legacyNameAttr;

                initialCompanies.Add(new InitialCompanyEntry
                {
                    CompanyId   = companyIdAttr?.S ?? string.Empty,
                    CompanyName = companyNameAttr?.S ?? string.Empty,
                    ClientId    = clientIdAttr?.S ?? string.Empty,
                    ClientCode  = clientCodeAttr?.S ?? string.Empty,
                    ClientName  = clientNameAttr?.S ?? string.Empty
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

        var initialProjects = new List<InitialProjectEntry>();
        if (response.Item.TryGetValue(InitialProjectsAttribute, out var initialProjectsAttr) && initialProjectsAttr.L is { Count: > 0 })
        {
            foreach (var entry in initialProjectsAttr.L)
            {
                if (entry.M is null) continue;
                entry.M.TryGetValue(ProjectIdAttribute, out var projectIdAttr);
                entry.M.TryGetValue(ProjectNameAttribute, out var projectNameAttr);
                entry.M.TryGetValue(KekaProjectIdAttribute, out var kekaProjectIdAttr);
                entry.M.TryGetValue(KekaProjectCodeAttribute, out var kekaProjectCodeAttr);
                entry.M.TryGetValue(KekaProjectNameAttribute, out var kekaProjectNameAttr);
                if (string.IsNullOrWhiteSpace(kekaProjectNameAttr?.S) && entry.M.TryGetValue(NameAttribute, out var legacyNameAttr))
                    kekaProjectNameAttr = legacyNameAttr;

                initialProjects.Add(new InitialProjectEntry
                {
                    ProjectId       = projectIdAttr?.S ?? string.Empty,
                    ProjectName     = projectNameAttr?.S ?? string.Empty,
                    KekaProjectId   = kekaProjectIdAttr?.S ?? string.Empty,
                    KekaProjectCode = kekaProjectCodeAttr?.S ?? string.Empty,
                    KekaProjectName = kekaProjectNameAttr?.S ?? string.Empty
                });
            }
        }

        return new SyncState
        {
            SyncType              = syncType,
            LastUpdatedAt         = lastUpdatedAt,
            Companies             = companies,
            InitialCompanies      = initialCompanies,
            Projects              = projects,
            DefaultProject        = defaultProject,
            InitialProjects       = initialProjects,
            FailedProjects        = failedProjects,
            FailedCompanies       = failedCompanies,
            ProjectStatuses       = ReadProjectStatuses(response.Item)
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

        if (state.InitialCompanies.Count > 0)
        {
            item[InitialCompaniesAttribute] = new AttributeValue
            {
                L = state.InitialCompanies.Select(c => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [CompanyIdAttribute]   = new AttributeValue { S = c.CompanyId },
                        [CompanyNameAttribute] = new AttributeValue { S = c.CompanyName },
                        [ClientIdAttribute]    = new AttributeValue { S = c.ClientId },
                        [CompanyCodeAttribute] = new AttributeValue { S = c.ClientCode },
                        [ClientNameAttribute]  = new AttributeValue { S = c.ClientName }
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

        if (state.InitialProjects.Count > 0)
        {
            item[InitialProjectsAttribute] = new AttributeValue
            {
                L = state.InitialProjects.Select(p => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        [ProjectIdAttribute]    = new AttributeValue { S = p.ProjectId },
                        [ProjectNameAttribute]  = new AttributeValue { S = p.ProjectName },
                        [KekaProjectIdAttribute]= new AttributeValue { S = p.KekaProjectId },
                        [KekaProjectCodeAttribute]  = new AttributeValue { S = p.KekaProjectCode },
                        [KekaProjectNameAttribute]   = new AttributeValue { S = p.KekaProjectName }
                    }
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
        logger.LogDebug("Saving {Count} failed project entries to DynamoDB (syncType={SyncType}).", failedEntries.Count, SyncTypes.FailedProjects);

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
                [KeyAttribute] = new AttributeValue { S = SyncTypes.FailedProjects }
            },
            // Full replace every run so the FailedProject record always reflects the latest state.
            UpdateExpression = "SET #failedProjects = :failedProjects, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#failedProjects"] = FailedProjectsAttribute,
                ["#lastUpdatedAt"]  = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":failedProjects"] = new AttributeValue { L = failedItems },
                [":lastUpdatedAt"]  = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} failed project entries to FailedProject record, lastUpdatedAt={LastUpdatedAt}.",
            failedEntries.Count, lastUpdatedAt);
    }

    public async Task SaveFailedCompaniesAsync(
        IReadOnlyList<FailedCompanyEntry> failedEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} failed company entries to DynamoDB (syncType={SyncType}).", failedEntries.Count, SyncTypes.FailedCompanies);

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
                [KeyAttribute] = new AttributeValue { S = SyncTypes.FailedCompanies }
            },
            // Full replace every run so the FailedCompany record always reflects the latest state.
            UpdateExpression = "SET #failedCompanies = :failedCompanies, #lastUpdatedAt = :lastUpdatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#failedCompanies"] = FailedCompaniesAttribute,
                ["#lastUpdatedAt"]   = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":failedCompanies"] = new AttributeValue { L = failedItems },
                [":lastUpdatedAt"]   = new AttributeValue { S = lastUpdatedAt.ToString("o") }
            }
        };

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);

        logger.LogInformation("Saved {Count} failed company entries to FailedCompany record, lastUpdatedAt={LastUpdatedAt}.",
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
        logger.LogDebug("Saving {Count} retry company entries to DynamoDB (syncType={SyncType}).", retryEntries.Count, SyncTypes.RetryCompanies);

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
                [KeyAttribute] = new AttributeValue { S = SyncTypes.RetryCompanies }
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

        logger.LogInformation("Saved {Count} retry company entries to RetryCompanies record, lastUpdatedAt={LastUpdatedAt}.",
            retryEntries.Count, lastUpdatedAt);
    }

    public async Task<IReadOnlyList<RetryCompanyEntry>> GetRetryCompaniesAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Reading retry company entries from DynamoDB (syncType={SyncType}).", SyncTypes.RetryCompanies);

        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.RetryCompanies }
            }
        };

        var response = await dynamoDb.GetItemAsync(request, cancellationToken);
        if (!response.IsItemSet || !response.Item.TryGetValue(CompaniesAttribute, out var companiesAttr) || companiesAttr.L is not { Count: > 0 })
            return [];

        var entries = new List<RetryCompanyEntry>();
        foreach (var item in companiesAttr.L)
        {
            if (item.M is null)
                continue;

            item.M.TryGetValue(IdAttribute, out var idAttr);
            item.M.TryGetValue(NameAttribute, out var nameAttr);
            item.M.TryGetValue(ErrorMessageAttribute, out var errorAttr);

            if (string.IsNullOrWhiteSpace(idAttr?.S))
                continue;

            entries.Add(new RetryCompanyEntry
            {
                Id = idAttr!.S!,
                Name = nameAttr?.S ?? string.Empty,
                ErrorMessage = errorAttr?.S ?? string.Empty
            });
        }

        logger.LogInformation("Loaded {Count} retry company entries.", entries.Count);
        return entries;
    }

    public async Task SaveRetryProjectsAsync(
        IReadOnlyList<RetryProjectEntry> retryEntries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} retry project entries to DynamoDB (syncType={SyncType}).", retryEntries.Count, SyncTypes.RetryProjects);

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
                [KeyAttribute] = new AttributeValue { S = SyncTypes.RetryProjects }
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

        logger.LogInformation("Saved {Count} retry project entries to RetryProjects record, lastUpdatedAt={LastUpdatedAt}.",
            retryEntries.Count, lastUpdatedAt);
    }

    public async Task SaveProjectStatusAsync(
        IReadOnlyList<ProjectStatusEntry> entries,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving {Count} project status entries to DynamoDB.", entries.Count);

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
                [KeyAttribute] = new AttributeValue { S = SyncTypes.ProjectStatus }
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

        logger.LogInformation("Saved {Count} project status entries.", entries.Count);
    }

    public async Task SaveFailedProjectStatusAsync(
        FailedProjectStatusEntry? failure,
        DateTime lastUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Saving failed project status entry (null = reset) to DynamoDB.");

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = SyncTypes.ProjectStatus }
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
            ? "Cleared failed project status entry on ProjectStatus record."
            : "Saved failed project status entry with error: {Error}.", failure?.ErrorMessage);
    }

    public async Task<IReadOnlyList<TimeEntryEmployeeDedupeState>> GetTimeEntryEmployeeDedupeStatesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<TimeEntryEmployeeDedupeState>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

        do
        {
            var request = new ScanRequest
            {
                TableName = _tableName,
                ProjectionExpression = "#syncType, #email, #dedupeKey, #lastUpdatedAt",
                FilterExpression = "begins_with(#syncType, :prefix)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#syncType"] = KeyAttribute,
                    ["#email"] = EmailAttribute,
                    ["#dedupeKey"] = DedupeKeyAttribute,
                    ["#lastUpdatedAt"] = LastUpdatedAtAttribute
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":prefix"] = new AttributeValue { S = $"{SyncTypes.TimeEntries}#" }
                },
                ExclusiveStartKey = lastEvaluatedKey
            };

            var response = await dynamoDb.ScanAsync(request, cancellationToken);
            foreach (var item in response.Items)
            {
                if (!item.TryGetValue(KeyAttribute, out var syncTypeAttr) || string.IsNullOrWhiteSpace(syncTypeAttr.S))
                    continue;

                results.Add(MapTimeEntryEmployeeState(syncTypeAttr.S, item));
            }

            lastEvaluatedKey = response.LastEvaluatedKey;
        }
        while (lastEvaluatedKey is { Count: > 0 });

        logger.LogInformation("Loaded {Count} time-entry employee checkpoint records.", results.Count);
        return results;
    }

    public async Task<IReadOnlyList<TimeEntryEmployeeDedupeState>> GetTimeEntryEmployeeDedupeStatesToSyncAsync(string previousWeekDedupeKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(previousWeekDedupeKey))
            return await GetTimeEntryEmployeeDedupeStatesAsync(cancellationToken);

        var results = new List<TimeEntryEmployeeDedupeState>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

        do
        {
            var request = new ScanRequest
            {
                TableName = _tableName,
                ProjectionExpression = "#syncType, #email, #dedupeKey, #lastUpdatedAt",
                FilterExpression = "begins_with(#syncType, :prefix) AND (attribute_not_exists(#dedupeKey) OR #dedupeKey = :emptyDedupeKey OR #dedupeKey < :previousWeekDedupeKey)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#syncType"] = KeyAttribute,
                    ["#email"] = EmailAttribute,
                    ["#dedupeKey"] = DedupeKeyAttribute,
                    ["#lastUpdatedAt"] = LastUpdatedAtAttribute
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":prefix"] = new AttributeValue { S = $"{SyncTypes.TimeEntries}#" },
                    [":emptyDedupeKey"] = new AttributeValue { S = string.Empty },
                    [":previousWeekDedupeKey"] = new AttributeValue { S = previousWeekDedupeKey }
                },
                ExclusiveStartKey = lastEvaluatedKey
            };

            var response = await dynamoDb.ScanAsync(request, cancellationToken);
            foreach (var item in response.Items)
            {
                if (!item.TryGetValue(KeyAttribute, out var syncTypeAttr) || string.IsNullOrWhiteSpace(syncTypeAttr.S))
                    continue;

                results.Add(MapTimeEntryEmployeeState(syncTypeAttr.S, item));
            }

            lastEvaluatedKey = response.LastEvaluatedKey;
        }
        while (lastEvaluatedKey is { Count: > 0 });

        logger.LogInformation(
            "Loaded {Count} time-entry employee checkpoint records requiring sync for previous week {PreviousWeekDedupeKey}.",
            results.Count,
            previousWeekDedupeKey);

        return results;
    }

    public async Task<TimeEntryEmployeeDedupeState?> GetTimeEntryEmployeeDedupeStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        var normalizedEmployeeId = employeeId.Trim();
        var syncType = $"{SyncTypes.TimeEntries}#{normalizedEmployeeId}";

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
            return null;

        return MapTimeEntryEmployeeState(syncType, response.Item);
    }

    public async Task UpsertTimeEntryEmployeeDedupeStateAsync(TimeEntryEmployeeDedupeState state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state.EmployeeId))
            return;

        var employeeId = state.EmployeeId.Trim();
        var syncType = $"{SyncTypes.TimeEntries}#{employeeId}";

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new AttributeValue { S = syncType }
            },
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#email"] = EmailAttribute,
                ["#dedupeKey"] = DedupeKeyAttribute,
                ["#lastUpdatedAt"] = LastUpdatedAtAttribute
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":email"] = new AttributeValue { S = state.Email ?? string.Empty },
                [":dedupeKey"] = new AttributeValue { S = state.DedupeKey ?? string.Empty }
            }
        };

        if (state.LastUpdatedAt.HasValue)
        {
            updateRequest.UpdateExpression = "SET #email = :email, #dedupeKey = :dedupeKey, #lastUpdatedAt = :lastUpdatedAt";
            updateRequest.ExpressionAttributeValues[":lastUpdatedAt"] = new AttributeValue { S = state.LastUpdatedAt.Value.ToString("o") };
        }
        else
        {
            updateRequest.UpdateExpression = "SET #email = :email, #dedupeKey = :dedupeKey REMOVE #lastUpdatedAt";
        }

        await dynamoDb.UpdateItemAsync(updateRequest, cancellationToken);
    }

    private static TimeEntryEmployeeDedupeState MapTimeEntryEmployeeState(
        string syncType,
        IReadOnlyDictionary<string, AttributeValue> item)
    {
        DateTime? lastUpdatedAt = null;
        if (item.TryGetValue(LastUpdatedAtAttribute, out var tsAttr)
            && DateTime.TryParseExact(tsAttr.S, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            lastUpdatedAt = parsed;
        }

        var employeeId = syncType.StartsWith($"{SyncTypes.TimeEntries}#", StringComparison.Ordinal)
            ? syncType[(SyncTypes.TimeEntries.Length + 1)..]
            : syncType;

        return new TimeEntryEmployeeDedupeState
        {
            EmployeeId = employeeId,
            Email = item.TryGetValue(EmailAttribute, out var emailAttr) ? emailAttr.S ?? string.Empty : string.Empty,
            DedupeKey = item.TryGetValue(DedupeKeyAttribute, out var dedupeKeyAttr) ? dedupeKeyAttr.S ?? string.Empty : string.Empty,
            LastUpdatedAt = lastUpdatedAt
        };
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
}
