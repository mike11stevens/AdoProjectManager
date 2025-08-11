using AdoProjectManager.Models;
using AdoProjectManager.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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
            
            // Get ALL projects for selection (not paginated for dropdown)
            var request = new ProjectSearchRequest
            {
                Page = 1,
                PageSize = 10000, // Get all projects for selection dropdown
                SearchQuery = "",
                IncludeRepositories = false
            };
            
            var result = await _adoService.GetProjectsPagedAsync(request);
            _logger.LogInformation("Retrieved {ProjectCount} projects for wizard", result.Items?.Count ?? 0);
            
            // Sort projects alphabetically for better user experience
            var sortedProjects = result.Items?.OrderBy(p => p.Name).ToList() ?? new List<AdoProject>();
            
            var model = new ProjectWizardRequest
            {
                AvailableProjects = sortedProjects,
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

    [HttpPost]
    public async Task<IActionResult> AnalyzeDifferences([FromBody] ProjectWizardRequest request)
    {
        try
        {
            _logger.LogInformation("ProjectWizard AnalyzeDifferences action called for Source: {SourceId}, Target: {TargetId}", 
                request.SourceProjectId, request.TargetProjectId);

            var sourceProject = await _adoService.GetProjectByIdAsync(request.SourceProjectId);
            var targetProject = await _adoService.GetProjectByIdAsync(request.TargetProjectId);

            if (sourceProject == null || targetProject == null)
            {
                return Json(new { 
                    success = false, 
                    message = "Could not find one or both projects"
                });
            }

            var analysis = await _projectWizardService.AnalyzeDifferencesAsync(request.SourceProjectId, request.TargetProjectId);

            _logger.LogInformation("🔍 Analysis results before sending to frontend:");
            _logger.LogInformation("   • WorkItems.HasChanges: {HasChanges}", analysis.WorkItems.HasChanges);
            _logger.LogInformation("   • WorkItems.NewItems.Count: {NewCount}", analysis.WorkItems.NewItems.Count);
            _logger.LogInformation("   • WorkItems.UpdatedItems.Count: {UpdatedCount}", analysis.WorkItems.UpdatedItems.Count);
            _logger.LogInformation("   • HasAnyDifferences: {HasAnyDifferences}", analysis.HasAnyDifferences);

            var jsonResult = Json(new { 
                success = true, 
                sourceProject = new { id = sourceProject.Id, name = sourceProject.Name },
                targetProject = new { id = targetProject.Id, name = targetProject.Name },
                differences = analysis
            });

            // Log the serialized JSON for debugging
            var jsonString = System.Text.Json.JsonSerializer.Serialize(analysis);
            _logger.LogInformation("🔍 Serialized analysis JSON length: {Length}", jsonString.Length);
            _logger.LogInformation("🔍 First 500 chars of JSON: {JsonPreview}", jsonString.Length > 500 ? jsonString.Substring(0, 500) : jsonString);

            return jsonResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project differences");
            return Json(new { 
                success = false, 
                message = $"Analysis failed: {ex.Message}"
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ApplySelectiveUpdates([FromBody] SelectiveUpdateRequest request)
    {
        try
        {
            _logger.LogInformation("ProjectWizard ApplySelectiveUpdates action called for Source: {SourceId}, Target: {TargetId}", 
                request.SourceProjectId, request.TargetProjectId);

            // Debug logging for received data
            _logger.LogInformation("🔍 Debug - Request object details:");
            _logger.LogInformation("  • SourceProjectId: '{SourceId}'", request.SourceProjectId);
            _logger.LogInformation("  • TargetProjectId: '{TargetId}'", request.TargetProjectId);
            _logger.LogInformation("  • WorkItems.NewItems.Count: {Count}", request.Differences?.WorkItems?.NewItems?.Count ?? 0);
            
            if (request.Differences?.WorkItems?.NewItems?.Any() == true)
            {
                foreach (var newItem in request.Differences.WorkItems.NewItems.Take(3))
                {
                    _logger.LogInformation("  • NewItem: SourceId={SourceId}, Title='{Title}', WorkItemType='{Type}', Selected={Selected}",
                        newItem.SourceId, newItem.Title ?? "NULL", newItem.WorkItemType ?? "NULL", newItem.Selected);
                }
            }

            var result = await _projectWizardService.ApplySelectiveUpdatesAsync(request);

            // Store result in TempData for status page
            TempData["UpdateResult"] = System.Text.Json.JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return Json(new { 
                success = result.Success, 
                message = result.Success ? "Updates applied successfully" : result.Error,
                result = result,
                redirectUrl = Url.Action("UpdateStatus", "ProjectWizard")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying selective updates");
            
            // Create error result for status page
            var errorResult = new ProjectWizardResult
            {
                Success = false,
                Error = ex.Message,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                OperationLogs = new List<OperationLog>
                {
                    new OperationLog 
                    { 
                        IsSuccess = false, 
                        Message = "Operation failed", 
                        Details = ex.Message,
                        OperationType = "Error"
                    }
                }
            };
            
            TempData["UpdateResult"] = System.Text.Json.JsonSerializer.Serialize(errorResult, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return Json(new { 
                success = false, 
                message = $"Update failed: {ex.Message}",
                redirectUrl = Url.Action("UpdateStatus", "ProjectWizard")
            });
        }
    }

    [HttpGet]
    public IActionResult UpdateStatus()
    {
        ProjectWizardResult? result = null;
        
        if (TempData["UpdateResult"] != null)
        {
            try
            {
                var resultJson = TempData["UpdateResult"]?.ToString();
                if (!string.IsNullOrEmpty(resultJson))
                {
                    result = System.Text.Json.JsonSerializer.Deserialize<ProjectWizardResult>(resultJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing update result from TempData");
            }
        }

        // If no result found, create a default error result
        if (result == null)
        {
            result = new ProjectWizardResult
            {
                Success = false,
                Error = "No operation result found. The operation may have timed out or the session may have expired.",
                StartTime = DateTime.Now,
                EndTime = DateTime.Now
            };
        }

        return View(result);
    }

    [HttpGet]
    public IActionResult SelectiveUpdates()
    {
        // This will be populated by the POST method below
        return View(new ProjectDifferencesAnalysis());
    }

    [HttpPost]
    public IActionResult SelectiveUpdates(string analysis, string sourceProjectId, string targetProjectId)
    {
        try
        {
            _logger.LogInformation("🔍 SelectiveUpdates POST received:");
            _logger.LogInformation("   • Analysis data length: {Length}", analysis?.Length ?? 0);
            _logger.LogInformation("   • SourceProjectId: {SourceId}", sourceProjectId);
            _logger.LogInformation("   • TargetProjectId: {TargetId}", targetProjectId);
            _logger.LogInformation("🔍 First 500 chars of received JSON: {JsonPreview}", 
                !string.IsNullOrEmpty(analysis) && analysis.Length > 500 ? analysis.Substring(0, 500) : analysis ?? "null");

            if (string.IsNullOrEmpty(analysis))
            {
                _logger.LogWarning("⚠️ No analysis data provided to SelectiveUpdates");
                TempData["Error"] = "No analysis data provided";
                return RedirectToAction("Index");
            }

            var differences = System.Text.Json.JsonSerializer.Deserialize<ProjectDifferencesAnalysis>(analysis, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            _logger.LogInformation("🔍 Deserialized analysis results:");
            _logger.LogInformation("   • WorkItems.HasChanges: {HasChanges}", differences?.WorkItems?.HasChanges);
            _logger.LogInformation("   • WorkItems.NewItems.Count: {NewCount}", differences?.WorkItems?.NewItems?.Count ?? 0);
            _logger.LogInformation("   • WorkItems.UpdatedItems.Count: {UpdatedCount}", differences?.WorkItems?.UpdatedItems?.Count ?? 0);
            _logger.LogInformation("   • HasAnyDifferences: {HasAnyDifferences}", differences?.HasAnyDifferences);
            
            // Pass the project IDs to the view via ViewBag
            ViewBag.SourceProjectId = sourceProjectId;
            ViewBag.TargetProjectId = targetProjectId;
            
            return View(differences);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error displaying selective updates");
            TempData["Error"] = $"Error displaying analysis: {ex.Message}";
            return RedirectToAction("Index");
        }
    }
}
