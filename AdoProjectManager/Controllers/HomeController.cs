using Microsoft.AspNetCore.Mvc;
using AdoProjectManager.Models;
using AdoProjectManager.Services;

namespace AdoProjectManager.Controllers;

public class HomeController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly IAdoService _adoService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ISettingsService settingsService, IAdoService adoService, ILogger<HomeController> logger)
    {
        _settingsService = settingsService;
        _adoService = adoService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            _logger.LogInformation("Index action called, retrieving settings...");
            var settings = await _settingsService.GetSettingsAsync();
            _logger.LogInformation("Settings retrieved: OrgUrl={OrgUrl}, HasPAT={HasPAT}", 
                settings.OrganizationUrl, !string.IsNullOrEmpty(settings.PersonalAccessToken));
            
            if (string.IsNullOrEmpty(settings.OrganizationUrl) || string.IsNullOrEmpty(settings.PersonalAccessToken))
            {
                _logger.LogWarning("Configuration missing - OrgUrl: '{OrgUrl}', PAT: {HasPAT}", 
                    settings.OrganizationUrl, !string.IsNullOrEmpty(settings.PersonalAccessToken));
                ViewBag.Error = "⚙️ Azure DevOps connection not configured. Please set up your organization URL and Personal Access Token.";
                ViewBag.ShowSettingsLink = true;
                ViewBag.IsConfigurationMissing = true;
                return View(new List<AdoProject>());
            }

            _logger.LogInformation("Testing connection...");
            var connectionTest = await _adoService.TestConnection();
            _logger.LogInformation("TestConnection() returned: {Result}", connectionTest);
            
            if (!connectionTest)
            {
                _logger.LogError("Connection test failed!");
                ViewBag.Error = "❌ Unable to connect to Azure DevOps. Please verify your organization URL and authentication credentials.";
                ViewBag.ShowSettingsLink = true;
                ViewBag.IsConnectionError = true;
                return View(new List<AdoProject>());
            }

            _logger.LogInformation("Connection test passed, calling GetProjectsAsync()...");
            var projects = await _adoService.GetProjectsAsync();
            _logger.LogInformation("GetProjectsAsync() returned {ProjectCount} projects", projects.Count);
            ViewBag.SuccessMessage = $"✅ Successfully connected! Found {projects.Count} project(s).";
            return View(projects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading projects");
            ViewBag.Error = "An error occurred while loading projects: " + ex.Message;
            ViewBag.ShowSettingsLink = true;
            return View(new List<AdoProject>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> CloneRepository([FromBody] CloneRequest request)
    {
        try
        {
            var result = await _adoService.CloneRepositoryAsync(request);
            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning repository");
            return Json(new CloneResult
            {
                Success = false,
                Message = "An error occurred while cloning the repository",
                Error = ex.Message
            });
        }
    }

    public async Task<IActionResult> Settings()
    {
        var settings = await _settingsService.GetSettingsAsync();
        var viewModel = new SettingsViewModel
        {
            OrganizationUrl = settings.OrganizationUrl,
            PersonalAccessToken = settings.PersonalAccessToken,
            DefaultClonePath = settings.DefaultClonePath,
            AuthType = settings.AuthType
        };
        
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> Settings(SettingsViewModel model)
    {
        _logger.LogInformation("Settings POST received with Organization URL: {OrgUrl}", model.OrganizationUrl);
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Model validation failed. Errors: {Errors}", 
                string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            return View(model);
        }

        _logger.LogInformation("Model validation passed. Processing save request...");

        _logger.LogInformation("Calling SettingsService.SaveSettingsAsync...");
        var saved = await _settingsService.SaveSettingsAsync(model);
        
        if (saved)
        {
            _logger.LogInformation("Settings saved successfully!");
            TempData["SuccessMessage"] = "Settings saved successfully!";
            return RedirectToAction(nameof(Settings));
        }
        else
        {
            _logger.LogError("Settings save failed - SettingsService returned false");
            ModelState.AddModelError("", "Failed to save settings. Please try again.");
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> TestConnection([FromBody] SettingsViewModel model)
    {
        try
        {
            var result = await _settingsService.TestConnectionAsync(model);
            return Json(new { success = result, message = result ? "Connection successful!" : "Connection failed. Please check your settings." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection");
            return Json(new { success = false, message = "Error testing connection: " + ex.Message });
        }
    }
}
