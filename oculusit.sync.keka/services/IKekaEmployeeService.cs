using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaEmployeeService
{
    /// <summary>
    /// Searches a Keka employee by email address.
    /// </summary>
    Task<KekaEmployee?> SearchEmployeeByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch all Keka employees from keka.
    /// </summary>
    Task<IReadOnlyList<KekaEmployee>> GetAllEmployeeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch all Keka departments from keka.
    /// </summary>
    Task<IReadOnlyList<KekaDepartment>> GetAllDepartmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Keka Employee. Returns the newly created Keka employee ID.
    /// </summary>
    Task<string> CreateEmployeeAsync(KekaEmployeeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the default project manager detail.
    /// </summary>
    Task<KekaEmployee?> GetDefaultProjectManagerEmployeeAsync(CancellationToken cancellationToken = default);

}
