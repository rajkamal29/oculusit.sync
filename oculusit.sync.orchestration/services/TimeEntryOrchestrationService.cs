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
    IKekaFinanceService kekaFinanceService,
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

        var normalizedStart = ConvertUtcToEst(entry.TimeStart) ?? DateTime.UtcNow;
        var normalizedEnd = ConvertUtcToEst(entry.TimeEnd) ?? normalizedStart.AddMinutes(minutes);

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

        await EnsureProjectAllocationAsync(kekaProject, kekaEmployee, cancellationToken);

        var request = new KekaTimesheetEntryBatchRequest
        {
            new()
            {
                ProjectId = kekaProject.Id,
                TaskId = taskId,
                NumberOfMinutes = minutes,
                Date = DateOnly.FromDateTime(normalizedStart),
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

    public async Task<int> LogTimeEntriesBatchAsync(
        IReadOnlyList<ConnectWiseTimeEntry> entries,
        string employeeEmail,
        CancellationToken cancellationToken = default)
    {
        if (entries is null || entries.Count == 0)
        {
            logger.LogWarning($"Skipping batch of time entries because there are 0 entries.");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(employeeEmail))
        {
            logger.LogWarning("Skipping batch of {Count} time entries because employee email is empty.", entries.Count);
            throw new InvalidOperationException($"Skipping batch of {entries.Count} time entries because employee email is empty.");
        }

        // Resolve Keka employee once for the entire batch
        var kekaEmployee = await kekaEmployeeService.SearchEmployeeByEmailAsync(employeeEmail.Trim(), cancellationToken);
        if (kekaEmployee is null || string.IsNullOrWhiteSpace(kekaEmployee.Id))
        {
            logger.LogWarning(
                "Skipping batch of {Count} time entries because Keka employee was not found for email {Email}.",
            entries.Count, employeeEmail);
            throw new InvalidOperationException(
                $"Keka employee was not found for email {employeeEmail}. Cannot process batch of {entries.Count} time entries.");
        }

        var batchRequest = new KekaTimesheetEntryBatchRequest();
        var allocatedProjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!TryResolveMinutes(entry, out var minutes) || minutes <= 0)
            {
                logger.LogWarning("Skipping time entry {TimeEntryId} in batch — minutes could not be resolved.", entry.Id);
                continue;
            }

            var kekaProject = await ResolveKekaProjectAsync(entry, cancellationToken);
            if (kekaProject is null)
            {
                logger.LogWarning("Skipping time entry {TimeEntryId} in batch — Keka project could not be resolved.", entry.Id);
                throw new InvalidOperationException($"Skipping time entry {entry.Id} in batch —  Connectwise project ( Project Name : {entry.Project?.Name}) not found in keka or Keka project could not be resolved.");
            }

            var taskName = ResolveTaskName(entry.BillableOption, entry.ChargeToType);
            if (string.IsNullOrWhiteSpace(taskName))
            {
                logger.LogWarning(
                    "Skipping time entry {TimeEntryId} in batch — chargeToType {ChargeToType} is not supported.",
                    entry.Id, entry.ChargeToType);
                throw new InvalidOperationException($"Skipping time entry {entry.Id} in batch — chargeToType {entry.ChargeToType} is not supported.");
            }

            var taskId = await ResolveOrCreateTaskIdAsync(kekaProject, taskName, entry.TimeStart, cancellationToken);
            if (string.IsNullOrWhiteSpace(taskId))
            {
                logger.LogWarning(
                    "Skipping time entry {TimeEntryId} in batch — task {TaskName} could not be resolved or created.",
                    entry.Id, taskName);
                throw new InvalidOperationException($"Skipping time entry {entry.Id} in batch — task ({taskName}) could not be resolved or created for project ({kekaProject.Name}).");
            }

            // Ensure allocation once per unique project in this batch
            if (allocatedProjectIds.Add(kekaProject.Id))
                await EnsureProjectAllocationAsync(kekaProject, kekaEmployee, cancellationToken);

            logger.LogInformation("Start time of TimeEntry {TimeEntryId} in UTC format: {StartTime}", entry.Id, entry.TimeStart);

            var normalizedStart = ConvertUtcToEst(entry.TimeStart) ?? DateTime.UtcNow;
            var normalizedEnd   = ConvertUtcToEst(entry.TimeEnd)   ?? normalizedStart.AddMinutes(minutes);

            var startDate = DateOnly.FromDateTime(normalizedStart);
            logger.LogInformation("Start time of TimeEntry {TimeEntryId} in EST format: {StartTime} and {startDate}", entry.Id, normalizedStart, startDate);

            batchRequest.Add(new()
            {
                ProjectId        = kekaProject.Id,
                TaskId           = taskId,
                NumberOfMinutes  = minutes,
                Date             = startDate,
                Comment          = $"CW TimeEntry {(entry.ChargeToId ?? entry.Project?.Id ?? entry.Company?.Id)?.ToString() ?? "NA"}",
                StartTime        = ToKekaTimeInt(normalizedStart),
                EndTime          = ToKekaTimeInt(normalizedEnd)
            });
        }

        if (batchRequest.Count == 0)
        {
            logger.LogWarning("Batch for employee {Email} resolved to 0 valid entries — nothing posted to Keka.", employeeEmail);
            throw new InvalidOperationException($"Batch for employee {employeeEmail} resolved to 0 valid entries — nothing posted to Keka.");
        }

        await kekaTimesheetEntryService.CreateTimesheetEntryAsync(kekaEmployee.Id, batchRequest, cancellationToken);

        logger.LogInformation(
            "Batch posted {BatchCount}/{TotalCount} time entries to Keka for employee {KekaEmployeeId}.",
            batchRequest.Count, entries.Count, kekaEmployee.Id);

        return batchRequest.Count;
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
                    "No Keka project mapping found for ConnectWise Project Id: {ProjectId} (Project Name: {ProjectName}) for time entry {TimeEntryId}.",
                    entry.Project.Id,
                    entry.Project.Name,
                    entry.Id);
                throw new InvalidOperationException($"Keka project not found for ConnectWise Project Id: {entry.Project.Id} (Project Name: {entry.Project.Name}) for time entry {entry.Id}.");
            }

            var kekaProject = await kekaProjectService.GetProjectByIdAsync(mappedProject.KekaProjectId, cancellationToken);
            if (kekaProject is null)
            {
                logger.LogWarning(
                    "Keka project {KekaProjectId} not found via API for ConnectWise (Project Name: {ProjectName}) (Project Id: {ProjectId}).",
                    mappedProject.KekaProjectId,
                    entry.Project.Name,
                    entry.Project.Id);
                throw new InvalidOperationException($"Keka project {mappedProject.KekaProjectId} not found via API for ConnectWise Project Name: {entry.Project.Name} (Project Id: {entry.Project.Id}).");
            }

            return kekaProject;
        }

        if (entry.Company is null || entry.Company.Id <= 0)
        {
            logger.LogWarning("Time entry {TimeEntryId} does not contain project or company context.", entry.Id);
            throw new InvalidOperationException($"Time entry {entry.Id} does not contain project or company context.");
        }

        var companyState = await syncStateService.GetAsync(SyncTypes.Company, cancellationToken);
        var kekaClientId = companyState?.Companies
            .FirstOrDefault(c => string.Equals(c.Id, entry.Company.Id.ToString(), StringComparison.Ordinal))
            ?.ClientId;

        if (string.IsNullOrWhiteSpace(kekaClientId))
        {
            logger.LogWarning(
                "No Keka client mapping found for ConnectWise Company Id: {CompanyId} (Company Name: {CompanyName}) on time entry {TimeEntryId}.",
                entry.Company.Id,
                entry.Company.Name,
                entry.Id);
            throw new InvalidOperationException($"Keka client not found for ConnectWise Company Id: {entry.Company.Id} (Company Name: {entry.Company.Name}) on time entry {entry.Id}.");
        }

        var projects = await kekaProjectService.GetProjectsByClientIdAsync(kekaClientId, cancellationToken);
        var defaultProjectCode = $"{entry.Company.Id}{DefaultProjectSuffix}";

        var defaultProject = projects.FirstOrDefault(p =>
            string.Equals(p.Code, defaultProjectCode, StringComparison.OrdinalIgnoreCase));

        if (defaultProject is null || string.IsNullOrWhiteSpace(defaultProject.Id))
        {
            logger.LogWarning(
                "Default project was not found for ConnectWise Company Id: {CompanyId} (Company Name: {CompanyName}) (expected code {ProjectCode}).",
                entry.Company.Id,
                entry.Company.Name,
                defaultProjectCode);
            throw new InvalidOperationException($"Default project not found for ConnectWise Company Id: {entry.Company.Id} (Company Name: {entry.Company.Name}) (expected code {defaultProjectCode}).");
        }

        return defaultProject;
    }

    private async Task EnsureProjectAllocationAsync(
        KekaProject kekaProject,
        KekaEmployee kekaEmployee,
        CancellationToken cancellationToken)
    {
        var allocations = await kekaProjectService.GetProjectAllocationsAsync(kekaProject.Id, cancellationToken);

        var existingAllocation = allocations.FirstOrDefault(a =>
            string.Equals(a.Employee?.Id, kekaEmployee.Id, StringComparison.OrdinalIgnoreCase));

        if (existingAllocation is not null)
        {
            logger.LogDebug(
                "Employee {EmployeeEmail} already has an allocation on Keka project {ProjectName}. Skipping creation.",
                kekaEmployee.Email,
                kekaProject.Name);
            return;
        }

        // Resolve billing role by matching employee department name to a billing role name.
        const int DepartmentGroupType = 2;
        var departmentName = kekaEmployee.Groups
            .FirstOrDefault(g => g.GroupType == DepartmentGroupType)
            ?.Title;

        KekaRateCard? billingRole = null;

        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            billingRole = await kekaFinanceService.GetBillingRoleAsync(departmentName, cancellationToken);

            if (billingRole is null)
            {
                logger.LogWarning(
                    "No Keka billing role found matching department '{Department}' for employee {EmployeeId}.",
                    departmentName,
                    kekaEmployee.Id);
                throw new InvalidOperationException($"No Keka billing role found matching department '{departmentName}' for employee {kekaEmployee.Email}.");
            }
        }
        else
        {
            logger.LogWarning(
                "Employee {EmployeeId} has no department group (groupType={GroupType}). Billing role will not be set on allocation.",
                kekaEmployee.Id,
                DepartmentGroupType);
            throw new InvalidOperationException($"Employee {kekaEmployee.Email} has no department group (groupType={DepartmentGroupType}). Billing role will not be set on allocation.");
        }

        var startDate = kekaProject.StartDate?.Date ?? DateTime.UtcNow.Date;

        var allocationRequest = new KekaProjectAllocationRequest
        {
            EmployeeId = kekaEmployee.Id,
            AllocationPercentage = 100,
            BillingRoleId = billingRole.BillingRoleId,
            RateCardId = billingRole.RateCardId,
            RateCategoryId = billingRole.RateCategoryId,
            RateUnit = billingRole.RateUnit,
            StartDate = startDate,
            EndDate = null,
            BillingType = kekaProject.IsBillable
                ? KekaProjectAllocationBillingType.Billable
                : KekaProjectAllocationBillingType.NonBillable
        };

        var projectAllocationId = await kekaProjectService.CreateProjectAllocationAsync(kekaProject.Id, allocationRequest, cancellationToken);

        if(string.IsNullOrWhiteSpace(projectAllocationId))
        {
            logger.LogWarning(
                "Failed to create project allocation for employee {EmployeeId} on Keka project {ProjectName}.",
                kekaEmployee.Id,
                kekaProject.Name);
            throw new InvalidOperationException($"Failed to create project allocation for employee {kekaEmployee.Email} on Keka project {kekaProject.Name}.");
        }

        logger.LogInformation(
            "Created project allocation for employee {EmployeeId} on Keka project {ProjectName} with billing role {BillingRoleId}.",
            kekaEmployee.Id,
            kekaProject.Name,
            billingRole.BillingRoleId);
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

        var fallbackStartDate = (ConvertUtcToEst(timeStart) ?? DateTime.UtcNow).Date;
        var startDate = kekaProject.StartDate?.Date ?? fallbackStartDate;

        var createRequest = new KekaTaskRequest
        {
            ProjectId = kekaProject.Id,
            Name = taskName,
            StartDate = startDate,
            EndDate = null,
            TaskBillingType = IsBillableTaskName(taskName) ? 1 : 0
        };

        return await kekaProjectService.CreateTaskAsync(kekaProject.Id, createRequest, cancellationToken);
    }

    private static string ResolveTaskName(string? billableOption, string? chargeToType)
    {
        var isBillable = !string.Equals(billableOption?.Trim(), "DoNotBill", StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(billableOption?.Trim(), "NoCharge", StringComparison.OrdinalIgnoreCase);

        return chargeToType?.Trim() switch
        {
            "ChargeCode" => isBillable ? BillableChargeCodeTask : NonBillableChargeCodeTask,
            "ServiceTicket" => isBillable ? BillableServiceTicketTask : NonBillableServiceTicketTask,
            "ProjectTicket" => isBillable ? BillableProjectTicketTask : NonBillableProjectTicketTask,
            _ => isBillable ? BillableChargeCodeTask : NonBillableChargeCodeTask,
        };
    }

    private static bool IsBillableTaskName(string taskName) =>
        string.Equals(taskName, BillableChargeCodeTask, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(taskName, BillableServiceTicketTask, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(taskName, BillableProjectTicketTask, StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveMinutes(ConnectWiseTimeEntry entry, out int minutes)
    {
        minutes = 0;

        if (entry.ActualHours.HasValue)
        {
            minutes = (int)Math.Round(entry.ActualHours.Value * 60m, MidpointRounding.AwayFromZero);
            if (minutes > 0)
                return true;
        }

        return false;
    }

    private static DateTime? ConvertUtcToEst(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        if (value.Value.Kind != DateTimeKind.Utc)
            return value.Value;

        return TimeZoneInfo.ConvertTimeFromUtc(
            value.Value,
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
    }

    private static int ToKekaTimeInt(DateTime dateTimeUtc) => (dateTimeUtc.Hour * 100) + dateTimeUtc.Minute;
}
