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
    /// Returns all tasks that already exist under the given Keka project ID.
    /// Used to skip creation of tasks whose name already exists in Keka.
    /// </summary>
    Task<IReadOnlyList<KekaTask>> GetTasksByProjectAsync(string projectId, CancellationToken cancellationToken = default);
}
