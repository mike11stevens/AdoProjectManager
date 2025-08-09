namespace AdoProjectManager.Models;

public class CloneRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public bool IncludeSubmodules { get; set; } = false;
}

public class CloneResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }
}
