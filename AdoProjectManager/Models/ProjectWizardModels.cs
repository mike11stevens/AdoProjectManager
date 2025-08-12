using System.ComponentModel.DataAnnotations;

namespace AdoProjectManager.Models;

public class ProjectWizardRequest
{
    [Required]
    [Display(Name = "Source Project")]
    public string SourceProjectId { get; set; } = string.Empty;
    
    [Required]
    [Display(Name = "Target Project")]
    public string TargetProjectId { get; set; } = string.Empty;
    
    public string OperationId { get; set; } = string.Empty;
    
    public ProjectWizardOptions Options { get; set; } = new();
    
    // For form display
    public List<AdoProject> AvailableProjects { get; set; } = new();
}

public class ProjectWizardOptions
{
    [Display(Name = "Clone Work Items")]
    public bool CloneWorkItems { get; set; } = true;
    
    [Display(Name = "Clone Work Item Queries")]
    public bool CloneQueries { get; set; } = true;
    
    [Display(Name = "Clone Security Groups & Members")]
    public bool CloneSecurityGroups { get; set; } = true;
    
    [Display(Name = "Clone Wiki Pages")]
    public bool CloneWikiPages { get; set; } = true;
    
    [Display(Name = "Include Work Item History")]
    public bool IncludeWorkItemHistory { get; set; } = false;
    
    [Display(Name = "Clone Area Paths")]
    public bool CloneAreaPaths { get; set; } = true;
    
    [Display(Name = "Clone Iteration Paths")]
    public bool CloneIterationPaths { get; set; } = true;
}

public class ProjectWizardResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ErrorMessage => Error; // Alias for view compatibility
    public TimeSpan Duration { get; set; }
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; }
    public List<WizardStepResult> Steps { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    
    // Results summary
    public int WorkItemsCloned { get; set; }
    public int? WorkItemsUpdated { get; set; }
    public int SecurityGroupsCloned { get; set; }
    public int WikiPagesCloned { get; set; }
    public int AreaPathsCloned { get; set; }
    public int IterationPathsCloned { get; set; }
    
    // Operation logging
    public List<OperationLog> OperationLogs { get; set; } = new();
}

public class WizardStepResult
{
    public string StepName { get; set; } = string.Empty;
    public string Name => StepName; // Alias for view compatibility
    public string Description { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Completed => Success; // Alias for view compatibility
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DateTime? CompletedAt => Success ? EndTime : null; // Alias for view compatibility
}

public class OperationLog
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? WorkItemId { get; set; }
    public string? OperationType { get; set; } // "Create", "Update", "AreaPath", "IterationPath"
}

// Models for selective update choices
public class ProjectDifferencesAnalysis
{
    public WorkItemDifferences WorkItems { get; set; } = new();
    public QueryDifferences Queries { get; set; } = new();
    public SecurityGroupDifferences SecurityGroups { get; set; } = new();
    public WikiDifferences Wiki { get; set; } = new();
    public bool HasAnyDifferences => WorkItems.HasChanges || Queries.HasChanges || 
                                    SecurityGroups.HasChanges || Wiki.HasChanges;
}

public class WorkItemDifferences
{
    public List<WorkItemDifference> NewItems { get; set; } = new();
    public List<WorkItemDifference> UpdatedItems { get; set; } = new();
    public List<WorkItemDifference> SynchronizedItems { get; set; } = new();
    public bool HasChanges => NewItems.Any() || UpdatedItems.Any();
}

public class WorkItemDifference
{
    public int SourceId { get; set; }
    public int? TargetId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string SourceState { get; set; } = string.Empty;
    public string? TargetState { get; set; }
    public string DifferenceType { get; set; } = string.Empty; // "New", "Update", "Synchronized"
    public string Description { get; set; } = string.Empty;
    public bool Selected { get; set; } = false; // For user selection
}

public class QueryDifferences
{
    public List<QueryDifference> NewQueries { get; set; } = new();
    public List<QueryDifference> UpdatedQueries { get; set; } = new();
    public List<QueryDifference> SynchronizedQueries { get; set; } = new();
    public List<QueryDifference> MissingFolders { get; set; } = new();
    public bool HasChanges => NewQueries.Any() || UpdatedQueries.Any() || MissingFolders.Any();
}

