using Amazon.Runtime.Internal;
using oculusit.sync.core.models;
using oculusit.sync.keka.modules;
using OfficeOpenXml;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace oculusit.sync;

public sealed partial class Worker
{
    /// <summary>
    /// Generates an Excel sheet containing project team members data.
    /// Fetches projects by sync type, retrieves ConnectWise team members,
    /// maps them to Keka employees, and creates an Excel file with the following columns:
    /// Client Code, Project Code, Employee Number, Display Name, Billing Role (Department), Bill Rate, Start Date, End Date.
    /// </summary>
    private async Task GenerateProjectTeamMembersExcelAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting project team members Excel generation.");

        try
        {
            // Get sync type for Project to fetch projects
            var projectSyncState = await syncStateService.GetAsync(SyncTypes.Project, cancellationToken);
            if (projectSyncState is null || projectSyncState.Projects.Count == 0)
            {
                logger.LogWarning("No project sync state found. Skipping Excel generation.");
                return;
            }

            // Get all ConnectWise members and create a dictionary keyed by member ID
            var connectWiseMembers = await connectWiseMemberService.GetAllMembersAsync(cancellationToken);
            var memberDictionary = connectWiseMembers
                .Where(m => m.Id > 0 && !string.IsNullOrWhiteSpace(m.Email))
                .ToDictionary(m => m.Id, m => m.Email);

            logger.LogInformation("Loaded {Count} ConnectWise members into dictionary.", memberDictionary.Count);

            // Get all Keka clients
            var allKekaClients = await kekaClientService.GetAllClientsAsync(cancellationToken);
            var kekaClientCodeById = allKekaClients
                .Where(c => !string.IsNullOrEmpty(c.Id) && !string.IsNullOrEmpty(c.Code))
                .ToDictionary(c => c.Id, c => c.Code ?? string.Empty);

            // Get all Oculus Keka employees
            var allKekaEmployees = await GetAllEmployeeAsync(cancellationToken);
            var kekaEmployeeByEmail = allKekaEmployees
                .Where(e => !string.IsNullOrEmpty(e.Email))
                .ToDictionary(e => e.Email ?? string.Empty, e => (
                            DisplayName: e.DisplayName,
                            EmployeeNumber: e.EmployeeNumber,
                            Department: e.Groups?.FirstOrDefault(g => g.GroupType == 2)?.Title
                            ));

            logger.LogInformation("Loaded {Count} Keka clients into dictionary.", kekaClientCodeById.Count);

            // Prepare Excel data
            var excelRows = new List<ProjectTeamMemberExcelRow>();
            var count = 0;

            foreach (var project in projectSyncState.Projects)
            {
                try
                {
                    // Get project client code
                    if (!kekaClientCodeById.TryGetValue(project.KekaClientId ?? string.Empty, out var clientCode))
                    {
                        logger.LogDebug("No Keka client found for ConnectWise project {ProjectId}. Skipping.", project.Id);
                        continue;
                    }

                    var kekaProject = await kekaProjectService.GetProjectByIdAsync(project?.KekaProjectId ?? string.Empty, cancellationToken);

                    // Get ConnectWise project team members
                    var teamMembers = await connectWiseProjectService.GetProjectMembersAsync(int.Parse(project?.Id ?? "0"), cancellationToken);

                    if (teamMembers.Count == 0)
                    {
                        logger.LogDebug("No team members found for project {ProjectId}.", project.Id);
                        continue;
                    }
                    count = count + teamMembers.Count;

                    foreach (var teamMember in teamMembers)
                    {
                        try
                        {
                            // Get member email from dictionary
                            var memberId = teamMember.Member?.Id ?? 0;
                            if (!memberDictionary.TryGetValue(memberId, out var memberEmail))
                            {
                                logger.LogDebug("Member {MemberId} not found in member dictionary.", memberId);
                                continue;
                            }

                            // Search for Keka employee by email
                            if (!kekaEmployeeByEmail.TryGetValue(memberEmail, out var kekaEmployee))
                            {
                                logger.LogDebug("Keka employee not found for email {Email}.", memberEmail);
                                continue;
                            }

                            // Add row to Excel data
                            excelRows.Add(new ProjectTeamMemberExcelRow
                            {
                                ClientCode = clientCode,
                                ProjectCode = kekaProject?.Code ?? string.Empty,
                                EmployeeNumber = kekaEmployee.EmployeeNumber ?? string.Empty,
                                EmployeeName = kekaEmployee.DisplayName ?? string.Empty,
                                Department = kekaEmployee.Department ?? string.Empty,
                                ProjectStartDate = kekaProject?.StartDate?.ToString("MM/dd/yyyy") ?? string.Empty,
                                ProjectEndDate = kekaProject?.EndDate?.ToString("MM/dd/yyyy") ?? string.Empty,
                                ProjectName = kekaProject?.Name ?? string.Empty
                            });
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "Error processing team member {MemberId} for project {ProjectId}.",
                                teamMember.Member?.Id, project.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Error processing project {ProjectId} for Excel generation.", project.Id);
                }
            }

            if (excelRows.Count == 0)
            {
                logger.LogWarning("No project team member data found to write to Excel.");
                return;
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Generate Excel file
            var fileName = $"ProjectTeamMembers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            var excelFilePath = Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory())!.FullName, fileName);

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Project Team Members");

                // Add headers
                worksheet.Cells[1, 1].Value = "Client Code";
                worksheet.Cells[1, 2].Value = "Project Code";
                worksheet.Cells[1, 3].Value = "Employee Number";
                worksheet.Cells[1, 4].Value = "Employee Name";
                worksheet.Cells[1, 5].Value = "Department";
                worksheet.Cells[1, 6].Value = "Project Start Date";
                worksheet.Cells[1, 7].Value = "Project End Date";
                worksheet.Cells[1, 8].Value = "Project Name";

                // Format header row
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                headerRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);

                // Add data rows
                for (int i = 0; i < excelRows.Count; i++)
                {
                    var row = excelRows[i];
                    var rowIndex = i + 2; // Start from row 2 (after header)

                    worksheet.Cells[rowIndex, 1].Value = row.ClientCode;
                    worksheet.Cells[rowIndex, 2].Value = row.ProjectCode;
                    worksheet.Cells[rowIndex, 3].Value = row.EmployeeNumber;
                    worksheet.Cells[rowIndex, 4].Value = row.EmployeeName;
                    worksheet.Cells[rowIndex, 5].Value = row.Department;
                    worksheet.Cells[rowIndex, 6].Value = row.ProjectStartDate;
                    worksheet.Cells[rowIndex, 7].Value = row.ProjectEndDate;
                    worksheet.Cells[rowIndex, 8].Value = row.ProjectName;
                }

                // Auto-fit columns
                worksheet.Column(1).AutoFit();
                worksheet.Column(2).AutoFit();
                worksheet.Column(3).AutoFit();
                worksheet.Column(4).AutoFit();
                worksheet.Column(5).AutoFit();
                worksheet.Column(6).AutoFit();
                worksheet.Column(7).AutoFit();
                worksheet.Column(8).AutoFit();

                // Save the file
                await package.SaveAsAsync(new FileInfo(excelFilePath), cancellationToken);
            }

            logger.LogInformation(
                "Excel file generated successfully at {FilePath} with {RowCount} project team member(s).",
                excelFilePath, excelRows.Count);
            logger.LogInformation("Total count: {count}", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating project team members Excel file.");
            throw;
        }
    }
    private async Task GenerateClientRateCardExcelAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting client rate card Excel generation.");

        try
        {
            // Get all Keka clients
            var clients = await kekaClientService.GetAllClientsAsync(cancellationToken);

            if (clients.Count == 0)
            {
                logger.LogWarning("No clients found. Skipping Excel generation.");
                return;
            }

            var billingRoles = new List<string>
            {
                "Sales",
                "Command Center",
                "Others",
                "HR",
                "Corporate IT",
                "Marketing",
                "finance",
                "ESA",
                "SPS - Strategic Proactive Security",
                "ITO",
                "Project Management",
                "Institutional Research - OEYE",
                "Customer Success",
                "Management",
                "Facilities",
                "IRE -   Infrastructure Reliability & Engineering"
            };

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var fileName = $"BillingRoles_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            var excelFilePath = Path.Combine(
                Directory.GetParent(Directory.GetCurrentDirectory())!.FullName,
                fileName);

            using var package = new ExcelPackage();

            var worksheet = package.Workbook.Worksheets.Add("Billing Roles");

            // Headers
            worksheet.Cells[1, 1].Value = "Client Code";
            worksheet.Cells[1, 2].Value = "Client Name";
            worksheet.Cells[1, 3].Value = "Billing Role Name";
            worksheet.Cells[1, 4].Value = "Rate Category";
            worksheet.Cells[1, 5].Value = "Description";
            worksheet.Cells[1, 6].Value = "Bill Rate";
            worksheet.Cells[1, 7].Value = "Suggested Cost";

            // Header formatting
            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            headerRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);

