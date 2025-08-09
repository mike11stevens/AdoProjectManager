using System.ComponentModel.DataAnnotations;

namespace AdoProjectManager.Models;

public class ProjectCloneRequest
{
    [Required]
    public string SourceProjectId { get; set; } = string.Empty;
    
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string TargetProjectName { get; set; } = string.Empty;
    
    public string TargetProjectDescription { get; set; } = string.Empty;
    public string TargetOrganizationUrl { get; set; } = string.Empty; // Can be same or different org
    public ProjectCloneOptions Options { get; set; } = new();
}

public class ProjectCloneOptions
{
    public bool CloneRepositories { get; set; } = true;
    public bool CloneWorkItems { get; set; } = true;
    public bool CloneAreaPaths { get; set; } = true;
    public bool CloneIterationPaths { get; set; } = true;
    public bool CloneTeams { get; set; } = true; // Enable teams/permissions cloning by default
    public bool CloneBuildPipelines { get; set; } = true;
    public bool CloneReleasePipelines { get; set; } = true;
    public bool CloneDashboards { get; set; } = true; // Enable dashboards cloning by default
    public bool CloneQueries { get; set; } = true; // Enable queries cloning by default
    public bool CloneProjectSettings { get; set; } = true; // Enable team configuration by default
    
    // Repository specific options
    public bool IncludeAllBranches { get; set; } = false;
    public bool IncludeWorkItemHistory { get; set; } = false;
    public List<string> ExcludeRepositories { get; set; } = new();
}

public class ProjectCloneResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string NewProjectId { get; set; } = string.Empty;
    public string NewProjectUrl { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public List<CloneStepResult> Steps { get; set; } = new();
    public string? Error { get; set; }
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }
}

public class CloneStepResult
{
    public string StepName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
