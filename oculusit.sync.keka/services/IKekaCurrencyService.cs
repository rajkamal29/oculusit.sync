namespace oculusit.sync.keka.services;

public interface IKekaCurrencyService
{
    /// <summary>
    /// Fetches the Keka internal ID for the USD currency.
    /// Returns null if USD is not found.
    /// </summary>
    Task<string?> GetUsdCurrencyIdAsync(CancellationToken cancellationToken = default);
}
