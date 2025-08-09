using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using LibGit2Sharp;
using AdoProjectManager.Models;
using System.Diagnostics;

namespace AdoProjectManager.Services;

public class AdoServiceWorking : IAdoService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AdoServiceWorking> _logger;

    public AdoServiceWorking(ISettingsService settingsService, ILogger<AdoServiceWorking> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    private async Task<VssConnection> GetConnectionAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        var credentials = new VssBasicCredential(string.Empty, settings.PersonalAccessToken);
        return new VssConnection(new Uri(settings.OrganizationUrl), credentials);
    }

    public async Task<bool> TestConnection()
    {
        _logger.LogError("🎯🎯🎯 WORKING ADOSERVICE TESTCONNECTION CALLED! 🎯🎯🎯");
        _logger.LogError("=== STARTING TEST CONNECTION IN WORKING SERVICE ===");
        
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            _logger.LogInformation("Testing connection to Azure DevOps at: {OrganizationUrl}", settings.OrganizationUrl);
            
            // Validate URL format
            if (string.IsNullOrWhiteSpace(settings.OrganizationUrl))
            {
                _logger.LogError("Organization URL is empty or null");
                return false;
            }

            if (!Uri.TryCreate(settings.OrganizationUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogError("Invalid Organization URL format: {OrganizationUrl}", settings.OrganizationUrl);
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
            {
                _logger.LogError("Personal Access Token is empty or null");
                return false;
            }

            _logger.LogError("Creating VssConnection...");
            using var connection = await GetConnectionAsync();
            _logger.LogError("VssConnection created, attempting to get ProjectHttpClient...");
            
            var projectClient = connection.GetClient<ProjectHttpClient>();
            _logger.LogError("ProjectHttpClient obtained, fetching projects...");
            
            _logger.LogError("About to call projectClient.GetProjects()...");
            var projects = await projectClient.GetProjects();
            _logger.LogError("GetProjects() call completed!");
            _logger.LogError("Successfully connected to Azure DevOps. Found {ProjectCount} projects.", projects?.Count ?? 0);
            
            if (projects == null || projects.Count == 0)
            {
                _logger.LogError("Connection successful but no projects found. Check:");
                _logger.LogError("1. PAT has 'Project and team (read)' permissions");
                _logger.LogError("2. User has access to projects in organization: {OrganizationUrl}", settings.OrganizationUrl);
                _logger.LogError("3. Projects exist in the organization");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connection to Azure DevOps: {ErrorMessage}", ex.Message);
            return false;
        }
    }

    public async Task<List<AdoProject>> GetProjectsAsync()
    {
        _logger.LogError("🎯🎯🎯 WORKING SERVICE GetProjectsAsync called! 🎯🎯🎯");
        var results = new List<AdoProject>();
        
        try
        {
            _logger.LogInformation("Starting GetProjectsAsync - connecting to Azure DevOps...");
            
            using var connection = await GetConnectionAsync();
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var projects = await projectClient.GetProjects();
            
            _logger.LogInformation("Retrieved {ProjectCount} projects from Azure DevOps", projects?.Count ?? 0);
            
            if (projects == null)
            {
                _logger.LogWarning("GetProjects returned null");
                return results;
            }

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
                results.Add(adoProject);
            }

            _logger.LogInformation("Successfully processed {ProjectCount} projects", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get projects from Azure DevOps: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    public async Task<List<Models.Repository>> GetRepositoriesAsync(string projectId)
    {
        try
        {
            using var connection = await GetConnectionAsync();
            var gitClient = connection.GetClient<GitHttpClient>();
            var repositories = await gitClient.GetRepositoriesAsync(new Guid(projectId));

            return repositories.Select(repo => new Models.Repository
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
            var settings = await _settingsService.GetSettingsAsync();
            var localPath = Path.Combine(settings.DefaultClonePath, request.LocalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            // Get the repository clone URL
            using var connection = await GetConnectionAsync();
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
                    Password = settings.PersonalAccessToken
                };

            // Perform the clone
            var gitRepository = LibGit2Sharp.Repository.Clone(repository.RemoteUrl, localPath, cloneOptions);

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
                Message = $"Failed to clone repository '{request.ProjectId}'",
                Error = ex.Message
            };
        }
    }
}
