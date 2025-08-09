using Microsoft.AspNetCore.SignalR;

namespace AdoProjectManager.Hubs
{
    public class CloneProgressHub : Hub
    {
        /// <summary>
        /// Join a specific clone operation group to receive updates for that operation
        /// </summary>
        /// <param name="cloneOperationId">Unique identifier for the clone operation</param>
        public async Task JoinCloneGroup(string cloneOperationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"clone_{cloneOperationId}");
        }

        /// <summary>
        /// Leave a specific clone operation group
        /// </summary>
        /// <param name="cloneOperationId">Unique identifier for the clone operation</param>
        public async Task LeaveCloneGroup(string cloneOperationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"clone_{cloneOperationId}");
        }
    }
}
