using AdoProjectManager.Models;
using AdoProjectManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdoProjectManager.Controllers;

public class ProjectWizardController : Controller
{
    private readonly ILogger<ProjectWizardController> _logger;
    private readonly IAdoService _adoService;
    private readonly IProjectWizardService _projectWizardService;
    private readonly ISettingsService _settingsService;

    public ProjectWizardController(
        ILogger<ProjectWizardController> logger, 
        IAdoService adoService,
        IProjectWizardService projectWizardService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _adoService = adoService;
        _projectWizardService = projectWizardService;
        _settingsService = settingsService;
    }

    public async Task<IActionResult> Index(string? sourceProject = null)
    {
        try
        {
            _logger.LogInformation("ProjectWizard Index action called");
            
            // Get projects for selection
            var request = new ProjectSearchRequest
            {
                Page = 1,
                PageSize = 100, // Get more projects for selection
                SearchQuery = "",
                IncludeRepositories = false
            };
            
            var result = await _adoService.GetProjectsPagedAsync(request);
            _logger.LogInformation("Retrieved {ProjectCount} projects for wizard", result.Items?.Count ?? 0);
            
            var model = new ProjectWizardRequest
            {
                AvailableProjects = result.Items ?? new List<AdoProject>(),
                SourceProjectId = sourceProject ?? "", // Pre-select if provided
                Options = new ProjectWizardOptions
                {
                    CloneWorkItems = true,
                    CloneSecurityGroups = true,
                    CloneWikiPages = true
                }
            };
            
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading projects for wizard");
            TempData["Error"] = "Failed to load projects. Please check your connection settings.";
            
            var emptyModel = new ProjectWizardRequest
            {
                AvailableProjects = new List<AdoProject>(),
                Options = new ProjectWizardOptions()
            };
            
            return View(emptyModel);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Execute(ProjectWizardRequest request)
    {
        _logger.LogInformation("Wizard Execute action called for Source: {SourceProjectId}, Target: {TargetProjectId}", 
            request?.SourceProjectId, request?.TargetProjectId);
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("ModelState is invalid. Errors: {Errors}", 
                string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            
            // Reload projects for the form
            var projectRequest = new ProjectSearchRequest
            {
                Page = 1,
                PageSize = 100,
                SearchQuery = "",
                IncludeRepositories = false
            };
            var projects = await _adoService.GetProjectsPagedAsync(projectRequest);
            request.AvailableProjects = projects.Items ?? new List<AdoProject>();
            
            return View("Index", request);
        }

        try
        {
            // Store the wizard request in TempData to pass to the status page
            TempData["WizardRequest"] = System.Text.Json.JsonSerializer.Serialize(request);
            
            return RedirectToAction(nameof(WizardStatus));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting wizard operation");
            ModelState.AddModelError("", "Failed to start wizard operation.");
            
            // Reload projects for the form
            var projectRequest = new ProjectSearchRequest
            {
                Page = 1,
                PageSize = 100,
                SearchQuery = "",
                IncludeRepositories = false
            };
            var projects = await _adoService.GetProjectsPagedAsync(projectRequest);
            request.AvailableProjects = projects.Items ?? new List<AdoProject>();
            
            return View("Index", request);
        }
    }

    public async Task<IActionResult> WizardStatus()
    {
        var wizardRequestJson = TempData["WizardRequest"] as string;
        if (string.IsNullOrEmpty(wizardRequestJson))
        {
            return RedirectToAction(nameof(Index));
        }

        var request = System.Text.Json.JsonSerializer.Deserialize<ProjectWizardRequest>(wizardRequestJson);
        
        // Get source and target project details for display
        var sourceProject = await _adoService.GetProjectByIdAsync(request.SourceProjectId);
        var targetProject = await _adoService.GetProjectByIdAsync(request.TargetProjectId);
        
        // Generate unique operation ID
        var operationId = Guid.NewGuid().ToString();
        
        ViewBag.SourceProject = sourceProject;
        ViewBag.TargetProject = targetProject;
        ViewBag.WizardRequest = request;
        ViewBag.OperationId = operationId;
        
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ExecuteWizard([FromBody] ProjectWizardRequest request)
    {
        try
        {
            // If no operation ID is provided, generate one
            if (string.IsNullOrEmpty(request.OperationId))
            {
                request.OperationId = Guid.NewGuid().ToString();
            }

            // Execute wizard operation
            var result = await _projectWizardService.ExecuteWizardAsync(request);

            // Return the result
            return Json(new { 
                success = result.Success, 
                operationId = request.OperationId,
                message = result.Success ? "Wizard completed successfully" : (result.Error ?? "Wizard failed"),
                result = result
            });
        }
        catch (InvalidOperationException ex)
        {
            // Handle project validation errors specifically
            _logger.LogWarning(ex, "Project validation failed for wizard operation");
            return Json(new { 
                success = false, 
                message = ex.Message,
                isValidationError = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing project wizard");
            return Json(new { 
                success = false, 
                message = $"Unexpected error: {ex.Message}"
            });
        }
    }
}
