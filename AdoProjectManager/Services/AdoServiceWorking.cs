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

    public async Task<AdoProject?> GetProjectByIdAsync(string projectId)
    {
        try
        {
            _logger.LogInformation("Getting single project with ID: {ProjectId}", projectId);
            
            using var connection = await GetConnectionAsync();
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var project = await projectClient.GetProject(projectId);
            
            if (project == null)
            {
                _logger.LogWarning("Project with ID {ProjectId} not found", projectId);
                return null;
            }

            var adoProject = new AdoProject
            {
                Id = project.Id.ToString(),
                Name = project.Name,
                Description = project.Description ?? string.Empty,
                Url = ConvertToWebUrl(project.Name, project.Url?.ToString()),
                State = project.State.ToString(),
                LastUpdateTime = project.LastUpdateTime,
                Visibility = project.Visibility.ToString()
            };

            // Get repositories for this project
            adoProject.Repositories = await GetRepositoriesAsync(project.Id.ToString());
            
            _logger.LogInformation("Successfully retrieved project: {ProjectName}", project.Name);
            return adoProject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project by ID: {ProjectId}", projectId);
            return null;
        }
    }

    public async Task<PagedResult<AdoProject>> GetProjectsPagedAsync(ProjectSearchRequest request)
    {
        _logger.LogInformation("🔍 GetProjectsPagedAsync called - Page: {Page}, PageSize: {PageSize}, Search: '{SearchQuery}'", 
            request.Page, request.PageSize, request.SearchQuery);
        
        var result = new PagedResult<AdoProject>
        {
            CurrentPage = request.Page,
            PageSize = request.PageSize,
            SearchQuery = request.SearchQuery
        };
        
        try
        {
            using var connection = await GetConnectionAsync();
            var projectClient = connection.GetClient<ProjectHttpClient>();
            
            // Get all projects (this is cached by Azure DevOps API for performance)
            var allProjects = await projectClient.GetProjects();
            _logger.LogInformation("Retrieved {ProjectCount} total projects from Azure DevOps", allProjects?.Count ?? 0);
            
            if (allProjects == null)
            {
                _logger.LogWarning("GetProjects returned null");
                return result;
            }

            // Convert to our model and apply search filter
            var filteredProjects = new List<AdoProject>();
            
            foreach (var project in allProjects)
            {
                // Apply search filter if provided
                if (!string.IsNullOrEmpty(request.SearchQuery))
                {
                    var searchQuery = request.SearchQuery.ToLowerInvariant();
                    var projectName = project.Name.ToLowerInvariant();
                    var projectDescription = (project.Description ?? "").ToLowerInvariant();
                    
                    // Support wildcard search
                    bool matches = false;
                    if (searchQuery.Contains('*'))
                    {
                        // Convert wildcard to regex pattern
                        var pattern = "^" + searchQuery.Replace("*", ".*") + "$";
                        matches = System.Text.RegularExpressions.Regex.IsMatch(projectName, pattern) ||
                                System.Text.RegularExpressions.Regex.IsMatch(projectDescription, pattern);
                    }
                    else
                    {
                        // Simple contains search
                        matches = projectName.Contains(searchQuery) || projectDescription.Contains(searchQuery);
                    }
                    
                    if (!matches) continue;
                }

                var adoProject = new AdoProject
                {
                    Id = project.Id.ToString(),
                    Name = project.Name,
                    Description = project.Description ?? string.Empty,
                    Url = ConvertToWebUrl(project.Name, project.Url?.ToString()),
                    State = project.State.ToString(),
                    LastUpdateTime = project.LastUpdateTime,
                    Visibility = project.Visibility.ToString()
                };

                // Only get repositories if requested (expensive operation)
                if (request.IncludeRepositories)
                {
                    adoProject.Repositories = await GetRepositoriesAsync(project.Id.ToString());
                }
                else
                {
                    adoProject.Repositories = new List<Models.Repository>();
                }
                
                filteredProjects.Add(adoProject);
            }

            // Set total count after filtering
            result.TotalItems = filteredProjects.Count;
            result.TotalPages = (int)Math.Ceiling((double)result.TotalItems / request.PageSize);

            // Apply pagination
            var skip = (request.Page - 1) * request.PageSize;
            result.Items = filteredProjects.Skip(skip).Take(request.PageSize).ToList();

            _logger.LogInformation("Filtered to {FilteredCount} projects, returning page {Page} with {ItemCount} items", 
                result.TotalItems, request.Page, result.Items.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated projects");
            throw;
        }
    }

    private string ConvertToWebUrl(string projectName, string? apiUrl = null)
    {
        try
        {
            // Get organization URL from settings
            var settings = _settingsService.GetSettingsAsync().Result;
            var orgUrl = settings?.OrganizationUrl ?? "";
            
            if (string.IsNullOrEmpty(orgUrl))
            {
                return apiUrl ?? "";
            }

            // Ensure organization URL ends without slash
            orgUrl = orgUrl.TrimEnd('/');
            
            // Convert to web UI URL format: https://dev.azure.com/{org}/{project}
            return $"{orgUrl}/{Uri.EscapeDataString(projectName)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert to web URL for project {ProjectName}, using API URL", projectName);
            return apiUrl ?? "";
        }
    }

    public async Task<List<AdoProject>> GetProjectsAsync()
    {
        return await GetProjectsAsync(true); // Default to including repositories for backward compatibility
    }

    public async Task<List<AdoProject>> GetProjectsAsync(bool includeRepositories)
    {
        _logger.LogError("🎯🎯🎯 WORKING SERVICE GetProjectsAsync called! IncludeRepositories: {IncludeRepositories} 🎯🎯🎯", includeRepositories);
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

                // Only get repositories if requested
                if (includeRepositories)
                {
                    adoProject.Repositories = await GetRepositoriesAsync(project.Id.ToString());
                }
                else
                {
                    adoProject.Repositories = new List<Models.Repository>();
                }
                
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
