using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public interface IConnectWiseProjectService
{
    /// <summary>
    /// Fetches all projects from ConnectWise by paginating through all pages
    /// and returns the complete list stored in memory.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseProject>> GetAllProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches only projects updated at or after <paramref name="since"/> (UTC),
    /// ordered by lastUpdated ascending.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseProject>> GetProjectsSinceAsync(DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches only the specified project IDs.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseProject>> GetProjectsByIdsAsync(IReadOnlyList<int> projectIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all project statuses from ConnectWise (/project/statuses).
    /// </summary>
    Task<IReadOnlyList<ConnectWiseProjectStatus>> GetAllProjectStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the team members for the specified project (/project/projects/{projectId}/teamMembers).
    /// </summary>
    Task<IReadOnlyList<ConnectWiseProjectTeamMember>> GetProjectMembersAsync(int projectId, CancellationToken cancellationToken = default);
}
