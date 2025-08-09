using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using LibGit2Sharp;
using AdoProjectManager.Models;
using System.Diagnostics;

namespace AdoProjectManager.Services;

public class AdoServiceReal : IAdoService
{
    private readonly AdoConnectionSettings _settings;
    private readonly ILogger<AdoServiceReal> _logger;

    public AdoServiceReal(AdoConnectionSettings settings, ILogger<AdoServiceReal> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    private VssConnection GetConnection()
    {
        // Only use PAT authentication now
        var credentials = new VssBasicCredential(string.Empty, _settings.PersonalAccessToken);
        return new VssConnection(new Uri(_settings.OrganizationUrl), credentials);
    }

    public async Task<bool> TestConnection()
    {
        _logger.LogError("🚀🚀🚀 REAL ADOSERVICE.CS TESTCONNECTION CALLED! 🚀🚀🚀");
        _logger.LogError("=== STARTING TEST CONNECTION ===");
        _logger.LogInformation("Testing connection to Azure DevOps at: {OrganizationUrl}", _settings.OrganizationUrl);
        
        try
        {
            // Validate URL format
            if (string.IsNullOrWhiteSpace(_settings.OrganizationUrl))
            {
                _logger.LogError("Organization URL is empty or null");
                return false;
            }

            if (!Uri.TryCreate(_settings.OrganizationUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogError("Invalid Organization URL format: {OrganizationUrl}", _settings.OrganizationUrl);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_settings.PersonalAccessToken))
            {
                _logger.LogError("Personal Access Token is empty or null");
                return false;
            }

            _logger.LogError("Creating VssConnection...");
            using var connection = GetConnection();
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
                _logger.LogError("2. User is member of projects in organization '{OrganizationUrl}'", _settings.OrganizationUrl);
                _logger.LogError("3. Try regenerating the PAT with full access scopes");
            }
            else
            {
                foreach (var project in projects)
                {
                    _logger.LogError("Found project: {ProjectName} (ID: {ProjectId}, State: {State})", 
                        project.Name, project.Id, project.State);
                }
            }
            
            _logger.LogError("=== TEST CONNECTION RETURNING TRUE ===");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== TEST CONNECTION FAILED: {ErrorMessage} ===", ex.Message);
            return false;
        }
    }

    public async Task<List<AdoProject>> GetProjectsAsync()
    {
        try
        {
            _logger.LogInformation("Starting GetProjectsAsync - connecting to Azure DevOps...");
            using var connection = GetConnection();
            _logger.LogInformation("VssConnection created, getting ProjectHttpClient...");
            
            var projectClient = connection.GetClient<ProjectHttpClient>();
            _logger.LogInformation("ProjectHttpClient obtained, calling GetProjects()...");
            
            var projects = await projectClient.GetProjects();
            _logger.LogInformation("GetProjects() returned {ProjectCount} projects", projects?.Count ?? 0);

            var result = new List<AdoProject>();
            
            if (projects == null || projects.Count == 0)
            {
                _logger.LogWarning("No projects found in Azure DevOps organization. This could be due to:");
                _logger.LogWarning("1. The organization has no projects");
                _logger.LogWarning("2. The Personal Access Token lacks 'Project and team (read)' permissions");
                _logger.LogWarning("3. The user is not a member of any projects in this organization");
                return result;
            }
            
            foreach (var project in projects)
            {
                _logger.LogInformation("Processing project: {ProjectName} (ID: {ProjectId})", project.Name, project.Id);
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
                _logger.LogInformation("Getting repositories for project: {ProjectName}", project.Name);
                adoProject.Repositories = await GetRepositoriesAsync(project.Id.ToString());
                _logger.LogInformation("Found {RepoCount} repositories in project: {ProjectName}", adoProject.Repositories.Count, project.Name);
                result.Add(adoProject);
            }

            _logger.LogInformation("Successfully retrieved {ProjectCount} projects with repositories from Azure DevOps", result.Count);
            return result;
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
            using var connection = GetConnection();
            var gitClient = connection.GetClient<GitHttpClient>();
            var repositories = await gitClient.GetRepositoriesAsync(new Guid(projectId));

            var result = repositories.Select(repo => new Models.Repository
            {
                Id = repo.Id.ToString(),
                Name = repo.Name,
                Url = repo.Url?.ToString() ?? string.Empty,
                CloneUrl = repo.RemoteUrl ?? string.Empty,
                DefaultBranch = repo.DefaultBranch ?? "main",
                Size = repo.Size ?? 0
            }).ToList();

            _logger.LogInformation("Retrieved {RepoCount} repositories for project {ProjectId}", result.Count, projectId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get repositories for project {ProjectId}", projectId);
            throw;
        }
    }

    public async Task<CloneResult> CloneRepositoryAsync(CloneRequest request)
    {
        try
        {
            _logger.LogInformation("Starting clone operation for repository: {RepositoryId}", request.RepositoryId);
            
            var localPath = Path.Combine(_settings.DefaultClonePath, request.LocalPath);
            
            // Create directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            // Get repository details to construct clone URL
            var connection = new VssConnection(new Uri(_settings.OrganizationUrl), new VssBasicCredential(string.Empty, _settings.PersonalAccessToken));
            var gitClient = connection.GetClient<GitHttpClient>();
            var repository = await gitClient.GetRepositoryAsync(request.RepositoryId);
            
            // Get project details
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var project = await projectClient.GetProject(request.ProjectId);
            
            // Construct the clone URL manually if RemoteUrl is empty
            string cloneUrl;
            if (!string.IsNullOrEmpty(repository.RemoteUrl))
            {
                cloneUrl = repository.RemoteUrl;
            }
            else
            {
                // Construct URL: https://dev.azure.com/organization/project/_git/repository
                cloneUrl = $"{_settings.OrganizationUrl}/{project.Name}/_git/{repository.Name}";
            }
            
            _logger.LogInformation("Clone URL: {CloneUrl}", cloneUrl);
            
            if (string.IsNullOrEmpty(cloneUrl))
            {
                throw new InvalidOperationException("Unable to determine clone URL for repository");
            }
            
            var cloneOptions = new CloneOptions
            {
                IsBare = false,
                Checkout = true,
                BranchName = request.Branch
            };
            
            cloneOptions.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
                new UsernamePasswordCredentials
                {
                    Username = "pat", // Use 'pat' as username for Azure DevOps PAT
                    Password = _settings.PersonalAccessToken
                };

            var repoPath = LibGit2Sharp.Repository.Clone(cloneUrl, localPath, cloneOptions);
            
            _logger.LogInformation("Successfully cloned repository {RepositoryName} to {LocalPath}", repository.Name, repoPath);
            
            return new CloneResult
            {
                Success = true,
                Message = $"Repository '{repository.Name}' successfully cloned to '{repoPath}'",
                LocalPath = repoPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository {RepositoryId}", request.RepositoryId);
            return new CloneResult
            {
                Success = false,
                Message = $"Failed to clone repository '{request.RepositoryId}'",
                Error = ex.Message
            };
        }
    }
}
