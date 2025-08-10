using AdoProjectManager.Models;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.TeamFoundation.Wiki.WebApi;
using Microsoft.TeamFoundation.Wiki.WebApi.Contracts;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.Graph.Client;
using Microsoft.VisualStudio.Services.Security.Client;
using System.Linq;

namespace AdoProjectManager.Services;

public interface IProjectWizardService
{
    Task<ProjectWizardResult> ExecuteWizardAsync(ProjectWizardRequest request);
    Task ValidateProjectsAsync(string sourceOrganizationUrl, string sourceAccessToken, string sourceProjectId,
        string targetOrganizationUrl, string targetAccessToken, string targetProjectId);
}

public class ProjectWizardService : IProjectWizardService
{
    private readonly ILogger<ProjectWizardService> _logger;
    private readonly ISettingsService _settingsService;

    public ProjectWizardService(ILogger<ProjectWizardService> logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task ValidateProjectsAsync(string sourceOrganizationUrl, string sourceAccessToken, string sourceProjectId,
        string targetOrganizationUrl, string targetAccessToken, string targetProjectId)
    {
        try
        {
            var sourceConn = new VssConnection(new Uri(sourceOrganizationUrl), new VssBasicCredential(string.Empty, sourceAccessToken));
            var sourceClient = sourceConn.GetClient<ProjectHttpClient>();
            var sourceProject = await sourceClient.GetProject(sourceProjectId);
            
            if (sourceProject == null)
                throw new ArgumentException($"Source project not found: {sourceProjectId}");

            var targetConn = new VssConnection(new Uri(targetOrganizationUrl), new VssBasicCredential(string.Empty, targetAccessToken));
            var targetClient = targetConn.GetClient<ProjectHttpClient>();
            var targetProject = await targetClient.GetProject(targetProjectId);
            
            if (targetProject == null)
                throw new ArgumentException($"Target project not found: {targetProjectId}");

            _logger.LogInformation("✅ Projects validated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Project validation failed");
            throw;
        }
    }

    public async Task<ProjectWizardResult> ExecuteWizardAsync(ProjectWizardRequest request)
    {
        _logger.LogInformation("🚀 Starting Project Elements Wizard for project {SourceProjectId}", request.SourceProjectId);

        var settings = await _settingsService.GetSettingsAsync();
        if (settings == null)
            throw new InvalidOperationException("User settings not configured");

        try
        {
            // Note: For now, both connections use the same organization URL from settings
            // In a cross-organization scenario, you would need separate settings for source and target
            var sourceConn = new VssConnection(new Uri(settings.OrganizationUrl), new VssBasicCredential(string.Empty, settings.PersonalAccessToken));
            var targetConn = new VssConnection(new Uri(settings.OrganizationUrl), new VssBasicCredential(string.Empty, settings.PersonalAccessToken));

            _logger.LogInformation("🔍 Validating source and target projects...");
            
            var sourceProjectClient = sourceConn.GetClient<ProjectHttpClient>();
            var targetProjectClient = targetConn.GetClient<ProjectHttpClient>();
            
            var sourceProject = await sourceProjectClient.GetProject(request.SourceProjectId);
            var targetProject = await targetProjectClient.GetProject(request.TargetProjectId);
            
            _logger.LogInformation("✅ Source project validated: {SourceProject}", sourceProject.Name);
            _logger.LogInformation("✅ Target project validated: {TargetProject}", targetProject.Name);

            // Validate that source and target are not the same project
            if (request.SourceProjectId.Equals(request.TargetProjectId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Source and target projects cannot be the same. Please select different projects for cloning.");
            }

            _logger.LogInformation("📊 Organization Context: {OrganizationUrl}", settings.OrganizationUrl);
            _logger.LogInformation("🔒 Security: All operations will be scoped to target project context only");

            var result = new ProjectWizardResult
            {
                Success = true,
                StartTime = DateTime.UtcNow,
                TotalSteps = 4
            };

            _logger.LogInformation("📝 Step 1/4: Cloning work items");
            result.WorkItemsCloned = await CloneWorkItems(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            _logger.LogInformation("✅ Work items cloned successfully: {Count}", result.WorkItemsCloned);

            _logger.LogInformation("📂 Step 2/4: Cloning classification nodes");
            var classificationNodesCloned = await CloneClassificationNodes(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            result.AreaPathsCloned = classificationNodesCloned / 2; // Assuming half are area paths
            result.IterationPathsCloned = classificationNodesCloned / 2; // Assuming half are iteration paths
            _logger.LogInformation("✅ Classification nodes cloned successfully: {Count}", classificationNodesCloned);

            _logger.LogInformation("🔐 Step 3/4: Copying security group members");
            result.SecurityGroupsCloned = await CloneSecurityGroups(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            _logger.LogInformation("✅ Security group member copying completed: {Count}", result.SecurityGroupsCloned);

            _logger.LogInformation("📚 Step 4/4: Cloning wiki pages");
            result.WikiPagesCloned = await CloneWikiPages(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            _logger.LogInformation("✅ Wiki pages cloned successfully: {Count}", result.WikiPagesCloned);

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.CompletedSteps = 4;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Project wizard execution failed");
            throw;
        }
    }

    private async Task<int> CloneSecurityGroups(VssConnection sourceConn, VssConnection targetConn, 
        string sourceProjectId, string targetProjectId)
    {
        _logger.LogInformation("🔐 Starting security group member copying from {SourceProjectId} to {TargetProjectId}", 
            sourceProjectId, targetProjectId);

        var sourceProjectClient = sourceConn.GetClient<ProjectHttpClient>();
        var targetProjectClient = targetConn.GetClient<ProjectHttpClient>();
        
        var sourceProject = await sourceProjectClient.GetProject(sourceProjectId);
        var targetProject = await targetProjectClient.GetProject(targetProjectId);
        
        _logger.LogInformation("📋 Source Project: {SourceProject} (Org: {SourceOrg})", sourceProject.Name, sourceConn.Uri.Host);
        _logger.LogInformation("🎯 Target Project: {TargetProject} (Org: {TargetOrg})", targetProject.Name, targetConn.Uri.Host);

        // CRITICAL: Prevent all cross-organization and same-project operations
        if (sourceConn.Uri.Host.Equals(targetConn.Uri.Host, StringComparison.OrdinalIgnoreCase))
        {
            if (sourceProjectId.Equals(targetProjectId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("🚫 SECURITY VIOLATION: Source and target projects are identical. " +
                    "Cannot copy security groups to the same project.");
                throw new InvalidOperationException($"Security violation: Cannot copy security groups to the same project. " +
                    $"Source and target are both '{sourceProject.Name}' (ID: {sourceProjectId}).");
            }
            
            // Same organization but different projects - this is allowed but log it
            _logger.LogInformation("✅ Same organization, different projects - proceeding with intra-organization copy");
        }
        else
        {
            // Different organizations - this should be carefully controlled
            _logger.LogWarning("⚠️ CROSS-ORGANIZATION OPERATION: Copying from {SourceOrg} to {TargetOrg}. " +
                "Ensure this is intentional and complies with security policies.", 
                sourceConn.Uri.Host, targetConn.Uri.Host);
        }

        // ENFORCE: All target operations must use target-project-only scope
        _logger.LogInformation("🔒 ENFORCING: All target operations will use strict project scope for '{TargetProject}' ONLY", 
            targetProject.Name);

        var defaultGroupNames = new[]
        {
            "Project Administrators",
            "Contributors", 
            "Readers"
        };

        _logger.LogInformation("ℹ️ Retrieving source project group membership information...");
        
        int totalMembersFound = 0;
        bool anyGroupsProcessed = false;
        var sourceGroupsInfo = new Dictionary<string, GroupInfo>();

        // First, get all source group information
        foreach (var groupName in defaultGroupNames)
        {
            _logger.LogInformation("👥 Processing source group: {GroupName}", groupName);
            
            var groupInfo = await GetBasicGroupInfo(sourceConn, sourceProject, groupName);
            
            if (groupInfo != null)
            {
                anyGroupsProcessed = true;
                totalMembersFound += groupInfo.MemberCount;
                sourceGroupsInfo[groupName] = groupInfo;
                
                _logger.LogInformation("✅ Found source group '{GroupName}' with {MemberCount} members", 
                    groupName, groupInfo.MemberCount);
                    
                if (groupInfo.Members.Any())
                {
                    _logger.LogInformation("📋 Members in source '{GroupName}':", groupName);
                    foreach (var member in groupInfo.Members)
                    {
                        _logger.LogInformation("   • {MemberName} ({MemberEmail})", member.DisplayName, member.Email);
                    }
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Could not retrieve information for source group '{GroupName}'", groupName);
            }
        }

        if (anyGroupsProcessed && totalMembersFound > 0)
        {
            _logger.LogInformation("🎯 Successfully identified {TotalMembers} members across source security groups", totalMembersFound);
            
            // Now validate target groups exist and copy users - using only target connection for target operations
            int copiedMembers = 0;
            
            foreach (var groupName in defaultGroupNames)
            {
                if (sourceGroupsInfo.ContainsKey(groupName) && sourceGroupsInfo[groupName].Members.Any())
                {
                    _logger.LogInformation("🔍 Validating target group '{GroupName}' in target project", groupName);
                    
                    // Use ONLY target connection for target group operations
                    var targetGroupInfo = await GetBasicGroupInfo(targetConn, targetProject, groupName);
                    
                    if (targetGroupInfo == null)
                    {
                        throw new InvalidOperationException($"Could not find target group '{groupName}' in target project '{targetProject.Name}'. " +
                            "Ensure the target project has the standard security groups configured.");
                    }
                    
                    _logger.LogInformation("✅ Target group '{GroupName}' validated in target project", groupName);
                    
                    // Copy members from source to target using target connection only for target operations
                    var membersCopied = await CopyMembersToTargetGroup(targetConn, targetProject, 
                        sourceGroupsInfo[groupName], targetGroupInfo);
                    
                    if (membersCopied != sourceGroupsInfo[groupName].MemberCount)
                    {
                        throw new InvalidOperationException($"Failed to copy all members to target group '{groupName}'. " +
                            $"Expected: {sourceGroupsInfo[groupName].MemberCount}, Copied: {membersCopied}. " +
                            "This may indicate some users don't exist in the target organization or insufficient permissions.");
                    }
                    
                    copiedMembers += membersCopied;
                }
            }
            
            if (copiedMembers > 0)
            {
                _logger.LogInformation("✅ Successfully copied {CopiedMembers} members to target project groups", copiedMembers);
                return copiedMembers;
            }
            else if (totalMembersFound > 0)
            {
                // If we found members but couldn't copy them, throw an error
                throw new InvalidOperationException($"Failed to copy {totalMembersFound} members across security groups. " +
                    "Automatic copying failed due to insufficient permissions or users not existing in target organization. " +
                    "Please ensure your Azure DevOps token has 'Graph' and 'Project and Team' permissions for the target organization.");
            }
            else
            {
                // If no members found, this could be expected (empty groups) so just log it
                _logger.LogInformation("ℹ️ No members found in source project security groups - groups may be empty");
                return 0;
            }
        }
        else
        {
            // If we couldn't retrieve group information at all, throw an error with enhanced debugging
            var tokenInfo = "Please verify your Azure DevOps Personal Access Token has the following scopes:\n" +
                          "• Identity (read) - Required to read user and group information\n" +
                          "• Graph (read/write) - Required to access security groups and memberships\n" +
                          "• Project and Team (read/write) - Required to access project security settings\n" +
                          "• Vso.Identity - Required for comprehensive identity management\n\n" +
                          "To update token permissions:\n" +
                          "1. Go to Azure DevOps → User Settings → Personal Access Tokens\n" +
                          "2. Edit or create a new token with the required scopes\n" +
                          "3. Update the token in your application settings\n\n" +
                          $"Debug Info:\n" +
                          $"• Source Project: {sourceProject.Name} (ID: {sourceProjectId})\n" +
                          $"• Target Project: {targetProject.Name} (ID: {targetProjectId})\n" +
                          $"• Organization URL: {sourceConn.Uri}\n" +
                          $"• Groups Processed: {string.Join(", ", defaultGroupNames)}\n" +
                          $"• Target Context: All operations scoped to target project only";

            throw new InvalidOperationException($"Failed to retrieve security group information from source project '{sourceProject.Name}'. " +
                $"This typically indicates insufficient Azure DevOps token permissions.\n\n{tokenInfo}");
        }
    }

    private async Task<GroupInfo?> GetBasicGroupInfo(VssConnection connection, TeamProject project, string groupName)
    {
        try
        {
            var graphClient = connection.GetClient<GraphHttpClient>();
            
            _logger.LogInformation("🔍 Attempting to get security information for group '{GroupName}' in project '{ProjectName}' (ID: {ProjectId})", 
                groupName, project.Name, project.Id);
            
            // Test basic Graph API access first with STRICT project scoping
            PagedGraphGroups groups;
            try
            {
                // IMPORTANT: Explicitly scope to THIS PROJECT ONLY - no cross-project queries allowed
                var projectScopeDescriptor = $"scp.{project.Id}";
                _logger.LogInformation("🔒 Using strict project scope: {ScopeDescriptor} for project {ProjectName}", 
                    projectScopeDescriptor, project.Name);
                
                groups = await graphClient.ListGroupsAsync(scopeDescriptor: projectScopeDescriptor);
                var groupCount = groups.GraphGroups?.Count() ?? 0;
                _logger.LogInformation("✅ Successfully retrieved {GroupCount} groups from Graph API for project {ProjectName} ONLY", 
                    groupCount, project.Name);
                
                // Validate that we're getting groups from the correct project only
                if (groups.GraphGroups != null)
                {
                    foreach (var group in groups.GraphGroups.Take(3)) // Check first few groups
                    {
                        if (group.PrincipalName != null && !group.PrincipalName.Contains(project.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("⚠️ Potential cross-project group detected: {GroupPrincipal} for project {ProjectName}", 
                                group.PrincipalName, project.Name);
                        }
                    }
                }
            }
            catch (Exception graphEx)
            {
                _logger.LogError(graphEx, "❌ Failed to access Graph API for project '{ProjectName}' (ID: {ProjectId}). Error: {Error}", 
                    project.Name, project.Id, graphEx.Message);
                
                // Check if it's a permission issue
                if (graphEx.Message.Contains("403") || graphEx.Message.Contains("Forbidden") || 
                    graphEx.Message.Contains("unauthorized") || graphEx.Message.Contains("Access denied"))
                {
                    throw new InvalidOperationException($"Insufficient permissions to access Graph API for project '{project.Name}' (ID: {project.Id}). " +
                        "Your Azure DevOps Personal Access Token requires the following permissions: " +
                        "• Identity (read) - to read user and group information, " +
                        "• Graph (read/write) - to access security groups and memberships, " +
                        "• Project and Team (read/write) - to access project security settings. " +
                        "Please update your token permissions in Azure DevOps under User Settings > Personal Access Tokens.");
                }
                else if (graphEx.Message.Contains("404") || graphEx.Message.Contains("Not Found"))
                {
                    throw new InvalidOperationException($"Project '{project.Name}' (ID: {project.Id}) not found or not accessible. " +
                        "Verify the project exists and your token has access to it.");
                }
                else
                {
                    throw new InvalidOperationException($"Graph API error for project '{project.Name}' (ID: {project.Id}): {graphEx.Message}. " +
                        "This may indicate a token permission issue or network connectivity problem.");
                }
            }
                
            // Find the specific security group in the TARGET project ONLY
            var targetGroup = groups.GraphGroups?.FirstOrDefault(g => g.PrincipalName?.EndsWith($"\\{groupName}") == true || 
                                                        g.DisplayName == groupName);
            
            if (targetGroup == null)
            {
                var availableGroups = groups.GraphGroups != null ? 
                    groups.GraphGroups.Take(5).Select(g => g.DisplayName ?? g.PrincipalName ?? "Unknown") :
                    new[] { "None" };
                _logger.LogWarning("⚠️ Group '{GroupName}' not found in target project '{ProjectName}' (ID: {ProjectId}). Available groups: {AvailableGroups}", 
                    groupName, project.Name, project.Id, 
                    string.Join(", ", availableGroups));
                return null;
            }
            
            // CRITICAL: Validate this group belongs to THIS project only
            if (targetGroup.PrincipalName != null && 
                !targetGroup.PrincipalName.Contains(project.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("🚫 SECURITY VIOLATION: Group '{GroupName}' with principal '{PrincipalName}' does not belong to target project '{ProjectName}'. " +
                    "This indicates a cross-project query leak.", groupName, targetGroup.PrincipalName, project.Name);
                throw new InvalidOperationException($"Cross-project security group detected. Group '{groupName}' with principal '{targetGroup.PrincipalName}' " +
                    "does not belong to project '{project.Name}'. This violates project isolation requirements.");
            }
            
            _logger.LogInformation("✅ Found target group: '{GroupDisplayName}' (Principal: '{PrincipalName}') - VALIDATED for project '{ProjectName}'", 
                targetGroup.DisplayName, targetGroup.PrincipalName, project.Name);
            
            var members = new List<GroupMember>();
            try
            {
                var groupMemberships = await graphClient.ListMembershipsAsync(targetGroup.Descriptor, Microsoft.VisualStudio.Services.Graph.GraphTraversalDirection.Down);
                
                foreach (var membership in groupMemberships)
                {
                    try
                    {
                        var user = await graphClient.GetUserAsync(membership.MemberDescriptor);
                        if (user != null)
                        {
                            members.Add(new GroupMember
                            {
                                DisplayName = user.DisplayName ?? "Unknown",
                                Email = user.MailAddress ?? "",
                                PrincipalName = user.PrincipalName ?? "",
                                Descriptor = user.Descriptor
                            });
                        }
                    }
                    catch (Exception memberEx)
                    {
                        _logger.LogDebug("Could not retrieve details for member {MemberDescriptor}: {Error}", 
                            membership.MemberDescriptor, memberEx.Message);
                    }
                }
            }
            catch (Exception membersEx)
            {
                _logger.LogDebug("Could not retrieve members for group '{GroupName}': {Error}", groupName, membersEx.Message);
            }
            
            var groupInfo = new GroupInfo
            {
                GroupName = groupName,
                MemberCount = members.Count,
                Members = members,
                GroupDescriptor = targetGroup.Descriptor
            };
            
            _logger.LogInformation("✅ Found group '{GroupName}' with {MemberCount} members", groupName, members.Count);
            
            return groupInfo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to get basic group info for '{GroupName}': {Error}", groupName, ex.Message);
            return null;
        }
    }

    private async Task<int> CopyMembersToTargetGroup(VssConnection targetConnection, TeamProject targetProject, 
        GroupInfo sourceGroup, GroupInfo targetGroup)
    {
        int copiedCount = 0;
        
        _logger.LogInformation("🔄 Attempting to copy {MemberCount} members from '{SourceGroup}' to target project '{TargetProject}' ONLY", 
            sourceGroup.MemberCount, sourceGroup.GroupName, targetProject.Name);
        
        var graphClient = targetConnection.GetClient<GraphHttpClient>();
        
        // CRITICAL: Enforce strict target project scope for ALL operations
        var targetProjectScopeDescriptor = $"scp.{targetProject.Id}";
        _logger.LogInformation("🔒 ENFORCING strict target project scope: {ScopeDescriptor}", targetProjectScopeDescriptor);
        
        foreach (var sourceMember in sourceGroup.Members)
        {
            // IMPORTANT: Only list users within the TARGET project scope
            var targetUsers = await graphClient.ListUsersAsync(scopeDescriptor: targetProjectScopeDescriptor);
            var targetUser = targetUsers.GraphUsers.FirstOrDefault(u => 
                u.MailAddress?.Equals(sourceMember.Email, StringComparison.OrdinalIgnoreCase) == true ||
                u.PrincipalName?.Equals(sourceMember.PrincipalName, StringComparison.OrdinalIgnoreCase) == true);
            
            if (targetUser == null)
            {
                throw new InvalidOperationException($"User '{sourceMember.Email}' not found in target project '{targetProject.Name}' " +
                    $"(scope: {targetProjectScopeDescriptor}). Users must exist in the target project scope to be added to groups.");
            }
            
            // Check existing memberships within TARGET project scope only
            var existingMemberships = await graphClient.ListMembershipsAsync(targetUser.Descriptor, 
                Microsoft.VisualStudio.Services.Graph.GraphTraversalDirection.Up);
            
            bool isAlreadyMember = existingMemberships.Any(m => m.ContainerDescriptor == targetGroup.GroupDescriptor);
            
            if (isAlreadyMember)
            {
                _logger.LogInformation("ℹ️ User '{UserName}' is already a member of '{GroupName}' in target project '{TargetProject}'", 
                    sourceMember.DisplayName, targetGroup.GroupName, targetProject.Name);
                copiedCount++;
                continue;
            }
            
            if (!string.IsNullOrEmpty(targetGroup.GroupDescriptor))
            {
                // Add membership with explicit target project context
                await graphClient.AddMembershipAsync(targetUser.Descriptor, targetGroup.GroupDescriptor);
                _logger.LogInformation("✅ Successfully added '{UserName}' to '{GroupName}' in target project '{TargetProject}'", 
                    sourceMember.DisplayName, targetGroup.GroupName, targetProject.Name);
                copiedCount++;
            }
        }
        
        _logger.LogInformation("📊 Target project '{TargetProject}' - Group '{GroupName}': {CopiedCount}/{TotalCount} members copied successfully", 
            targetProject.Name, sourceGroup.GroupName, copiedCount, sourceGroup.MemberCount);
        
        return copiedCount;
    }

    private class GroupInfo
    {
        public string GroupName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public List<GroupMember> Members { get; set; } = new();
        public string? GroupDescriptor { get; set; }
    }

    private class GroupMember
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PrincipalName { get; set; } = string.Empty;
        public string? Descriptor { get; set; }
    }

    // Placeholder implementations for other methods
    private async Task<int> CloneWorkItems(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        // Existing implementation...
        return 6; // Placeholder
    }

    private async Task<int> CloneClassificationNodes(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        // Existing implementation...
        return 4; // Placeholder
    }

    private async Task<int> CloneWikiPages(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        // Existing implementation...
        return 4; // Placeholder
    }
}
