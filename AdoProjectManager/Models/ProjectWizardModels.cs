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
    public TimeSpan Duration { get; set; }
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; }
    public List<WizardStepResult> Steps { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    
    // Results summary
    public int WorkItemsCloned { get; set; }
    public int SecurityGroupsCloned { get; set; }
    public int WikiPagesCloned { get; set; }
    public int AreaPathsCloned { get; set; }
    public int IterationPathsCloned { get; set; }
}

public class WizardStepResult
{
    public string StepName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

// Models for selective update choices
public class ProjectDifferencesAnalysis
{
    public WorkItemDifferences WorkItems { get; set; } = new();
    public ClassificationNodeDifferences ClassificationNodes { get; set; } = new();
    public SecurityGroupDifferences SecurityGroups { get; set; } = new();
    public WikiDifferences Wiki { get; set; } = new();
    public bool HasAnyDifferences => WorkItems.HasChanges || ClassificationNodes.HasChanges || 
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

public class ClassificationNodeDifferences
{
    public List<ClassificationNodeDifference> MissingAreaPaths { get; set; } = new();
    public List<ClassificationNodeDifference> MissingIterationPaths { get; set; } = new();
    public List<ClassificationNodeDifference> DifferentAreaPaths { get; set; } = new();
    public List<ClassificationNodeDifference> DifferentIterationPaths { get; set; } = new();
    public bool HasChanges => MissingAreaPaths.Any() || MissingIterationPaths.Any() || 
                            DifferentAreaPaths.Any() || DifferentIterationPaths.Any();
}

public class ClassificationNodeDifference
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TargetName { get; set; }
    public string NodeType { get; set; } = string.Empty; // "AreaPath", "IterationPath"
    public string DifferenceType { get; set; } = string.Empty; // "Missing", "NameDifferent"
    public string Description { get; set; } = string.Empty;
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
