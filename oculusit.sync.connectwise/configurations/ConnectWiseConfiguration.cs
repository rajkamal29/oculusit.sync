namespace oculusit.sync.connectwise.configurations;

public sealed class ConnectWiseConfiguration
{
    public const string SectionName = "ConnectWise";

    public string BaseUrl { get; init; } = string.Empty;         // e.g. https://na.myconnectwise.net
    public string CompanyId { get; init; } = string.Empty;       // your CW company ID
    public string PublicKey { get; init; } = string.Empty;       // API Member public key
    public string PrivateKey { get; init; } = string.Empty;      // API Member private key
    public string ClientId { get; init; } = string.Empty;        // from developer.connectwise.com
    public string ApiVersion { get; init; } = string.Empty;

    /// <summary>
    /// Page size for paginated ConnectWise API calls. Default is 50 if not set.
    /// </summary>
    public int PageSize { get; init; } = 100;
    public string Environment {  get; init; } = string.Empty;
}