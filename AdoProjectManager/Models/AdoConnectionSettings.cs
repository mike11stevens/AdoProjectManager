namespace AdoProjectManager.Models;

public enum AuthenticationType
{
    PersonalAccessToken
}

public class AdoConnectionSettings
{
    public string OrganizationUrl { get; set; } = string.Empty;
    public string PersonalAccessToken { get; set; } = string.Empty;
    public string DefaultClonePath { get; set; } = @"C:\Projects";
    public AuthenticationType AuthType { get; set; } = AuthenticationType.PersonalAccessToken;
}
