using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaClientService
{
    /// <summary>
    /// Gets a Keka client by Keka client ID.
    /// </summary>
    Task<KekaClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all Keka PSA clients in a single call.
    /// Used to build an in-memory lookup before syncing.
    /// </summary>
    Task<IReadOnlyList<KekaClient>> GetAllClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Keka client. Returns the newly created Keka client ID.
    /// </summary>
    Task<string> CreateClientAsync(KekaClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing Keka client by Keka client ID.
    /// </summary>
    Task UpdateClientAsync(string clientId, KekaClientUpdateRequest request, CancellationToken cancellationToken = default);
}
