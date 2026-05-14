using oculusit.sync.connectwise.modules;
using oculusit.sync.keka.modules;
using System.Text.RegularExpressions;

namespace oculusit.sync.orchestration.mappings;

public static class KekaClientMapper
{
    private const string FallbackEmail       = "blank_customer_email@oculusit.com";
    private const string FallbackCountryCode  = "US";
    private const int MaxWebsiteLength = 48;
    private static readonly Regex PhoneRegex = new(@"^(?=.{7,18}$)\(?\+?\(?\d*\)?[ \/()]?\s?([- \/()]?\s?\d[- \/()]?){7,18}$", RegexOptions.Compiled);

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
        string? usdCurrencyId = null)
    {
        var addressLine1 = NullIfEmpty(company.AddressLine1);
        var addressLine2 = NullIfEmpty(company.AddressLine2);
        var city         = NullIfEmpty(company.City);
        var state        = NullIfEmpty(company.State);
        var zip          = NullIfEmpty(company.Zip);
        // CountryCode is mandatory in Keka — always falls back to "US" when not resolvable.
        var countryCode  = ResolveCountryCode(company.Country?.Name);

        // BillingAddress is always built so the mandatory countryCode is always present.
        // If neither a currency ID nor any address detail exists, omit billing info entirely.
        var hasBillingData = usdCurrencyId is not null
                          || addressLine1 is not null || city is not null
                          || state is not null || zip is not null;

        KekaBillingAddress? billingAddress = hasBillingData
            ? new KekaBillingAddress
            {
                AddressLine1 = addressLine1,
                AddressLine2 = addressLine2,
                CountryCode  = countryCode,   // never null — defaults to "US"
                City         = city,
                State        = state,
                Zip          = zip
            }
            : null;

        KekaBillingInfo? billingInfo = hasBillingData
            ? new KekaBillingInfo
            {
                BillingCurrencyId = usdCurrencyId,
                BillingAddress    = billingAddress
            }
            : null;

        return new KekaClientRequest
        {
            Name        = company.Name,
            Description = NullIfEmpty(company.Identifier),
            Code        = company.Id,
            Phone       = ValidatePhone(company.PhoneNumber),
            Website     = ValidateWebsite(company.Website),
            Email       = string.IsNullOrWhiteSpace(company.InvoiceCCEmailAddress)
                            ? FallbackEmail
                            : ExtractFirstEmail(company.InvoiceCCEmailAddress),
            BillingInfo = billingInfo
        };
    }

    private static string ResolveCountryCode(string? countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName))
            return FallbackCountryCode;

        if (countryName.Length == 2)
            return countryName.ToUpperInvariant();

        return _countryCodeMap.TryGetValue(countryName, out var code)
            ? code
            : FallbackCountryCode;
    }

    public static KekaClientUpdateRequest MapToKekaClientUpdateRequest(
        ConnectWiseCompany company)
    {
        var addressLine1 = NullIfEmpty(company.AddressLine1);
        var addressLine2 = NullIfEmpty(company.AddressLine2);
        var city         = NullIfEmpty(company.City);
        var state        = NullIfEmpty(company.State);
        var zip          = NullIfEmpty(company.Zip);
        // CountryCode is mandatory in Keka — always falls back to "US" when not resolvable.
        var countryCode  = ResolveCountryCode(company.Country?.Name);

        // BillingAddress is always built so the mandatory countryCode is always present.
        // If neither any address detail exists, omit billing address entirely.
        var hasBillingData = addressLine1 is not null || city is not null
                          || state is not null || zip is not null;

        KekaBillingAddress? billingAddress = hasBillingData
            ? new KekaBillingAddress
            {
                AddressLine1 = addressLine1,
                AddressLine2 = addressLine2,
                CountryCode  = countryCode,   // never null — defaults to "US"
                City         = city,
                State        = state,
                Zip          = zip
            }
            : new KekaBillingAddress
            {
                CountryCode = countryCode,
            };

        return new KekaClientUpdateRequest
        {
            Name        = company.Name,
            Description = NullIfEmpty(company.Identifier),
            Code        = company.Id,
            Phone       = NullIfEmpty(company.PhoneNumber),
            Website     = NullIfEmpty(company.Website),
            Email       = string.IsNullOrWhiteSpace(company.InvoiceCCEmailAddress)
                            ? FallbackEmail
                            : ExtractFirstEmail(company.InvoiceCCEmailAddress),
            BillingAddress = billingAddress
        };
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Returns the first email address from a semicolon or comma separated list.
    /// If only a single email is present, returns it trimmed as-is.
    /// </summary>
    private static string ExtractFirstEmail(string email) =>
        email.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];

    private static string? ValidatePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var trimmedPhone = phone.Trim();

        // Validate against mobile number regex
        if (!PhoneRegex.IsMatch(trimmedPhone))
            return null;

        return trimmedPhone;
    }

    private static string? ValidateWebsite(string? website)
    {
        if (string.IsNullOrWhiteSpace(website))
            return null;

        var trimmedWebsite = website.Trim();

        // Check length (VARCHAR(48))
        if (trimmedWebsite.Length > MaxWebsiteLength)
            return null;

        return trimmedWebsite;
    }
}
