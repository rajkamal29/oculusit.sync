using Microsoft.Extensions.Logging;
using oculusit.sync.connectwise.modules;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using oculusit.sync.keka.services;

namespace oculusit.sync.orchestration.services;

public sealed class TimeEntryOrchestrationService(
    ISyncStateService syncStateService,
    IKekaProjectService kekaProjectService,
    IKekaEmployeeService kekaEmployeeService,
    IKekaTimesheetEntryService kekaTimesheetEntryService,
    ILogger<TimeEntryOrchestrationService> logger) : ITimeEntryOrchestrationService
{
    private const string DefaultProjectSuffix = "-CWDP";
    private const string BillableChargeCodeTask = "CW: Billable Charge Code";
    private const string NonBillableChargeCodeTask = "CW: Non-Billable Charge Code";
    private const string BillableServiceTicketTask = "CW: Billable Service Ticket";
    private const string NonBillableServiceTicketTask = "CW: Non-Billable Service Ticket";
    private const string BillableProjectTicketTask = "CW: Billable Project Ticket";
    private const string NonBillableProjectTicketTask = "CW: Non-Billable Project Ticket";

    public async Task<bool> LogTimeEntryAsync(
        ConnectWiseTimeEntry entry,
        string employeeEmail,
        CancellationToken cancellationToken = default)
    {
        if (entry is null)
            return false;

        if (string.IsNullOrWhiteSpace(employeeEmail))
        {
            logger.LogWarning("Skipping ConnectWise time entry {TimeEntryId} because employee email is empty.", entry.Id);
            return false;
        }

        if (!TryResolveMinutes(entry, out var minutes) || minutes <= 0)
        {
            logger.LogWarning("Skipping ConnectWise time entry {TimeEntryId} because number of minutes cannot be resolved.", entry.Id);
            return false;
        }

        var normalizedStart = NormalizeUtc(entry.TimeStart) ?? DateTime.UtcNow;
        var normalizedEnd = NormalizeUtc(entry.TimeEnd) ?? normalizedStart.AddMinutes(minutes);

        var kekaEmployee = await kekaEmployeeService.SearchEmployeeByEmailAsync(employeeEmail.Trim(), cancellationToken);
        if (kekaEmployee is null || string.IsNullOrWhiteSpace(kekaEmployee.Id))
        {
            logger.LogWarning(
                "Skipping ConnectWise time entry {TimeEntryId} because Keka employee was not found for email {Email}.",
                entry.Id,
                employeeEmail);
            return false;
        }

        var kekaProject = await ResolveKekaProjectAsync(entry, cancellationToken);
        if (kekaProject is null)
        {
            logger.LogWarning("Skipping ConnectWise time entry {TimeEntryId} because Keka project could not be resolved.", entry.Id);
            return false;
        }

        var taskName = ResolveTaskName(entry.BillableOption, entry.ChargeToType);
        if (string.IsNullOrWhiteSpace(taskName))
        {
            logger.LogWarning(
                "Skipping ConnectWise time entry {TimeEntryId} because chargeToType {ChargeToType} is not supported for task resolution.",
                entry.Id,
                entry.ChargeToType);
            return false;
        }

        var taskId = await ResolveOrCreateTaskIdAsync(kekaProject, taskName, entry.TimeStart, cancellationToken);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            logger.LogWarning(
                "Skipping ConnectWise time entry {TimeEntryId} because task {TaskName} could not be resolved or created.",
                entry.Id,
                taskName);
            return false;
        }

        var request = new KekaTimesheetEntryBatchRequest
        {
            new()
            {
                ProjectId = kekaProject.Id,
                TaskId = taskId,
                NumberOfMinutes = minutes,
                Date = normalizedStart.Date,
                Comment = string.IsNullOrWhiteSpace(entry.Notes)
                    ? $"CW TimeEntry {entry.Id}"
                    : $"CW TimeEntry {entry.Id} - {entry.Notes}",
                StartTime = ToKekaTimeInt(normalizedStart),
                EndTime = ToKekaTimeInt(normalizedEnd)
            }
        };

        await kekaTimesheetEntryService.CreateTimesheetEntryAsync(kekaEmployee.Id, request, cancellationToken);

        logger.LogInformation(
            "Logged ConnectWise time entry {TimeEntryId} to Keka employee {KekaEmployeeId}, project {KekaProjectId}, task {TaskId}, minutes {Minutes}.",
            entry.Id,
            kekaEmployee.Id,
            kekaProject.Id,
            taskId,
            minutes);

        return true;
    }

    private async Task<KekaProject?> ResolveKekaProjectAsync(ConnectWiseTimeEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Project is not null && entry.Project.Id > 0)
        {
            var projectState = await syncStateService.GetAsync(SyncTypes.Project, cancellationToken);
            var mappedProject = projectState?.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, entry.Project.Id.ToString(), StringComparison.Ordinal));

            if (string.IsNullOrWhiteSpace(mappedProject?.KekaProjectId))
            {
                logger.LogWarning(
                    "No Keka project mapping found for ConnectWise project {ProjectId} on time entry {TimeEntryId}.",
                    entry.Project.Id,
                    entry.Id);
                return null;
            }

            var allProjects = await kekaProjectService.GetAllProjectsAsync(cancellationToken);
            var kekaProject = allProjects.FirstOrDefault(p =>
                string.Equals(p.Id, mappedProject.KekaProjectId, StringComparison.OrdinalIgnoreCase));

            if (kekaProject is null)
                logger.LogWarning(
                    "Keka project {KekaProjectId} not found via API for ConnectWise project {ProjectId}.",
                    mappedProject.KekaProjectId,
                    entry.Project.Id);

            return kekaProject;
        }

        if (entry.Company is null || entry.Company.Id <= 0)
        {
            logger.LogWarning("Time entry {TimeEntryId} does not contain project or company context.", entry.Id);
            return null;
        }

        var companyState = await syncStateService.GetAsync(SyncTypes.Company, cancellationToken);
        var kekaClientId = companyState?.Companies
            .FirstOrDefault(c => string.Equals(c.Id, entry.Company.Id.ToString(), StringComparison.Ordinal))
            ?.ClientId;

        if (string.IsNullOrWhiteSpace(kekaClientId))
        {
            logger.LogWarning(
                "No Keka client mapping found for ConnectWise company {CompanyId} on time entry {TimeEntryId}.",
                entry.Company.Id,
                entry.Id);
            return null;
        }

        var projects = await kekaProjectService.GetProjectsByClientIdAsync(kekaClientId, cancellationToken);
        var defaultProjectCode = $"{entry.Company.Id}{DefaultProjectSuffix}";

        var defaultProject = projects.FirstOrDefault(p =>
            string.Equals(p.Code, defaultProjectCode, StringComparison.OrdinalIgnoreCase));

        if (defaultProject is null || string.IsNullOrWhiteSpace(defaultProject.Id))
        {
            logger.LogWarning(
                "Default project was not found for ConnectWise company {CompanyId} (expected code {ProjectCode}).",
                entry.Company.Id,
                defaultProjectCode);
            return null;
        }

        return defaultProject;
    }

    private async Task<string?> ResolveOrCreateTaskIdAsync(
        KekaProject kekaProject,
        string taskName,
        DateTime? timeStart,
        CancellationToken cancellationToken)
    {
        var tasks = await kekaProjectService.GetTasksByProjectAsync(kekaProject.Id, cancellationToken);
        var existingTask = tasks.FirstOrDefault(t =>
            string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase));

        if (existingTask is not null && !string.IsNullOrWhiteSpace(existingTask.Id))
            return existingTask.Id;

        var fallbackStartDate = (NormalizeUtc(timeStart) ?? DateTime.UtcNow).Date;
        var startDate = kekaProject.StartDate?.Date ?? fallbackStartDate;
        var endDate = kekaProject.EndDate?.Date ?? startDate.AddYears(10);

        var createRequest = new KekaTaskRequest
        {
            ProjectId = kekaProject.Id,
            Name = taskName,
            StartDate = startDate,
            EndDate = endDate,
            TaskBillingType = IsBillableTaskName(taskName) ? 1 : 0
        };

        return await kekaProjectService.CreateTaskAsync(kekaProject.Id, createRequest, cancellationToken);
    }

    private static string ResolveTaskName(string? billableOption, string? chargeToType)
    {
        var isBillable = !string.Equals(billableOption?.Trim(), "DoNotBill", StringComparison.OrdinalIgnoreCase);

        return chargeToType?.Trim() switch
        {
            "ChargeCode" => isBillable ? BillableChargeCodeTask : NonBillableChargeCodeTask,
            "ServiceTicket" => isBillable ? BillableServiceTicketTask : NonBillableServiceTicketTask,
            "ProjectTicket" => isBillable ? BillableProjectTicketTask : NonBillableProjectTicketTask,
            _ => string.Empty
        };
    }

    private static bool IsBillableTaskName(string taskName) =>
        string.Equals(taskName, BillableChargeCodeTask, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(taskName, BillableServiceTicketTask, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(taskName, BillableProjectTicketTask, StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveMinutes(ConnectWiseTimeEntry entry, out int minutes)
    {
        minutes = 0;

        if (entry.HoursBilled.HasValue)
        {
            minutes = (int)Math.Round(entry.HoursBilled.Value * 60m, MidpointRounding.AwayFromZero);
            if (minutes > 0)
                return true;
        }

        if (entry.HoursActual.HasValue)
        {
            minutes = (int)Math.Round(entry.HoursActual.Value * 60m, MidpointRounding.AwayFromZero);
            if (minutes > 0)
                return true;
        }

        var start = NormalizeUtc(entry.TimeStart);
        var end = NormalizeUtc(entry.TimeEnd);
        if (start.HasValue && end.HasValue && end > start)
        {
            minutes = (int)Math.Round((end.Value - start.Value).TotalMinutes, MidpointRounding.AwayFromZero);
            return minutes > 0;
        }

        return false;
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
    }

    private static int ToKekaTimeInt(DateTime dateTimeUtc) => (dateTimeUtc.Hour * 100) + dateTimeUtc.Minute;
}
