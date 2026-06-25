using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaEmployeeService
{
    /// <summary>
    /// Searches a Keka employee by email address.
    /// </summary>
    Task<KekaEmployee?> SearchEmployeeByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the default project manager detail.
    /// </summary>
    Task<KekaEmployee?> GetDefaultProjectManagerEmployeeAsync(CancellationToken cancellationToken = default);

}
