using Microsoft.AspNetCore.SignalR;
using AdoProjectManager.Hubs;

namespace AdoProjectManager.Services
{
    public interface ICloneProgressService
    {
        Task SendProgressUpdate(string cloneOperationId, int percentage, string message);
        Task SendStepUpdate(string cloneOperationId, string stepName, string status, string message);
        Task SendLogMessage(string cloneOperationId, string logLevel, string message, DateTime timestamp);
        Task SendCloneComplete(string cloneOperationId, bool success, object? result = null, string? error = null);
    }

    public class CloneProgressService : ICloneProgressService
    {
        private readonly IHubContext<CloneProgressHub> _hubContext;
        private readonly ILogger<CloneProgressService> _logger;

        public CloneProgressService(IHubContext<CloneProgressHub> hubContext, ILogger<CloneProgressService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task SendProgressUpdate(string cloneOperationId, int percentage, string message)
        {
            try
            {
                await _hubContext.Clients.Group($"clone_{cloneOperationId}")
                    .SendAsync("ProgressUpdate", new
                    {
                        Percentage = percentage,
                        Message = message,
                        Timestamp = DateTime.Now.ToString("O") // ISO 8601 format for JavaScript
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send progress update for clone operation {CloneOperationId}", cloneOperationId);
            }
        }

        public async Task SendStepUpdate(string cloneOperationId, string stepName, string status, string message)
        {
            try
            {
                await _hubContext.Clients.Group($"clone_{cloneOperationId}")
                    .SendAsync("StepUpdate", new
                    {
                        StepName = stepName,
                        Status = status, // "started", "completed", "failed"
                        Message = message,
                        Timestamp = DateTime.Now.ToString("O") // ISO 8601 format for JavaScript
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send step update for clone operation {CloneOperationId}", cloneOperationId);
            }
        }

        public async Task SendLogMessage(string cloneOperationId, string logLevel, string message, DateTime timestamp)
        {
            try
            {
                await _hubContext.Clients.Group($"clone_{cloneOperationId}")
                    .SendAsync("LogMessage", new
                    {
                        LogLevel = logLevel, // "info", "warning", "error", "success"
                        Message = message,
                        Timestamp = timestamp.ToString("O") // ISO 8601 format for JavaScript
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send log message for clone operation {CloneOperationId}", cloneOperationId);
            }
        }

        public async Task SendCloneComplete(string cloneOperationId, bool success, object? result = null, string? error = null)
        {
            try
            {
                await _hubContext.Clients.Group($"clone_{cloneOperationId}")
                    .SendAsync("CloneComplete", new
                    {
                        Success = success,
                        Result = result,
                        Error = error,
                        Timestamp = DateTime.Now.ToString("O") // ISO 8601 format for JavaScript
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send clone complete notification for clone operation {CloneOperationId}", cloneOperationId);
            }
        }
    }
}
