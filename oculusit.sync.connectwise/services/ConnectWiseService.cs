using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

/// <summary>
/// Facade that satisfies <see cref="IConnectWiseService"/> by delegating to
/// <see cref="ConnectWiseCompanyService"/> and <see cref="ConnectWiseProjectService"/>.
/// </summary>
public sealed class ConnectWiseService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseService
{
    private readonly ConnectWiseCompanyService _companies = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseCompanyService>.Instance);

    private readonly ConnectWiseMemberService _members = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseMemberService>.Instance);

    private readonly ConnectWiseProjectService _projects = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseProjectService>.Instance);

    private readonly ConnectWiseTimeEntryService _timeEntries = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseTimeEntryService>.Instance);

    private readonly ConnectWiseTimesheetService _timesheets = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseTimesheetService>.Instance);

    public Task<IReadOnlyList<ConnectWiseCompany>> GetAllCompaniesAsync(CancellationToken cancellationToken = default)
        => _companies.GetAllCompaniesAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesSinceAsync(DateTime since, CancellationToken cancellationToken = default)
        => _companies.GetCompaniesSinceAsync(since, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesByIdsAsync(IReadOnlyList<int> companyIds, CancellationToken cancellationToken = default)
        => _companies.GetCompaniesByIdsAsync(companyIds, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseMember>> GetAllMembersAsync(CancellationToken cancellationToken = default)
        => _members.GetAllMembersAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProject>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
        => _projects.GetAllProjectsAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProject>> GetProjectsSinceAsync(DateTime since, CancellationToken cancellationToken = default)
        => _projects.GetProjectsSinceAsync(since, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProject>> GetProjectsByIdsAsync(IReadOnlyList<int> projectIds, CancellationToken cancellationToken = default)
        => _projects.GetProjectsByIdsAsync(projectIds, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProjectStatus>> GetAllProjectStatusesAsync(CancellationToken cancellationToken = default)
        => _projects.GetAllProjectStatusesAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForDayAsync(
        DateOnly date,
        IReadOnlyList<int>? memberIds = null,
        CancellationToken cancellationToken = default)
        => _timeEntries.GetTimeEntriesForDayAsync(date, memberIds, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForCompanyAndDayAsync(
        int companyId,
        DateOnly date,
        IReadOnlyList<int>? memberIds = null,
        CancellationToken cancellationToken = default)
        => _timeEntries.GetTimeEntriesForCompanyAndDayAsync(companyId, date, memberIds, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesByTimesheetIdAsync(
        int timesheetId,
        string timeOffWorkType,
        CancellationToken cancellationToken = default)
        => _timeEntries.GetTimeEntriesByTimesheetIdAsync(timesheetId, timeOffWorkType, cancellationToken);

    public Task<ConnectWiseTimesheet?> GetTimesheetByIdAsync(
        int timesheetId,
        CancellationToken cancellationToken = default)
        => _timesheets.GetTimesheetByIdAsync(timesheetId, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimesheet>> GetTimesheetsByEmployeeAndDateRangeAsync(
        int employeeId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
        => _timesheets.GetTimesheetsByEmployeeAndDateRangeAsync(employeeId, startDate, endDate, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimesheet>> GetTimesheetsByWeekAsync(
        int week,
        int year,
        CancellationToken cancellationToken = default)
        => _timesheets.GetTimesheetsByWeekAsync(week, year, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimesheetAuditTrail>> GetTimesheetAuditTrailAsync(
        int timesheetId,
        CancellationToken cancellationToken = default)
        => _timesheets.GetTimesheetAuditTrailAsync(timesheetId, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseTimesheet>> GetTimesheetsSinceAsync(
        DateTime lastUpdatedSince,
        CancellationToken cancellationToken = default)
        => _timesheets.GetTimesheetsSinceAsync(lastUpdatedSince, cancellationToken);
}
