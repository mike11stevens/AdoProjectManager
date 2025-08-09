using AdoProjectManager.Models;

namespace AdoProjectManager.Services;

public interface IAdoService
{
    Task<List<AdoProject>> GetProjectsAsync();
    Task<List<AdoProject>> GetProjectsAsync(bool includeRepositories);
    Task<PagedResult<AdoProject>> GetProjectsPagedAsync(ProjectSearchRequest request);
    Task<AdoProject?> GetProjectByIdAsync(string projectId);
    Task<List<AdoProjectManager.Models.Repository>> GetRepositoriesAsync(string projectId);
    Task<CloneResult> CloneRepositoryAsync(CloneRequest request);
    Task<bool> TestConnection();
}
