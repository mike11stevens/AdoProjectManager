using AdoProjectManager.Models;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System.Text.RegularExpressions;

namespace AdoProjectManager.Services;

public class WorkItemDeploymentService
{
    private readonly ILogger<WorkItemDeploymentService> _logger;
    private readonly ISettingsService _settingsService;

    public WorkItemDeploymentService(ILogger<WorkItemDeploymentService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<List<TemplateWorkItem>> GetWorkItemsFromTemplateProject(string sourceProjectId)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            var connection = new VssConnection(new Uri(settings.OrganizationUrl), new VssBasicCredential(string.Empty, settings.PersonalAccessToken));
            var witClient = connection.GetClient<WorkItemTrackingHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            // Get project details
            var project = await projectClient.GetProject(sourceProjectId);
            var projectName = project.Name;

            _logger.LogInformation("Retrieving work items from template project: {ProjectName}", projectName);

            // Query for all work items in the source project
            var wiql = new Wiql
            {
                Query = $@"
                    SELECT [System.Id], [System.Title], [System.WorkItemType], [System.State], 
                           [System.AssignedTo], [System.AreaPath], [System.IterationPath],
                           [System.Tags], [System.Parent], [System.Description]
                    FROM WorkItems 
                    WHERE [System.TeamProject] = '{projectName}'
                    ORDER BY [System.Id]"
            };

            var queryResult = await witClient.QueryByWiqlAsync(wiql, sourceProjectId);
            
            if (queryResult.WorkItems == null || !queryResult.WorkItems.Any())
            {
                _logger.LogWarning("No work items found in template project: {ProjectName}", projectName);
                return new List<TemplateWorkItem>();
            }

            var workItemIds = queryResult.WorkItems.Select(wi => wi.Id).ToArray();
            var workItems = await witClient.GetWorkItemsAsync(workItemIds, expand: WorkItemExpand.Fields);

            var templateWorkItems = new List<TemplateWorkItem>();

            foreach (var wi in workItems)
            {
                var templateWI = new TemplateWorkItem
                {
                    Id = wi.Id ?? 0,
                    Title = GetFieldValue(wi.Fields, "System.Title"),
                    WorkItemType = GetFieldValue(wi.Fields, "System.WorkItemType"),
                    State = GetFieldValue(wi.Fields, "System.State"),
                    Priority = GetFieldValue(wi.Fields, "System.Priority"),
                    AssignedTo = GetDisplayName(GetFieldValue(wi.Fields, "System.AssignedTo")),
                    AreaPath = GetFieldValue(wi.Fields, "System.AreaPath"),
                    IterationPath = GetFieldValue(wi.Fields, "System.IterationPath"),
                    Tags = GetFieldValue(wi.Fields, "System.Tags"),
                    Description = GetFieldValue(wi.Fields, "System.Description"),
                    ParentId = GetParentId(wi.Fields)
                };

                templateWorkItems.Add(templateWI);
            }

            _logger.LogInformation("Retrieved {Count} work items from template project", templateWorkItems.Count);
            return templateWorkItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work items from template project: {ProjectId}", sourceProjectId);
            throw;
        }
    }

    public async Task<WorkItemDeploymentResult> DeployWorkItemsToMultipleProjects(WorkItemDeploymentRequest request, IProgress<string>? progress = null)
    {
        var startTime = DateTime.UtcNow;
        var result = new WorkItemDeploymentResult();
        
        try
        {
            _logger.LogInformation("Starting work item deployment from {SourceProject} to {TargetCount} projects", 
                request.SourceProjectId, request.TargetProjectIds.Count);

            progress?.Report("🚀 Starting multi-project work item deployment...");

            var settings = await _settingsService.GetSettingsAsync();
            var connection = new VssConnection(new Uri(settings.OrganizationUrl), new VssBasicCredential(string.Empty, settings.PersonalAccessToken));
            var witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            // Get source work items
            var sourceWorkItems = await GetSelectedWorkItemsWithDetails(request.SourceProjectId, request.WorkItemIds, witClient);
            if (!sourceWorkItems.Any())
            {
                result.Error = "No work items found to deploy";
                return result;
            }

            progress?.Report($"📋 Found {sourceWorkItems.Count} work items to deploy");

            // Process each target project
            foreach (var targetProjectId in request.TargetProjectIds)
            {
                var projectResult = await DeployWorkItemsToProject(
                    request.SourceProjectId, targetProjectId, sourceWorkItems, 
                    request.Options, witClient, progress);
                
                result.ProjectResults.Add(projectResult);
                result.TotalWorkItemsDeployed += projectResult.WorkItemsCreated + projectResult.WorkItemsUpdated;
                
                if (projectResult.Success)
                    result.SuccessfulProjects++;
                else
                    result.FailedProjects++;
            }

            result.TotalProjectsProcessed = request.TargetProjectIds.Count;
            result.Success = result.SuccessfulProjects > 0;
            result.Duration = DateTime.UtcNow - startTime;

            progress?.Report($"✅ Deployment completed: {result.SuccessfulProjects}/{result.TotalProjectsProcessed} projects successful");
            
            _logger.LogInformation("Work item deployment completed: {Successful}/{Total} projects successful, {WorkItems} work items deployed",
                result.SuccessfulProjects, result.TotalProjectsProcessed, result.TotalWorkItemsDeployed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during multi-project work item deployment");
            result.Success = false;
            result.Error = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
    }

    private async Task<List<WorkItem>> GetSelectedWorkItemsWithDetails(string sourceProjectId, List<int> workItemIds, WorkItemTrackingHttpClient witClient)
    {
        if (!workItemIds.Any())
            return new List<WorkItem>();

        try
        {
            var workItems = await witClient.GetWorkItemsAsync(workItemIds, expand: WorkItemExpand.All);
            return workItems.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work item details from source project");
            throw;
        }
    }

    private async Task<ProjectDeploymentResult> DeployWorkItemsToProject(
        string sourceProjectId, string targetProjectId, List<WorkItem> sourceWorkItems,
        WorkItemDeploymentOptions options, WorkItemTrackingHttpClient witClient, IProgress<string>? progress)
    {
        var startTime = DateTime.UtcNow;
        var result = new ProjectDeploymentResult
        {
            ProjectId = targetProjectId
        };

        try
        {
            // Get target project details
            var settings = await _settingsService.GetSettingsAsync();
            var connection = new VssConnection(new Uri(settings.OrganizationUrl), new VssBasicCredential(string.Empty, settings.PersonalAccessToken));
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var targetProject = await projectClient.GetProject(targetProjectId);
            result.ProjectName = targetProject.Name;

            progress?.Report($"📂 Deploying to project: {targetProject.Name}");

            // Get work item type mappings if needed
            var typeMappings = options.MapWorkItemTypes ? 
                await GetWorkItemTypeMappings(sourceProjectId, targetProjectId, witClient) : 
                new Dictionary<string, string>();

            // Create work item mapping for parent-child relationships
            var workItemMapping = new Dictionary<int, int>(); // Source ID -> Target ID

            // Process work items in dependency order (parents first)
            var sortedWorkItems = SortWorkItemsByDependency(sourceWorkItems);

            foreach (var sourceWI in sortedWorkItems)
            {
                try
                {
                    var detail = await ProcessSingleWorkItem(sourceWI, targetProjectId, options, typeMappings, workItemMapping, witClient);
                    result.WorkItemDetails.Add(detail);

                    if (detail.Success)
                    {
                        if (detail.Action == "Created")
                            result.WorkItemsCreated++;
                        else if (detail.Action == "Updated")
                            result.WorkItemsUpdated++;
                        
                        // Track mapping for parent-child relationships
                        if (detail.TargetWorkItemId.HasValue)
                            workItemMapping[sourceWI.Id ?? 0] = detail.TargetWorkItemId.Value;
                    }
                    else
                    {
                        result.WorkItemsSkipped++;
                    }

                    result.Warnings.AddRange(detail.Warnings);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing work item {WorkItemId} for project {ProjectName}", 
                        sourceWI.Id, targetProject.Name);
                    
                    result.WorkItemDetails.Add(new WorkItemDeploymentDetail
                    {
                        SourceWorkItemId = sourceWI.Id ?? 0,
                        SourceTitle = GetFieldValue(sourceWI.Fields, "System.Title"),
                        WorkItemType = GetFieldValue(sourceWI.Fields, "System.WorkItemType"),
                        Action = "Failed",
                        Success = false,
                        Error = ex.Message
                    });
                    result.WorkItemsSkipped++;
                }
            }

            // Update parent-child relationships
            if (options.IncludeLinks)
            {
                await UpdateParentChildRelationships(sourceWorkItems, workItemMapping, witClient, targetProjectId);
            }

            result.Success = result.WorkItemsCreated > 0 || result.WorkItemsUpdated > 0;
            result.Duration = DateTime.UtcNow - startTime;

            progress?.Report($"✅ {targetProject.Name}: {result.WorkItemsCreated} created, {result.WorkItemsUpdated} updated, {result.WorkItemsSkipped} skipped");

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying work items to project {ProjectId}", targetProjectId);
            result.Success = false;
            result.Error = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
        }

        return result;
    }

    private async Task<WorkItemDeploymentDetail> ProcessSingleWorkItem(
        WorkItem sourceWI, string targetProjectId, WorkItemDeploymentOptions options,
        Dictionary<string, string> typeMappings, Dictionary<int, int> workItemMapping,
        WorkItemTrackingHttpClient witClient)
    {
        var detail = new WorkItemDeploymentDetail
        {
            SourceWorkItemId = sourceWI.Id ?? 0,
            SourceTitle = GetFieldValue(sourceWI.Fields, "System.Title"),
            WorkItemType = GetFieldValue(sourceWI.Fields, "System.WorkItemType")
        };

        try
        {
            // Map work item type if needed
            var targetWorkItemType = detail.WorkItemType;
            if (typeMappings.ContainsKey(detail.WorkItemType))
            {
                targetWorkItemType = typeMappings[detail.WorkItemType];
                detail.Warnings.Add($"Work item type mapped from '{detail.WorkItemType}' to '{targetWorkItemType}'");
            }

            // Check if work item already exists (if updating is enabled)
            WorkItem? existingWI = null;
            if (options.UpdateExisting)
            {
                existingWI = await FindExistingWorkItem(sourceWI, targetProjectId, witClient);
            }

            if (existingWI != null)
            {
                // Update existing work item
                var updateDocument = await CreateWorkItemUpdateDocument(sourceWI, existingWI, targetProjectId, options);
                if (updateDocument.Any())
                {
                    var updatedWI = await witClient.UpdateWorkItemAsync(updateDocument, targetProjectId, existingWI.Id ?? 0);
                    detail.TargetWorkItemId = updatedWI.Id;
                    detail.Action = "Updated";
                    detail.Success = true;
                }
                else
                {
                    detail.Action = "Skipped";
                    detail.Success = true;
                    detail.Warnings.Add("No changes detected");
                }
            }
            else
            {
                // Create new work item
                var createDocument = await CreateWorkItemDocument(sourceWI, targetProjectId, targetWorkItemType, options, workItemMapping);
                var newWI = await witClient.CreateWorkItemAsync(createDocument, targetProjectId, targetWorkItemType);
                detail.TargetWorkItemId = newWI.Id;
                detail.Action = "Created";
                detail.Success = true;
            }
        }
        catch (Exception ex)
        {
            detail.Success = false;
            detail.Error = ex.Message;
            detail.Action = "Failed";
        }

        return detail;
    }

    private async Task<JsonPatchDocument> CreateWorkItemDocument(
        WorkItem sourceWI, string targetProjectId, string workItemType, 
        WorkItemDeploymentOptions options, Dictionary<int, int> workItemMapping)
    {
        var patchDocument = new JsonPatchDocument();

        // Core fields
        AddPatchOperation(patchDocument, "System.Title", GetFieldValue(sourceWI.Fields, "System.Title"));
        AddPatchOperation(patchDocument, "System.Description", GetFieldValue(sourceWI.Fields, "System.Description"));
        
        // Optional fields based on options
        if (!string.IsNullOrEmpty(GetFieldValue(sourceWI.Fields, "System.Tags")))
        {
            AddPatchOperation(patchDocument, "System.Tags", GetFieldValue(sourceWI.Fields, "System.Tags"));
        }

        // Priority and other common fields
        var priority = GetFieldValue(sourceWI.Fields, "System.Priority");
        if (!string.IsNullOrEmpty(priority))
        {
            AddPatchOperation(patchDocument, "System.Priority", priority);
        }

        // Area and Iteration paths (adjust for target project)
        var areaPath = AdjustPathForTargetProject(GetFieldValue(sourceWI.Fields, "System.AreaPath"), targetProjectId);
        var iterationPath = AdjustPathForTargetProject(GetFieldValue(sourceWI.Fields, "System.IterationPath"), targetProjectId);
        
        AddPatchOperation(patchDocument, "System.AreaPath", areaPath);
        AddPatchOperation(patchDocument, "System.IterationPath", iterationPath);

        // Additional fields commonly used in work items
        AddOptionalField(patchDocument, sourceWI.Fields, "System.AcceptanceCriteria");
        AddOptionalField(patchDocument, sourceWI.Fields, "Microsoft.VSTS.Common.BusinessValue");
        AddOptionalField(patchDocument, sourceWI.Fields, "Microsoft.VSTS.Common.ValueArea");
        AddOptionalField(patchDocument, sourceWI.Fields, "Microsoft.VSTS.Scheduling.Effort");
        AddOptionalField(patchDocument, sourceWI.Fields, "Microsoft.VSTS.Scheduling.StoryPoints");

        return patchDocument;
    }

    private async Task<JsonPatchDocument> CreateWorkItemUpdateDocument(
        WorkItem sourceWI, WorkItem existingWI, string targetProjectId, WorkItemDeploymentOptions options)
    {
        var patchDocument = new JsonPatchDocument();

        // Compare and update fields that have changed
        CompareAndAddField(patchDocument, sourceWI.Fields, existingWI.Fields, "System.Title");
        CompareAndAddField(patchDocument, sourceWI.Fields, existingWI.Fields, "System.Description");
        CompareAndAddField(patchDocument, sourceWI.Fields, existingWI.Fields, "System.Tags");
        CompareAndAddField(patchDocument, sourceWI.Fields, existingWI.Fields, "System.Priority");
        CompareAndAddField(patchDocument, sourceWI.Fields, existingWI.Fields, "System.AcceptanceCriteria");

        return patchDocument;
    }

    private void AddPatchOperation(JsonPatchDocument patchDocument, string fieldName, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            patchDocument.Add(new JsonPatchOperation()
            {
                Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                Path = $"/fields/{fieldName}",
                Value = value
            });
        }
    }

    private void AddOptionalField(JsonPatchDocument patchDocument, IDictionary<string, object> fields, string fieldName)
    {
        var value = GetFieldValue(fields, fieldName);
        if (!string.IsNullOrEmpty(value))
        {
            AddPatchOperation(patchDocument, fieldName, value);
        }
    }

    private void CompareAndAddField(JsonPatchDocument patchDocument, IDictionary<string, object> sourceFields, 
        IDictionary<string, object> targetFields, string fieldName)
    {
        var sourceValue = GetFieldValue(sourceFields, fieldName);
        var targetValue = GetFieldValue(targetFields, fieldName);
        
        if (sourceValue != targetValue && !string.IsNullOrEmpty(sourceValue))
        {
            AddPatchOperation(patchDocument, fieldName, sourceValue);
        }
    }

    private string AdjustPathForTargetProject(string path, string targetProjectId)
    {
        if (string.IsNullOrEmpty(path))
            return $"{targetProjectId}";

        // Extract the path parts after the project name
        var pathParts = path.Split('\\');
        if (pathParts.Length > 1)
        {
            // Keep everything after the project name
            var subPath = string.Join("\\", pathParts.Skip(1));
            return $"{targetProjectId}\\{subPath}";
        }

        return targetProjectId;
    }

    private async Task<WorkItem?> FindExistingWorkItem(WorkItem sourceWI, string targetProjectId, WorkItemTrackingHttpClient witClient)
    {
        try
        {
            var title = GetFieldValue(sourceWI.Fields, "System.Title");
            var workItemType = GetFieldValue(sourceWI.Fields, "System.WorkItemType");

            // Query for work item with same title and type
            var wiql = new Wiql
            {
                Query = $@"
                    SELECT [System.Id] 
                    FROM WorkItems 
                    WHERE [System.TeamProject] = @project 
                    AND [System.Title] = '{title.Replace("'", "''")}' 
                    AND [System.WorkItemType] = '{workItemType}'"
            };

            var queryResult = await witClient.QueryByWiqlAsync(wiql, targetProjectId);
            
            if (queryResult.WorkItems?.Any() == true)
            {
                var workItemId = queryResult.WorkItems.First().Id;
                var workItems = await witClient.GetWorkItemsAsync(new[] { workItemId }, expand: WorkItemExpand.Fields);
                return workItems.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for existing work item in project {ProjectId}", targetProjectId);
        }

        return null;
    }

    private async Task<Dictionary<string, string>> GetWorkItemTypeMappings(string sourceProjectId, string targetProjectId, WorkItemTrackingHttpClient witClient)
    {
        try
        {
            var sourceTypes = await witClient.GetWorkItemTypesAsync(sourceProjectId);
            var targetTypes = await witClient.GetWorkItemTypesAsync(targetProjectId);

            var mappings = new Dictionary<string, string>();
            
            foreach (var sourceType in sourceTypes)
            {
                // Direct match first
                var targetType = targetTypes.FirstOrDefault(t => t.Name.Equals(sourceType.Name, StringComparison.OrdinalIgnoreCase));
                if (targetType != null)
                {
                    mappings[sourceType.Name] = targetType.Name;
                    continue;
                }

                // Fallback mappings for common types
                var fallbackMapping = GetFallbackTypeMapping(sourceType.Name, targetTypes.Select(t => t.Name).ToList());
                if (!string.IsNullOrEmpty(fallbackMapping))
                {
                    mappings[sourceType.Name] = fallbackMapping;
                }
            }

            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating work item type mappings between projects");
            return new Dictionary<string, string>();
        }
    }

    private string GetFallbackTypeMapping(string sourceType, List<string> targetTypes)
    {
        // Common work item type mappings
        var fallbackMappings = new Dictionary<string, string[]>
        {
            { "User Story", new[] { "Story", "Feature", "Product Backlog Item", "Task" } },
            { "Story", new[] { "User Story", "Feature", "Product Backlog Item", "Task" } },
            { "Product Backlog Item", new[] { "User Story", "Story", "Feature", "Task" } },
            { "Feature", new[] { "Epic", "User Story", "Story", "Product Backlog Item" } },
            { "Epic", new[] { "Feature", "User Story", "Story" } },
            { "Task", new[] { "User Story", "Story", "Product Backlog Item" } },
            { "Bug", new[] { "Issue", "Task", "User Story" } },
            { "Issue", new[] { "Bug", "Task", "User Story" } }
        };

        if (fallbackMappings.ContainsKey(sourceType))
        {
            foreach (var candidate in fallbackMappings[sourceType])
            {
                if (targetTypes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        // Return first available type as last resort
        return targetTypes.FirstOrDefault() ?? "Task";
    }

    private List<WorkItem> SortWorkItemsByDependency(List<WorkItem> workItems)
    {
        // Simple sorting: items without parents first, then items with parents
        var itemsWithoutParents = workItems.Where(wi => GetParentId(wi.Fields) == null).ToList();
        var itemsWithParents = workItems.Where(wi => GetParentId(wi.Fields) != null).ToList();
        
        var sorted = new List<WorkItem>();
        sorted.AddRange(itemsWithoutParents);
        sorted.AddRange(itemsWithParents);
        
        return sorted;
    }

    private async Task UpdateParentChildRelationships(List<WorkItem> sourceWorkItems, Dictionary<int, int> workItemMapping, 
        WorkItemTrackingHttpClient witClient, string targetProjectId)
    {
        foreach (var sourceWI in sourceWorkItems)
        {
            var parentId = GetParentId(sourceWI.Fields);
            if (parentId.HasValue && workItemMapping.ContainsKey(sourceWI.Id ?? 0) && workItemMapping.ContainsKey(parentId.Value))
            {
                try
                {
                    var childId = workItemMapping[sourceWI.Id ?? 0];
                    var mappedParentId = workItemMapping[parentId.Value];

                    var patchDocument = new JsonPatchDocument();
                    patchDocument.Add(new JsonPatchOperation()
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                        Path = "/relations/-",
                        Value = new
                        {
                            rel = "System.LinkTypes.Hierarchy-Reverse",
                            url = $"https://dev.azure.com/_apis/wit/workItems/{mappedParentId}",
                            attributes = new { comment = "Parent-child relationship created during deployment" }
                        }
                    });

                    await witClient.UpdateWorkItemAsync(patchDocument, targetProjectId, childId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create parent-child relationship for work item {ChildId}", sourceWI.Id);
                }
            }
        }
    }

    private string GetFieldValue(IDictionary<string, object> fields, string fieldName)
    {
        if (fields.TryGetValue(fieldName, out var value))
        {
            return value?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private string GetDisplayName(string assignedToField)
    {
        if (string.IsNullOrEmpty(assignedToField))
            return string.Empty;

        // Extract display name from "DisplayName <email>" format
        var match = Regex.Match(assignedToField, @"^([^<]+)");
        return match.Success ? match.Groups[1].Value.Trim() : assignedToField;
    }

    private int? GetParentId(IDictionary<string, object> fields)
    {
        var parentValue = GetFieldValue(fields, "System.Parent");
        if (int.TryParse(parentValue, out var parentId))
        {
            return parentId;
        }
        return null;
    }
}
