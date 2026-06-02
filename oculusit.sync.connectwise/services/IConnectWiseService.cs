using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

/// <summary>
/// Kept for backward compatibility. Prefer injecting feature-specific services directly.
/// </summary>
public interface IConnectWiseService :
    IConnectWiseCompanyService,
    IConnectWiseMemberService,
    IConnectWiseProjectService,
    IConnectWiseTimeEntryService
{
}
