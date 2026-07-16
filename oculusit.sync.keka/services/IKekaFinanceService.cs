using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaFinanceService
{

    /// <summary>
    /// Retrieves the billing role that matches the specified department name.
    /// Returns the billing role if a match is found.
    /// </summary>
    Task<KekaRateCard?> GetBillingRoleAsync(string departmentName, CancellationToken cancellationToken = default);
}
