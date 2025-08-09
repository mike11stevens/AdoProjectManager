namespace AdoProjectManager.Models;

public class AdoProject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime LastUpdateTime { get; set; }
    public string Visibility { get; set; } = string.Empty;
    public List<Repository> Repositories { get; set; } = new();
}

public class Repository
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;
    public long Size { get; set; }
}
