using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IOculusITKekaClientAndProjectService
{
    /// <summary>
    /// Fetches all Keka PSA projects with pagination.
    /// Used to build an in-memory lookup before syncing.
    /// </summary>
    Task<IReadOnlyList<KekaProject>> GetAllOculusITKekaProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all Keka PSA clients in a single call.
    /// Used to build an in-memory lookup before syncing.
    /// </summary>
    Task<IReadOnlyList<KekaClient>> GetAllOculusITKekaClientsAsync(CancellationToken cancellationToken = default);
}
