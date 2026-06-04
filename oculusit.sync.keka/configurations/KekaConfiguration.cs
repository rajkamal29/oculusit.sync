namespace oculusit.sync.keka.configurations;

public sealed class KekaConfiguration
{
    public const string SectionName = "Keka";
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string IdentityUrl { get; init; } = string.Empty;
    public string TokenEndpoint { get; init; } = "/connect/token";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string GrantType { get; init; } = "kekaapi";
    public string Scope { get; init; } = "kekaapi";

    /// <summary>
    /// Buffer in seconds to refresh the token before it actually expires.
    /// Default is 60 seconds before expiry.
    /// </summary>
    public int TokenExpiryBufferSeconds { get; init; } = 60;
    public string ApiKey {  get; init; } = string.Empty;
}
