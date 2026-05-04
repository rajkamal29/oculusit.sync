using oculusit.sync.connectwise.modules;
using oculusit.sync.keka.modules;

namespace oculusit.sync.orchestration.mappings;

public static class KekaClientMapper
{
    private const string FallbackEmail       = "blank_customer_email@oculusit.com";
    private const string FallbackCountryCode = "US";

    // Well-known country name → ISO 3166-1 alpha-2 code mappings
    private static readonly Dictionary<string, string> _countryCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["united states"]  = "US",
        ["usa"]            = "US",
        ["canada"]         = "CA",
        ["united kingdom"] = "GB",
        ["uk"]             = "GB",
        ["australia"]      = "AU",
        ["india"]          = "IN",
        ["germany"]        = "DE",
        ["france"]         = "FR",
        ["singapore"]      = "SG",
    };

    public static KekaClientRequest MapToKekaClientRequest(
        ConnectWiseCompany company,
        string? usdCurrencyId = null) =>
        new()
        {
            Name        = company.Name,
            Description = company.Identifier,
            Code        = company.Id,
            Phone       = company.PhoneNumber,
            Website     = company.Website,
            Email       = string.IsNullOrWhiteSpace(company.InvoiceCCEmailAddress)
                            ? FallbackEmail
                            : company.InvoiceCCEmailAddress,
            BillingInfo = new KekaBillingInfo
            {
                BillingCurrencyId = usdCurrencyId,
                BillingAddress = new KekaBillingAddress
                {
                    AddressLine1 = company.AddressLine1,
                    AddressLine2 = company.AddressLine2,
                    CountryCode  = ResolveCountryCode(company.Country?.Name),
                    City         = company.City,
                    State        = company.State ?? string.Empty,
                    Zip          = company.Zip
                }
            }
        };

    private static string ResolveCountryCode(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName))
            return FallbackCountryCode;

        // If it's already a 2-letter ISO code return as-is (uppercased)
        if (countryName.Length == 2)
            return countryName.ToUpperInvariant();

        return _countryCodeMap.TryGetValue(countryName, out var code)
            ? code
            : FallbackCountryCode;
    }
}
