using AdoProjectManager.Models;
using AdoProjectManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdoProjectManager.Controllers;

public class ProjectCloneController : Controller
{
    private readonly ILogger<ProjectCloneController> _logger;
    private readonly IAdoService _adoService;
    private readonly IProjectCloneService _projectCloneService;
    private readonly ISettingsService _settingsService;

    public ProjectCloneController(
        ILogger<ProjectCloneController> logger, 
        IAdoService adoService,
        IProjectCloneService projectCloneService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _adoService = adoService;
        _projectCloneService = projectCloneService;
        _settingsService = settingsService;
    }

    public async Task<IActionResult> Index(int page = 1, string searchQuery = "")
    {
        try
        {
            _logger.LogInformation("ProjectClone Index action called with page {Page}, search: {SearchQuery}", page, searchQuery);
            
            // Use paginated method for better performance with large organizations
            var request = new ProjectSearchRequest
            {
                Page = page,
                PageSize = 20,
                SearchQuery = searchQuery,
                IncludeRepositories = false // Don't need repositories for selection
            };
            
            var result = await _adoService.GetProjectsPagedAsync(request);
            _logger.LogInformation("Retrieved page {Page} with {ProjectCount} projects", page, result.Items?.Count ?? 0);
            
            return View(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading projects for clone selection");
            TempData["Error"] = "Failed to load projects. Please check your connection settings.";
            
            // Return empty paged result on error
            var emptyResult = new PagedResult<AdoProject>
            {
                Items = new List<AdoProject>(),
                CurrentPage = page,
                PageSize = 20,
                TotalItems = 0,
                TotalPages = 0,
                SearchQuery = searchQuery
            };
            
            return View(emptyResult);
        }
    }

    [HttpGet]
    public async Task<IActionResult> SearchProjects(int page = 1, string searchQuery = "")
    {
        try
        {
            var request = new ProjectSearchRequest
            {
                Page = page,
                PageSize = 20,
                SearchQuery = searchQuery,
                IncludeRepositories = false
            };
            
            var result = await _adoService.GetProjectsPagedAsync(request);
            return PartialView("_ProjectsCloneTable", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching projects for clone");
            return PartialView("_ProjectsCloneTable", new PagedResult<AdoProject>
            {
                Items = new List<AdoProject>(),
                CurrentPage = page,
                PageSize = 20,
                TotalItems = 0,
                TotalPages = 0,
                SearchQuery = searchQuery
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Configure(string projectId)
    {
        if (string.IsNullOrEmpty(projectId))
        {
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var sourceProject = await _adoService.GetProjectByIdAsync(projectId);
            
            if (sourceProject == null)
            {
                TempData["Error"] = "Source project not found.";
                return RedirectToAction(nameof(Index));
            }

            // Get current organization URL from settings
            var currentOrgUrl = await GetCurrentOrganizationUrl();

            var model = new ProjectCloneRequest
            {
                SourceProjectId = projectId,
                TargetOrganizationUrl = currentOrgUrl, // Default to current org
                Options = new ProjectCloneOptions
                {
                    CloneRepositories = true,
                    CloneWorkItems = true,
                    CloneAreaPaths = true,
                    CloneIterationPaths = true,
                    CloneBuildPipelines = true,
                    CloneQueries = true,
                    CloneProjectSettings = true,
                    IncludeAllBranches = false,
                    IncludeWorkItemHistory = false
                }
            };

            ViewBag.SourceProject = sourceProject;
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring project clone for project {ProjectId}", projectId);
            TempData["Error"] = "Failed to configure project clone.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> StartClone(ProjectCloneRequest request)
    {
        _logger.LogInformation("StartClone action called with SourceProjectId: {SourceProjectId}, TargetProjectName: {TargetProjectName}", 
            request?.SourceProjectId, request?.TargetProjectName);
        _logger.LogInformation("🔍 TargetOrganizationUrl: {TargetOrganizationUrl}", request?.TargetOrganizationUrl);
        _logger.LogInformation("🔍 Request is null: {IsNull}", request == null);
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("ModelState is invalid. Errors: {Errors}", 
                string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            
            var sourceProject = await _adoService.GetProjectByIdAsync(request.SourceProjectId);
            ViewBag.SourceProject = sourceProject;
            return View("Configure", request);
        }

        try
        {
            // Validate target organization if different from source
            if (!string.IsNullOrEmpty(request.TargetOrganizationUrl))
            {
                _logger.LogInformation("🔍 About to validate target organization: {TargetOrganizationUrl}", request.TargetOrganizationUrl);
                var currentPat = await GetCurrentPat();
                _logger.LogInformation("🔍 Current PAT retrieved: {HasPat}", !string.IsNullOrEmpty(currentPat));
                
                var isValid = await _projectCloneService.ValidateTargetOrganization(
                    request.TargetOrganizationUrl, 
                    currentPat);
                
                _logger.LogInformation("🔍 Validation result: {IsValid}", isValid);
                
                if (!isValid)
                {
                    _logger.LogWarning("❌ Target organization validation failed for URL: {TargetOrganizationUrl}", request.TargetOrganizationUrl);
                    ModelState.AddModelError("TargetOrganizationUrl", "Cannot connect to target organization. Please check the URL and ensure you have access.");
                    var sourceProject = await _adoService.GetProjectByIdAsync(request.SourceProjectId);
                    ViewBag.SourceProject = sourceProject;
                    return View("Configure", request);
                }
            }

            // Store the clone request in TempData to pass to the status page
            TempData["CloneRequest"] = System.Text.Json.JsonSerializer.Serialize(request);
            
            return RedirectToAction(nameof(CloneStatus));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting project clone");
            ModelState.AddModelError("", "Failed to start project clone operation.");
            
            var sourceProject = await _adoService.GetProjectByIdAsync(request.SourceProjectId);
            ViewBag.SourceProject = sourceProject;
            return View("Configure", request);
        }
    }

    public async Task<IActionResult> CloneStatus()
    {
        var cloneRequestJson = TempData["CloneRequest"] as string;
        if (string.IsNullOrEmpty(cloneRequestJson))
        {
            return RedirectToAction(nameof(Index));
        }

        var request = System.Text.Json.JsonSerializer.Deserialize<ProjectCloneRequest>(cloneRequestJson);
        
        // Get source project details for display
        var sourceProject = await _adoService.GetProjectByIdAsync(request.SourceProjectId);
        
        // Generate unique clone operation ID for real-time updates
        var cloneOperationId = Guid.NewGuid().ToString();
        
        ViewBag.SourceProject = sourceProject;
        ViewBag.CloneRequest = request;
        ViewBag.CloneOperationId = cloneOperationId;
        
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ExecuteClone([FromBody] ProjectCloneRequest request)
    {
        try
        {
            // If no clone operation ID is provided, generate one
            if (string.IsNullOrEmpty(request.CloneOperationId))
            {
                request.CloneOperationId = Guid.NewGuid().ToString();
            }

            // Execute clone operation directly (synchronously for simplicity)
            var result = await _projectCloneService.CloneProjectAsync(request);

            // Return the result
            return Json(new { 
                success = result.Success, 
                cloneOperationId = request.CloneOperationId,
                message = result.Success ? "Clone completed successfully" : (result.Error ?? "Clone failed"),
                result = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing project clone");
            return Json(new { 
                success = false, 
                message = ex.Message 
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetProcessTemplates(string organizationUrl)
    {
        try
        {
            var currentPat = await GetCurrentPat();
            var templates = await _projectCloneService.GetAvailableProcessTemplates(organizationUrl, currentPat);
            return Json(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting process templates for {OrganizationUrl}", organizationUrl);
            return Json(new List<string>());
        }
    }

    private async Task<string> GetCurrentPat()
    {
        try
        {
            _logger.LogInformation("🔍 GetCurrentPat: Starting to retrieve PAT using SettingsService");
            var settings = await _settingsService.GetSettingsAsync();
            _logger.LogInformation("🔍 GetCurrentPat: Retrieved settings. PAT length: {PatLength}", settings?.PersonalAccessToken?.Length ?? 0);
            _logger.LogInformation("🔍 GetCurrentPat: PAT is null or empty: {IsNullOrEmpty}", string.IsNullOrEmpty(settings?.PersonalAccessToken));
            return settings?.PersonalAccessToken ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GetCurrentPat: Failed to retrieve PAT using SettingsService");
            return "";
        }
    }

    private async Task<string> GetCurrentOrganizationUrl()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return settings?.OrganizationUrl ?? "https://dev.azure.com/misteven";
        }
        catch
        {
            return "https://dev.azure.com/misteven";
        }
    }
}
