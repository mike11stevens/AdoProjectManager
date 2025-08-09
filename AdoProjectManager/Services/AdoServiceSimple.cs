#if false // DISABLED - Conflicting with the real AdoService class
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.Identity.Client;
using LibGit2Sharp;
using AdoProjectManager.Models;
using System.Diagnostics;
using AdoRepository = AdoProjectManager.Models.Repository;
using GitRepository = LibGit2Sharp.Repository;

namespace AdoProjectManager.Services;

public class AdoService : IAdoService
{
    private readonly AdoConnectionSettings _settings;
    private readonly ILogger<AdoService> _logger;
    private VssConnection? _connection;

    public AdoService(AdoConnectionSettings settings, ILogger<AdoService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool TestConnection()
    {
        try
        {
            var credentials = new VssBasicCredential(string.Empty, _settings.PersonalAccessToken);
            var connection = new VssConnection(new Uri(_settings.OrganizationUrl), credentials);
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var projects = projectClient.GetProjects().Result;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connection to Azure DevOps");
            return false;
        }
    }

    public async Task<List<AdoProject>> GetProjectsAsync()
    {
        try
        {
            var credentials = new VssBasicCredential(string.Empty, _settings.PersonalAccessToken);
            var connection = new VssConnection(new Uri(_settings.OrganizationUrl), credentials);
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var projects = await projectClient.GetProjects();

            var result = new List<AdoProject>();
            
            foreach (var project in projects)
            {
                var adoProject = new AdoProject
                {
                    Id = project.Id.ToString(),
                    Name = project.Name,
                    Description = project.Description ?? string.Empty,
                    Url = project.Url?.ToString() ?? string.Empty,
                    State = project.State.ToString(),
                    LastUpdateTime = project.LastUpdateTime,
                    Visibility = project.Visibility.ToString()
                };

                // Get repositories for this project
                adoProject.Repositories = await GetRepositoriesAsync(project.Id.ToString());
                result.Add(adoProject);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get projects from Azure DevOps");
            throw;
        }
    }

    public async Task<List<AdoRepository>> GetRepositoriesAsync(string projectId)
    {
        try
        {
            var credentials = new VssBasicCredential(string.Empty, _settings.PersonalAccessToken);
            var connection = new VssConnection(new Uri(_settings.OrganizationUrl), credentials);
            var gitClient = connection.GetClient<GitHttpClient>();
            var repositories = await gitClient.GetRepositoriesAsync(new Guid(projectId));

            return repositories.Select(repo => new AdoRepository
            {
                Id = repo.Id.ToString(),
                Name = repo.Name,
                Url = repo.Url?.ToString() ?? string.Empty,
                CloneUrl = repo.RemoteUrl ?? string.Empty,
                DefaultBranch = repo.DefaultBranch ?? "main",
                Size = repo.Size ?? 0
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get repositories for project {ProjectId}", projectId);
            throw;
        }
    }

    public async Task<CloneResult> CloneRepositoryAsync(CloneRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Ensure the local path directory exists
            var localPath = Path.Combine(_settings.DefaultClonePath, request.LocalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            // Get the repository clone URL
            var credentials = new VssBasicCredential(string.Empty, _settings.PersonalAccessToken);
            var connection = new VssConnection(new Uri(_settings.OrganizationUrl), credentials);
            var gitClient = connection.GetClient<GitHttpClient>();
            var repository = await gitClient.GetRepositoryAsync(new Guid(request.ProjectId), new Guid(request.RepositoryId));

            if (repository?.RemoteUrl == null)
            {
                return new CloneResult
                {
                    Success = false,
                    Message = "Repository not found or no remote URL available",
                    Duration = stopwatch.Elapsed
                };
            }

            // Prepare clone options
            var cloneOptions = new CloneOptions
            {
                BranchName = request.Branch,
                RecurseSubmodules = request.IncludeSubmodules
            };
            
            cloneOptions.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) => 
                new UsernamePasswordCredentials
                {
                    Username = string.Empty,
                    Password = _settings.PersonalAccessToken
                };

            // Perform the clone
            GitRepository.Clone(repository.RemoteUrl, localPath, cloneOptions);

            stopwatch.Stop();

            return new CloneResult
            {
                Success = true,
                Message = $"Successfully cloned repository '{repository.Name}' to '{localPath}'",
                LocalPath = localPath,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to clone repository");
            
            return new CloneResult
            {
                Success = false,
                Message = "Failed to clone repository",
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }
}
#endif // End of disabled AdoServiceSimple
