using AdoProjectManager.Models;

namespace AdoProjectManager.Services;

public interface IAdoService
{
    Task<List<AdoProject>> GetProjectsAsync();
    Task<List<AdoProjectManager.Models.Repository>> GetRepositoriesAsync(string projectId);
    Task<CloneResult> CloneRepositoryAsync(CloneRequest request);
    Task<bool> TestConnection();
}
