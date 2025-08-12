using AdoProjectManager.Models;
using AdoProjectManager.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AdoProjectManager.Controllers;

public class WorkItemDeploymentController : Controller
{
    private readonly ILogger<WorkItemDeploymentController> _logger;
    private readonly WorkItemDeploymentService _deploymentService;
    private readonly IAdoService _adoService;

    public WorkItemDeploymentController(
        ILogger<WorkItemDeploymentController> logger,
        WorkItemDeploymentService deploymentService,
        IAdoService adoService)
    {
        _logger = logger;
        _deploymentService = deploymentService;
        _adoService = adoService;
    }

    // GET: WorkItemDeployment
    public IActionResult Index()
    {
        var model = new WorkItemDeploymentRequest
        {
            AvailableProjects = new List<AdoProject>() // Don't load projects on initial page load
        };

        return View(model);
    }

    // GET: WorkItemDeployment/Deploy
    public IActionResult Deploy()
    {
        var model = new WorkItemDeploymentRequest
        {
            AvailableProjects = new List<AdoProject>(), // Will be loaded via AJAX
            Options = new WorkItemDeploymentOptions
            {
                IncludeAttachments = true,
                IncludeLinks = true,
                CreateMissingPaths = true,
                MapWorkItemTypes = true,
                UpdateExisting = false,
                IncludeHistory = false
            }
        };

        return View(model);
    }

    // POST: WorkItemDeployment/SearchProjects
    [HttpPost]
    public async Task<IActionResult> SearchProjects(string searchTerm = "", int page = 1, int pageSize = 20)
    {
        try
        {
            var request = new ProjectSearchRequest
            {
                SearchQuery = searchTerm,
                Page = page,
                PageSize = pageSize
            };
            
            var pagedResult = await _adoService.GetProjectsPagedAsync(request);
            
            var result = new
            {
                projects = pagedResult.Items.Select(p => new { 
                    id = p.Id, 
                    name = p.Name, 
                    description = p.Description ?? ""
                }),
                hasMore = pagedResult.HasNextPage
            };
            
            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching projects");
            return Json(new { error = "Failed to load projects" });
        }
    }

    // POST: WorkItemDeployment/LoadWorkItems
    [HttpPost]
    public async Task<IActionResult> LoadWorkItems([Required] string sourceProjectId)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceProjectId))
            {
                return Json(new { success = false, error = "Source project is required" });
            }

            var workItems = await _deploymentService.GetWorkItemsFromTemplateProject(sourceProjectId);
            
            return Json(new { 
                success = true, 
                workItems = workItems.Select(wi => new {
                    id = wi.Id,
                    title = wi.Title,
                    workItemType = wi.WorkItemType,
                    state = wi.State,
                    priority = wi.Priority,
                    assignedTo = wi.AssignedTo,
                    areaPath = wi.AreaPath,
                    iterationPath = wi.IterationPath,
                    tags = wi.Tags,
                    parentId = wi.ParentId,
                    description = wi.Description
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading work items from source project: {SourceProjectId}", sourceProjectId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    // POST: WorkItemDeployment/Deploy
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deploy(WorkItemDeploymentRequest request)
    {
        if (!ModelState.IsValid)
        {
            // Don't reload projects, let the UI handle it via AJAX
            request.AvailableProjects = new List<AdoProject>();
            return View(request);
        }

        try
        {
            // Validate that we have work items selected
            if (!request.WorkItemIds.Any())
            {
                ModelState.AddModelError("WorkItemIds", "Please select at least one work item to deploy");
                request.AvailableProjects = new List<AdoProject>();
                return View(request);
            }

            // Validate that we have target projects selected
            if (!request.TargetProjectIds.Any())
            {
                ModelState.AddModelError("TargetProjectIds", "Please select at least one target project");
                request.AvailableProjects = new List<AdoProject>();
                return View(request);
            }

            // Remove source project from target projects if it was selected
            request.TargetProjectIds.RemoveAll(id => id == request.SourceProjectId);

            if (!request.TargetProjectIds.Any())
            {
                ModelState.AddModelError("TargetProjectIds", "Target projects cannot include the source project");
                request.AvailableProjects = new List<AdoProject>();
                return View(request);
            }

            // Store the request data in TempData for the results page
            TempData["DeploymentRequest"] = System.Text.Json.JsonSerializer.Serialize(request);

            // Redirect to the deployment progress page
            return RedirectToAction("DeployProgress");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating work item deployment");
            ModelState.AddModelError("", "An error occurred while initiating the deployment: " + ex.Message);
            request.AvailableProjects = new List<AdoProject>();
            return View(request);
        }
    }

    // GET: WorkItemDeployment/DeployProgress
    public IActionResult DeployProgress()
    {
        var requestJson = TempData["DeploymentRequest"] as string;
        if (string.IsNullOrEmpty(requestJson))
        {
            return RedirectToAction("Index");
        }

        var request = System.Text.Json.JsonSerializer.Deserialize<WorkItemDeploymentRequest>(requestJson);
        return View(request);
    }

    // POST: WorkItemDeployment/ExecuteDeployment
    [HttpPost]
    public async Task<IActionResult> ExecuteDeployment([FromBody] WorkItemDeploymentRequest request)
    {
        try
        {
            var result = await _deploymentService.DeployWorkItemsToMultipleProjects(request);
            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing work item deployment");
            return Json(new { success = false, error = ex.Message });
        }
    }

    // GET: WorkItemDeployment/Results/{operationId}
    public IActionResult Results(WorkItemDeploymentResult result)
    {
        return View(result);
    }

    // API endpoints for AJAX calls

    // GET: WorkItemDeployment/Api/Projects
    [HttpGet]
    public async Task<IActionResult> GetProjects()
    {
        try
        {
            var projects = await _adoService.GetProjectsAsync();
            return Json(projects.Select(p => new { id = p.Id, name = p.Name }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving projects");
            return Json(new { error = ex.Message });
        }
    }

    // GET: WorkItemDeployment/Api/WorkItems/{projectId}
    [HttpGet]
    public async Task<IActionResult> GetWorkItems(string projectId)
    {
        try
        {
            if (string.IsNullOrEmpty(projectId))
            {
                return BadRequest("Project ID is required");
            }

            var workItems = await _deploymentService.GetWorkItemsFromTemplateProject(projectId);
            return Json(workItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work items for project: {ProjectId}", projectId);
            return Json(new { error = ex.Message });
        }
    }

    // POST: WorkItemDeployment/Api/ValidateDeployment
    [HttpPost]
    public IActionResult ValidateDeployment([FromBody] WorkItemDeploymentRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.SourceProjectId))
        {
            errors.Add("Source project is required");
        }

        if (!request.TargetProjectIds.Any())
        {
            errors.Add("At least one target project is required");
        }

        if (!request.WorkItemIds.Any())
        {
            errors.Add("At least one work item must be selected");
        }

        if (request.TargetProjectIds.Contains(request.SourceProjectId))
        {
            errors.Add("Source project cannot be included in target projects");
        }

        return Json(new { 
            isValid = !errors.Any(), 
            errors = errors 
        });
    }
}
