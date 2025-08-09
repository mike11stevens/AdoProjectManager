using System.ComponentModel.DataAnnotations;

namespace AdoProjectManager.Models;

public class SettingsViewModel
{
    [Required]
    [Display(Name = "Organization URL")]
    [Url(ErrorMessage = "Please enter a valid URL")]
    public string OrganizationUrl { get; set; } = string.Empty;

    [Display(Name = "Default Clone Path")]
    public string DefaultClonePath { get; set; } = @"C:\Projects";

    [Display(Name = "Authentication Type")]
    public AuthenticationType AuthType { get; set; } = AuthenticationType.PersonalAccessToken;

    [Required]
    [Display(Name = "Personal Access Token")]
    public string PersonalAccessToken { get; set; } = string.Empty;

    public bool TestConnectionResult { get; set; }
    public string TestConnectionMessage { get; set; } = string.Empty;
}
