using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

/// <summary>
/// Kept for backward compatibility. Prefer injecting
/// <see cref="IConnectWiseCompanyService"/> or <see cref="IConnectWiseProjectService"/> directly.
/// </summary>
public interface IConnectWiseService : IConnectWiseCompanyService, IConnectWiseProjectService
{
}
