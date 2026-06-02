using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public interface IConnectWiseMemberService
{
    /// <summary>
    /// Fetches all active ConnectWise members and returns the complete list in memory.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseMember>> GetAllMembersAsync(CancellationToken cancellationToken = default);
}
