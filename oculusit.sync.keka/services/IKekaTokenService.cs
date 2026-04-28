namespace oculusit.sync.keka.services;

public interface IKekaTokenService
{
    /// <summary>
    /// Returns a valid Bearer access token.
    /// Returns the cached token if still valid, otherwise fetches a new one.
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a new token to be fetched regardless of the cached token state.
    /// Use this when an API call returns 401 Unauthorized.
    /// </summary>
    Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken = default);
}
