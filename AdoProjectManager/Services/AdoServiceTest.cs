#if false // DISABLED - Conflicting with the real AdoService class
using AdoProjectManager.Models;

namespace AdoProjectManager.Services;

public class AdoService : IAdoService
{
    private readonly AdoConnectionSettings _settings;
    private readonly ILogger<AdoService> _logger;

    public AdoService(AdoConnectionSettings settings, ILogger<AdoService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task<bool> TestConnection()
    {
        return Task.FromResult(true); // Simplified for testing
    }

    public Task<List<AdoProject>> GetProjectsAsync()
    {
        return Task.FromResult(new List<AdoProject>());
    }

    public Task<List<Models.Repository>> GetRepositoriesAsync(string projectId)
    {
        return Task.FromResult(new List<Models.Repository>());
    }

    public Task<CloneResult> CloneRepositoryAsync(CloneRequest request)
    {
        return Task.FromResult(new CloneResult 
        { 
            Success = false, 
            Message = "Simplified implementation for testing" 
        });
    }
}
#endif // End of disabled AdoServiceTest