            var row = 2;

            foreach (var client in clients)
            {
                foreach (var billingRole in billingRoles)
                {
                    worksheet.Cells[row, 1].Value = client.Code;
                    worksheet.Cells[row, 2].Value = client.Name;
                    worksheet.Cells[row, 3].Value = billingRole;
                    worksheet.Cells[row, 4].Value = string.Empty;
                    worksheet.Cells[row, 5].Value = string.Empty;
                    worksheet.Cells[row, 6].Value = 0;
                    worksheet.Cells[row, 7].Value = string.Empty;

                    row++;
                }
            }

            worksheet.Cells.AutoFitColumns();

            await package.SaveAsAsync(new FileInfo(excelFilePath), cancellationToken);

            logger.LogInformation(
                "Billing roles Excel generated successfully at {FilePath} with {RowCount} rows.",
                excelFilePath,
                row - 2);

            logger.LogInformation(
                "Excel file generated successfully at {FilePath} with {RowCount} clients(s).",
                excelFilePath, clients.Count);
            logger.LogInformation("Total count: {count}", row);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating project team members Excel file.");
            throw;
        }
    }

    /// <summary>
    /// Internal class to hold project team member Excel row data.
    /// </summary>
    private sealed class ProjectTeamMemberExcelRow
    {
        public string ClientCode { get; init; } = string.Empty;
        public string ProjectCode { get; init; } = string.Empty;
        public string EmployeeNumber { get; init; } = string.Empty;
        public string EmployeeName { get; init; } = string.Empty;
        public string Department { get; init; } = string.Empty;
        public string ProjectStartDate { get; init; } = string.Empty;
        public string ProjectEndDate { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    private async Task SetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        var token = string.Empty;
        try
        {
            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "2607138d-c9eb-4377-ab4f-d7a51f402b30",
                ["client_secret"] = "S6pz8Ghueb6rYsOGy0Nj",
                ["grant_type"] = "kekaapi",
                ["scope"] = "kekaapi",
                ["api_key"] = "SxGhjyOtK2mMn7J4qukULTvnHm7u8iJIgnNpjB9OSto="
            });

            var requestUri = new Uri(new Uri("https://login.keka.com"), "/connect/token");
            var response = await _httpClient.PostAsync(requestUri, formContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Keka token request failed. StatusCode: {StatusCode}, Body: {Body}",
                    response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka token request failed with status {response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseBody)
                ?? throw new InvalidOperationException("Keka token response could not be deserialized.");

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Keka token response returned an empty access token.");
            }

            token = tokenResponse.AccessToken;

            logger.LogInformation(
                "Keka access token fetched successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Error while fetching oculusIT Access Token. ErrorMessage: {errorMessage}", ex);
        }
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private Uri BuildUri(string relativePath) =>
        new(new Uri("https://oculusit.keka.com"), $"/api/v1{relativePath}");

    public async Task<IReadOnlyList<KekaEmployee>> GetAllEmployeeAsync(CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var allEmployees = new List<KekaEmployee>();
        var pageNumber = 1;
        bool hasMoreItems;

        do
        {
            var uri = new Uri(BuildUri("/hris/employees"), $"?pageNumber={pageNumber}");
            logger.LogDebug("Fetching Keka employees page {PageNumber}.", pageNumber);

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Received 401 fetching Keka employees page {PageNumber}. Refreshing token.", pageNumber);
                await SetAuthHeaderAsync(cancellationToken);
                response = await _httpClient.GetAsync(uri, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch Keka employees page {PageNumber}. StatusCode: {StatusCode}, Body: {Body}",
                    pageNumber, response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka GET /hris/employees?pageNumber={pageNumber} failed ({(int)response.StatusCode}): {errorBody}",
                    null, response.StatusCode);
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<KekaDataListResponse<KekaEmployee>>(_jsonOptions, cancellationToken);

            if (envelope?.Data is { Count: > 0 } page)
                allEmployees.AddRange(page);

            hasMoreItems = pageNumber < envelope?.TotalPages;
            pageNumber++;
        }
        while (hasMoreItems);

        logger.LogInformation("Fetched {Count} Keka employees total.", allEmployees.Count);
        return allEmployees;
    }

    public async Task SyncProdEmployeeToDemo(CancellationToken cancellationToken = default)
    {
        var prodEmployees = await GetAllEmployeeAsync(cancellationToken);
        var demoEmployees = await kekaEmployeeService.GetAllEmployeeAsync(cancellationToken);
        var demoDepartments = await kekaEmployeeService.GetAllDepartmentsAsync(cancellationToken);
        var demoEmployeeEmails = new HashSet<string>(demoEmployees.Select(e => e.Email ?? string.Empty));
        var count = 0;
        var failed = 0;
        var skipped = 0;
        var departmentNotFoundList = new List<KekaEmployee>();

        foreach (var prodEmployee in prodEmployees.Where(e => e.Email == "rajkamal_vankayala@oculusit.com"))
        {
            try
            {
                var title = prodEmployee.Groups
    .FirstOrDefault(g => g.GroupType == 2)?.Title;
                if (string.IsNullOrWhiteSpace(prodEmployee.Email))
                    continue;

                var demoEmployeeEmail = demoEmployeeEmails.Contains(prodEmployee.Email);
                if (!demoEmployeeEmail)
                {
                    
                    var request = new KekaEmployeeRequest
                    {
                        EmployeeNumber = prodEmployee.EmployeeNumber,
                        DisplayName = prodEmployee.DisplayName,
                        FirstName = prodEmployee.FirstName,
                        MiddleName = prodEmployee.MiddleName,
                        LastName = prodEmployee.LastName,
                        Email = prodEmployee.Email,
                        Gender = prodEmployee.Gender,
                        DateOfBirth = prodEmployee.DateOfBirth ?? new DateTime(1900, 1, 1),
                        DateJoined = prodEmployee.JoiningDate,
                        Department = demoDepartments.FirstOrDefault(d =>
    string.Equals(d.Name, title, StringComparison.OrdinalIgnoreCase))
    ?.Id,
                        JobTitle = "9fff34a9-1dfd-4ab3-a930-e0441d2f35e6",
                        Location = "8c176bc4-08ce-4364-8792-52e9643d3483",
                        LegalEntity = "fd120b84-a4a4-4db4-82bc-397d5d5aebdc",
                    };

                    var response = await kekaEmployeeService.CreateEmployeeAsync(request, cancellationToken);
                    logger.LogInformation("Successfully created employee {Email} in demo environment.", prodEmployee.Email);
                    count++;
                }
                else
                {
                    var response = demoEmployees.FirstOrDefault(d => string.Equals(d.Email, prodEmployee.Email, StringComparison.OrdinalIgnoreCase));
                    var reportingManager = demoEmployees.FirstOrDefault(d => string.Equals(d.Email, prodEmployee?.ReportsTo?.Email ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    var request = new KekaEmployeeUpdateRequest
                    {
                        Department = demoDepartments.FirstOrDefault(d =>
    string.Equals(d.Name, "Marketing", StringComparison.OrdinalIgnoreCase))
    ?.Id
                    };
                    await kekaEmployeeService.UpdateEmployeeAsync(request, response.Id, cancellationToken);
                    logger.LogWarning("Employee {Email} already exists in demo environment.", prodEmployee.Email);
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error occur while Creating Employee {EmployeeEmail} date of birth {DOB} in keka.", prodEmployee.Email, prodEmployee.DateOfBirth);
                departmentNotFoundList.Add(prodEmployee);
                failed++;           
            }
        }
        logger.LogInformation("Successfully created {count} employees in demo environment. Total employee count {prodcount} Skipped {Skipped} Failed {failed}", count, prodEmployees.Count, skipped, failed);
        departmentNotFoundList.ForEach(d => logger.LogInformation("DisplayName: {Name}, Email: {Email}, Employee Number: {Number}, Department: {Department}, exit status: {status}", d.DisplayName, d.Email, d.EmployeeNumber, d.Groups.FirstOrDefault(g => g.GroupType == 2)?.Title, d.ExitStatus));
        logger.LogInformation("Department not found list count {listCount}", departmentNotFoundList.Count);
    }
}
