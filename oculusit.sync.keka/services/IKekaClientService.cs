using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaClientService
{
    /// <summary>
    /// Gets a Keka client by Keka client ID.
    /// </summary>
    Task<KekaClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a Keka client by ConnectWise company ID (stored as code in Keka).
    /// Returns null if not found.
    /// </summary>
    Task<KekaClient?> GetClientByCodeAsync(int code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Keka client.
    /// </summary>
    Task<KekaClient> CreateClientAsync(KekaClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing Keka client by Keka client ID.
    /// </summary>
    Task<KekaClient> UpdateClientAsync(string clientId, KekaClientRequest request, CancellationToken cancellationToken = default);
}
