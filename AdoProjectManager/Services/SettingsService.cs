using AdoProjectManager.Data;
using AdoProjectManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoProjectManager.Services;

public interface ISettingsService
{
    Task<AdoConnectionSettings> GetSettingsAsync();
    Task<bool> SaveSettingsAsync(SettingsViewModel settings);
    Task<bool> TestConnectionAsync(SettingsViewModel settings);
}

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SettingsService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;

    public SettingsService(AppDbContext context, ILogger<SettingsService> logger, ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configuration = configuration;
    }

    public async Task<AdoConnectionSettings> GetSettingsAsync()
    {
        var userSettings = await _context.UserSettings.FirstOrDefaultAsync();
        
        if (userSettings == null)
        {
            // Fallback to appsettings.json configuration
            _logger.LogInformation("No database settings found, falling back to appsettings.json");
            var configSettings = new AdoConnectionSettings();
            _configuration.GetSection("AdoConnectionSettings").Bind(configSettings);
            return configSettings;
        }

        return new AdoConnectionSettings
        {
            OrganizationUrl = userSettings.OrganizationUrl,
            PersonalAccessToken = userSettings.PersonalAccessToken,
            DefaultClonePath = userSettings.DefaultClonePath,
            AuthType = userSettings.AuthType
        };
    }

    public async Task<bool> SaveSettingsAsync(SettingsViewModel settings)
    {
        try
        {
            _logger.LogInformation("Starting to save settings...");
            
            var userSettings = await _context.UserSettings.FirstOrDefaultAsync();
            
            if (userSettings == null)
            {
                _logger.LogInformation("No existing settings found, creating new UserSettings record");
                userSettings = new UserSettings
                {
                    CreatedAt = DateTime.UtcNow
                };
                _context.UserSettings.Add(userSettings);
            }
            else
            {
                _logger.LogInformation("Updating existing UserSettings record with ID: {SettingsId}", userSettings.Id);
            }

            userSettings.OrganizationUrl = settings.OrganizationUrl;
            userSettings.PersonalAccessToken = settings.PersonalAccessToken;
            userSettings.DefaultClonePath = settings.DefaultClonePath;
            userSettings.AuthType = settings.AuthType;
            userSettings.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("Saving settings to database - Organization: {OrgUrl}, Auth Type: {AuthType}", 
                settings.OrganizationUrl, settings.AuthType);

            var changeCount = await _context.SaveChangesAsync();
            _logger.LogInformation("Settings saved successfully. {ChangeCount} changes made to database", changeCount);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings: {ErrorMessage}", ex.Message);
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(SettingsViewModel settings)
    {
        try
        {
            _logger.LogInformation("Testing connection with provided settings...");
            
            // Validate basic settings
            if (string.IsNullOrWhiteSpace(settings.OrganizationUrl) || 
                string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
            {
                _logger.LogWarning("Missing required connection settings");
                return false;
            }

            // Validate URL format
            if (!Uri.TryCreate(settings.OrganizationUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("Invalid Organization URL format");
                return false;
            }

            // Create connection and test
            var credentials = new VssBasicCredential(string.Empty, settings.PersonalAccessToken);
            using var connection = new VssConnection(uri, credentials);
            
            // Try to get the connection's organization info as a basic connectivity test
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var projects = await projectClient.GetProjects(top: 1); // Just get 1 project to test connectivity
            
            _logger.LogInformation("Connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
            return false;
        }
    }
}
