using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaProjectService
{
    /// <summary>
    /// Fetches all Keka PSA projects with pagination.
    /// Used to build an in-memory lookup before syncing.
    /// </summary>
    Task<IReadOnlyList<KekaProject>> GetAllProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single Keka PSA project by its ID.
    /// </summary>
    Task<KekaProject?> GetProjectByIdAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches Keka PSA projects for a specific client ID.
    /// </summary>
    Task<IReadOnlyList<KekaProject>> GetProjectsByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Keka PSA project. Returns the newly created Keka project ID.
    /// </summary>
    Task<string> CreateProjectAsync(KekaProjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing Keka PSA project by Keka project ID.
    /// </summary>
    Task UpdateProjectAsync(string projectId, KekaProjectUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a task under a Keka PSA project. Returns the newly created Keka task ID.
    /// </summary>
    Task<string> CreateTaskAsync(string projectId, KekaTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing Keka PSA project task by project ID and task ID.
    /// </summary>
    Task UpdateTaskAsync(string projectId, string taskId, KekaTaskUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all tasks that already exist under the given Keka project ID.
    /// Used to skip creation of tasks whose name already exists in Keka.
    /// </summary>
    Task<IReadOnlyList<KekaTask>> GetTasksByProjectAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Keka project allocation for a specific project.
    /// Returns the Keka allocation identifier.
    /// </summary>
    Task<string> CreateProjectAllocationAsync(string projectId, KekaProjectAllocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets allocations for a Keka PSA project by Keka project ID.
    /// </summary>
    Task<IReadOnlyList<KekaProjectAllocation>> GetProjectAllocationsAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all Keka PSA billing roles.
    /// Used to map an employee department name to its billing role ID.
    /// </summary>
    Task<IReadOnlyList<KekaBillingRole>> GetAllBillingRolesAsync(CancellationToken cancellationToken = default);
}
