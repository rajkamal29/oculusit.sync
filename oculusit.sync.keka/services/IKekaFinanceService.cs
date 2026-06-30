namespace oculusit.sync.keka.services;

public interface IKekaFinanceService
{

    /// <summary>
    /// Retrieves the billing role ID that matches the specified department name.
    /// Returns the billing role ID if a match is found.
    /// </summary>
    Task<string?> GetBillingRoleIdAsync(string departmentName, CancellationToken cancellationToken = default);
}
