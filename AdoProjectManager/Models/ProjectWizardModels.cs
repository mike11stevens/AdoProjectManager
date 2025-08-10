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
