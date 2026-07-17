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

        await EnsureProjectAllocationAsync(kekaProject, kekaEmployee, cancellationToken);

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
                throw new InvalidOperationException($"Skipping time entry {entry.Id} in batch —  Connectwise project {entry.Project?.Name} not found in keka or Keka project could not be resolved.");
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
                throw new InvalidOperationException($"Skipping time entry {entry.Id} in batch — task {taskName} could not be resolved or created.");
            }

            // Ensure allocation once per unique project in this batch
            if (allocatedProjectIds.Add(kekaProject.Id))
                await EnsureProjectAllocationAsync(kekaProject, kekaEmployee, cancellationToken);

            var normalizedStart = NormalizeUtc(entry.TimeStart) ?? DateTime.UtcNow;
            var normalizedEnd   = NormalizeUtc(entry.TimeEnd)   ?? normalizedStart.AddMinutes(minutes);

            batchRequest.Add(new()
            {
                ProjectId        = kekaProject.Id,
                TaskId           = taskId,
                NumberOfMinutes  = minutes,
                Date             = normalizedStart.Date,
                Comment          = $"CW TimeEntry {entry.Id}",
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
                    "No Keka project mapping found for ConnectWise Project Name: {ProjectName} Project Id: {ProjectId} on time entry {TimeEntryId}.",
                    entry.Project.Id,
                    entry.Project.Name,
                    entry.Id);
                return null;
            }

            var kekaProject = await kekaProjectService.GetProjectByIdAsync(mappedProject.KekaProjectId, cancellationToken);
            if (kekaProject is null)
                logger.LogWarning(
                    "Keka project {KekaProjectId} not found via API for ConnectWise Project Name: {ProjectName} Project Id: {ProjectId}.",
                    mappedProject.KekaProjectId,
                    entry.Project.Name,
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
                "No Keka client mapping found for ConnectWise Company Name: {CompanyName} Company Id: {CompanyId} on time entry {TimeEntryId}.",
                entry.Company.Id,
                entry.Company.Name,
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
                "Default project was not found for ConnectWise Company Name: {CompanyName} Company Id: {CompanyId} (expected code {ProjectCode}).",
                entry.Company.Id,
                entry.Company.Name,
                defaultProjectCode);
            return null;
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

            var projectEndDate = kekaProject.EndDate?.Date;
            if (projectEndDate.HasValue && existingAllocation.EndDate?.Date != projectEndDate)
            {
                try
                {
                    await kekaProjectService.UpdateProjectAllocationAsync(
                        kekaProject.Id,
                        existingAllocation?.Id ?? string.Empty,
                        new KekaUpdateProjectAllocationRequest { EndDate = projectEndDate },
                        cancellationToken);

                    logger.LogInformation(
                        "Updated end date of allocation {AllocationId} to Keka project {ProjectName} to {EndDate}.",
                        existingAllocation?.Id,
                        kekaProject.Name,
                        projectEndDate.Value);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to update end date of allocation {AllocationId} on Keka project {ProjectName}. Continuing with existing allocation.",
                        existingAllocation.Id,
                        kekaProject.Name);
                }
            }

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
                throw new InvalidOperationException($"No Keka billing role found matching department '{departmentName}' for employee {kekaEmployee.Id}.");
            }
        }
        else
        {
            logger.LogWarning(
                "Employee {EmployeeId} has no department group (groupType={GroupType}). Billing role will not be set on allocation.",
                kekaEmployee.Id,
                DepartmentGroupType);
            throw new InvalidOperationException($"Employee {kekaEmployee.Id} has no department group (groupType={DepartmentGroupType}). Billing role will not be set on allocation.");
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
            EndDate = kekaProject.EndDate?.Date ?? null,
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
            throw new InvalidOperationException($"Failed to create project allocation for employee {kekaEmployee.Id} on Keka project {kekaProject.Id}.");
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
        {
            var projectEndDate = kekaProject.EndDate?.Date;
            if (projectEndDate.HasValue && existingTask.EndDate?.Date != projectEndDate)
            {
                try
                {
                    await kekaProjectService.UpdateTaskAsync(
                        kekaProject.Id,
                        existingTask.Id,
                        new KekaTaskUpdateRequest { EndDate = projectEndDate },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to update end date of task {TaskId} on Keka project {ProjectName}. Continuing with existing task.",
                        existingTask.Id,
                        kekaProject.Name);
                    return string.Empty;
                }

                logger.LogInformation(
                    "Updated end date of task {TaskId} on Keka project {ProjectName} to {EndDate}.",
                    existingTask.Id,
                    kekaProject.Name,
                    projectEndDate.Value);
            }

            return existingTask.Id;
        }

        var fallbackStartDate = (NormalizeUtc(timeStart) ?? DateTime.UtcNow).Date;
        var startDate = kekaProject.StartDate?.Date ?? fallbackStartDate;
        var endDate = kekaProject.EndDate?.Date ?? null;

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

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
    }

    private static int ToKekaTimeInt(DateTime dateTimeUtc) => (dateTimeUtc.Hour * 100) + dateTimeUtc.Minute;
}