public class QueryDifference
{
    public string QueryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryType { get; set; } = string.Empty; // "Flat", "Tree", "OneHop"
    public string? Wiql { get; set; }
    public string DifferenceType { get; set; } = string.Empty; // "New", "Update", "Synchronized", "Folder"
    public string Description { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool Selected { get; set; } = false; // For user selection
}

public class SecurityGroupDifferences
{
    public List<SecurityGroupDifference> GroupDifferences { get; set; } = new();
    public bool HasChanges => GroupDifferences.Any(gd => gd.HasChanges);
}

public class SecurityGroupDifference
{
    public string GroupName { get; set; } = string.Empty;
    public List<SecurityMemberDifference> MembersToAdd { get; set; } = new();
    public List<SecurityMemberDifference> MembersToRemove { get; set; } = new();
    public bool HasChanges => MembersToAdd.Any() || MembersToRemove.Any();
}

public class SecurityMemberDifference
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PrincipalName { get; set; } = string.Empty;
    public string DifferenceType { get; set; } = string.Empty; // "Add", "Remove"
    public bool Selected { get; set; } = false; // For user selection
}

public class WikiDifferences
{
    public List<WikiPageDifference> PageDifferences { get; set; } = new();
    public bool HasChanges => PageDifferences.Any();
    public string GuidanceMessage { get; set; } = "Manual wiki comparison recommended";
}

public class WikiPageDifference
{
    public string PageName { get; set; } = string.Empty;
    public string DifferenceType { get; set; } = string.Empty; // "Missing", "Different", "New"
    public string Description { get; set; } = string.Empty;
    public bool Selected { get; set; } = false; // For user selection
}

public class SelectiveUpdateRequest
{
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
    public ProjectDifferencesAnalysis Differences { get; set; } = new();
}

// Multi-Project Work Item Deployment Models
public class WorkItemDeploymentRequest
{
    [Required]
    [Display(Name = "Source/Template Project")]
    public string SourceProjectId { get; set; } = string.Empty;
    
    [Required]
    [Display(Name = "Target Projects")]
    public List<string> TargetProjectIds { get; set; } = new();
    
    [Display(Name = "Work Items to Deploy")]
    public List<int> WorkItemIds { get; set; } = new();
    
    public WorkItemDeploymentOptions Options { get; set; } = new();
    
    // For form display
    public List<AdoProject> AvailableProjects { get; set; } = new();
    public List<TemplateWorkItem> AvailableWorkItems { get; set; } = new();
}

public class WorkItemDeploymentOptions
{
    [Display(Name = "Include Work Item History")]
    public bool IncludeHistory { get; set; } = false;
    
    [Display(Name = "Include Attachments")]
    public bool IncludeAttachments { get; set; } = true;
    
    [Display(Name = "Include Links/Relations")]
    public bool IncludeLinks { get; set; } = true;
    
    [Display(Name = "Update Existing Work Items")]
    public bool UpdateExisting { get; set; } = false;
    
    [Display(Name = "Create Area/Iteration Paths if Missing")]
    public bool CreateMissingPaths { get; set; } = true;
    
    [Display(Name = "Map Work Item Types")]
    public bool MapWorkItemTypes { get; set; } = true;
}

public class TemplateWorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public string AreaPath { get; set; } = string.Empty;
    public string IterationPath { get; set; } = string.Empty;
    public bool Selected { get; set; } = false;
    public string Tags { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class WorkItemDeploymentResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProjectDeploymentResult> ProjectResults { get; set; } = new();
    public int TotalWorkItemsDeployed { get; set; }
    public int TotalProjectsProcessed { get; set; }
    public int SuccessfulProjects { get; set; }
    public int FailedProjects { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class ProjectDeploymentResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int WorkItemsCreated { get; set; }
    public int WorkItemsUpdated { get; set; }
    public int WorkItemsSkipped { get; set; }
    public List<WorkItemDeploymentDetail> WorkItemDetails { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

public class WorkItemDeploymentDetail
{
    public int SourceWorkItemId { get; set; }
    public int? TargetWorkItemId { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "Created", "Updated", "Skipped"
    public string? Error { get; set; }
    public bool Success { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class WorkItemTypeMapping
{
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
    public Dictionary<string, string> TypeMappings { get; set; } = new(); // Source Type -> Target Type
    public List<string> SourceTypes { get; set; } = new();
    public List<string> TargetTypes { get; set; } = new();
}
