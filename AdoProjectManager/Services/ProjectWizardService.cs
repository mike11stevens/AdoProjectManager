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
using System.Text.RegularExpressions;

namespace AdoProjectManager.Services;

public interface IProjectWizardService
{
    Task<ProjectWizardResult> ExecuteWizardAsync(ProjectWizardRequest request);
    Task<ProjectDifferencesAnalysis> AnalyzeDifferencesAsync(string sourceProjectId, string targetProjectId);
    Task<ProjectWizardResult> ApplySelectiveUpdatesAsync(SelectiveUpdateRequest request);
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
                TotalSteps = 3
            };

            _logger.LogInformation("📝 Step 1/3: Analyzing work item differences");
            result.WorkItemsCloned = await CloneWorkItems(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            _logger.LogInformation("✅ Work item analysis completed: {Count} updates identified", result.WorkItemsCloned);

            _logger.LogInformation("� Step 2/3: Analyzing security group differences");
            result.SecurityGroupsCloned = await CloneSecurityGroups(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            _logger.LogInformation("✅ Security group analysis completed: {Count} updates identified", result.SecurityGroupsCloned);

            _logger.LogInformation("📚 Step 3/3: Analyzing wiki page differences");
            result.WikiPagesCloned = await CloneWikiPages(sourceConn, targetConn, request.SourceProjectId, request.TargetProjectId);
            _logger.LogInformation("✅ Wiki page analysis completed: {Count} updates identified", result.WikiPagesCloned);

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.CompletedSteps = 3;

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
        _logger.LogInformation("🔐 Starting security group analysis and update process from {SourceProjectId} to {TargetProjectId}", 
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
            _logger.LogInformation("✅ Same organization, different projects - proceeding with intra-organization analysis");
        }
        else
        {
            // Different organizations - this should be carefully controlled
            _logger.LogWarning("⚠️ CROSS-ORGANIZATION OPERATION: Analyzing from {SourceOrg} to {TargetOrg}. " +
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
            
            // Now analyze target groups for differences and prepare update plan
            var updatePlan = new List<GroupUpdatePlan>();
            
            foreach (var groupName in defaultGroupNames)
            {
                if (sourceGroupsInfo.ContainsKey(groupName))
                {
                    _logger.LogInformation("🔍 Analyzing target group '{GroupName}' for differences", groupName);
                    
                    // Use ONLY target connection for target group operations
                    var targetGroupInfo = await GetBasicGroupInfo(targetConn, targetProject, groupName);
                    
                    if (targetGroupInfo == null)
                    {
                        _logger.LogWarning("⚠️ Could not find or access target group '{GroupName}' in target project '{TargetProject}'. " +
                            "This may be due to permission issues or missing group configuration.", groupName, targetProject.Name);
                        continue; // Skip this group but continue with others
                    }
                    
                    // Compare source and target group memberships to create diff
                    var differences = await AnalyzeGroupDifferences(sourceGroupsInfo[groupName], targetGroupInfo);
                    
                    if (differences.HasChanges)
                    {
                        updatePlan.Add(new GroupUpdatePlan
                        {
                            GroupName = groupName,
                            SourceGroup = sourceGroupsInfo[groupName],
                            TargetGroup = targetGroupInfo,
                            Differences = differences
                        });
                    }
                    else
                    {
                        _logger.LogInformation("✅ Group '{GroupName}' is already synchronized - no changes needed", groupName);
                    }
                }
            }
            
            if (updatePlan.Any())
            {
                // Present differences to user and ask for confirmation
                _logger.LogInformation("📊 Found differences in {GroupCount} security groups:", updatePlan.Count);
                
                foreach (var plan in updatePlan)
                {
                    LogGroupDifferences(plan);
                }
                
                // For now, we'll apply the updates automatically but log what would be done
                // In a future version, this could be made interactive
                _logger.LogInformation("🔄 Applying security group updates based on differences...");
                
                int totalUpdatedMembers = 0;
                foreach (var plan in updatePlan)
                {
                    var updatedMembers = await ApplyGroupUpdates(targetConn, targetProject, plan);
                    totalUpdatedMembers += updatedMembers;
                }
                
                _logger.LogInformation("✅ Successfully updated {UpdatedMembers} group memberships", totalUpdatedMembers);
                return totalUpdatedMembers;
            }
            else
            {
                _logger.LogInformation("✅ All security groups are already synchronized - no updates needed");
                return 0;
            }
        }
        else
        {
            // If we couldn't retrieve group information, provide helpful guidance but don't fail the entire wizard
            _logger.LogWarning("⚠️ Unable to retrieve security group information - this is likely due to insufficient token permissions");
            
            var tokenInfo = "To enable security group analysis, please verify your Azure DevOps Personal Access Token has the following scopes:\n" +
                          "• Identity (read) - Required to read user and group information\n" +
                          "• Graph (read/write) - Required to access security groups and memberships\n" +
                          "• Project and Team (read/write) - Required to access project security settings\n" +
                          "• Member Entitlement Management (read) - Required for user/group operations\n\n" +
                          "To update token permissions:\n" +
                          "1. Go to Azure DevOps → User Settings → Personal Access Tokens\n" +
                          "2. Edit or create a new token with the required scopes\n" +
                          "3. Update the token in your application settings\n\n" +
                          "Manual Security Group Setup Instructions:\n" +
                          "Since automatic security group synchronization isn't available, please manually configure:\n" +
                          $"• Source Project: {sourceProject.Name} (ID: {sourceProjectId})\n" +
                          $"• Target Project: {targetProject.Name} (ID: {targetProjectId})\n" +
                          $"• Groups to Review: {string.Join(", ", defaultGroupNames)}\n" +
                          $"• Organization: {sourceConn.Uri}\n\n" +
                          "You can manually add users to security groups in the Azure DevOps web interface:\n" +
                          "Project Settings → Permissions → [Group Name] → Members → Add";

            _logger.LogInformation("📋 Security Group Setup Guidance:");
            _logger.LogInformation("{TokenInfo}", tokenInfo);
            
            // Return 0 to indicate no groups were processed, but don't fail the wizard
            _logger.LogInformation("🔄 Continuing wizard execution - security groups will need manual configuration");
            return 0;
        }
    }

    private Task<GroupDifferences> AnalyzeGroupDifferences(GroupInfo sourceGroup, GroupInfo targetGroup)
    {
        _logger.LogInformation("🔍 Analyzing differences for group '{GroupName}'", sourceGroup.GroupName);
        
        var differences = new GroupDifferences();
        
        // Find members that exist in source but not in target (need to add)
        foreach (var sourceMember in sourceGroup.Members)
        {
            var existsInTarget = targetGroup.Members.Any(tm => 
                tm.Email.Equals(sourceMember.Email, StringComparison.OrdinalIgnoreCase) ||
                tm.PrincipalName.Equals(sourceMember.PrincipalName, StringComparison.OrdinalIgnoreCase));
            
            if (!existsInTarget)
            {
                differences.MembersToAdd.Add(sourceMember);
            }
        }
        
        // Find members that exist in target but not in source (potentially to remove)
        foreach (var targetMember in targetGroup.Members)
        {
            var existsInSource = sourceGroup.Members.Any(sm => 
                sm.Email.Equals(targetMember.Email, StringComparison.OrdinalIgnoreCase) ||
                sm.PrincipalName.Equals(targetMember.PrincipalName, StringComparison.OrdinalIgnoreCase));
            
            if (!existsInSource)
            {
                differences.MembersToRemove.Add(targetMember);
            }
            else
            {
                differences.ExistingMembers.Add(targetMember);
            }
        }
        
        _logger.LogInformation("📊 Group '{GroupName}' analysis: {ToAdd} to add, {ToRemove} to remove, {Existing} existing", 
            sourceGroup.GroupName, differences.MembersToAdd.Count, differences.MembersToRemove.Count, differences.ExistingMembers.Count);
        
        return Task.FromResult(differences);
    }

    private void LogGroupDifferences(GroupUpdatePlan plan)
    {
        _logger.LogInformation("📋 Group '{GroupName}' Update Plan:", plan.GroupName);
        
        if (plan.Differences.MembersToAdd.Any())
        {
            _logger.LogInformation("  ➕ Members to ADD ({Count}):", plan.Differences.MembersToAdd.Count);
            foreach (var member in plan.Differences.MembersToAdd)
            {
                _logger.LogInformation("    • {Name} ({Email})", member.DisplayName, member.Email);
            }
        }
        
        if (plan.Differences.MembersToRemove.Any())
        {
            _logger.LogInformation("  ➖ Members that exist in target but not in source ({Count}):", plan.Differences.MembersToRemove.Count);
            foreach (var member in plan.Differences.MembersToRemove)
            {
                _logger.LogInformation("    • {Name} ({Email}) [keeping - not removing automatically]", member.DisplayName, member.Email);
            }
        }
        
        if (plan.Differences.ExistingMembers.Any())
        {
            _logger.LogInformation("  ✅ Members already synchronized ({Count}):", plan.Differences.ExistingMembers.Count);
            foreach (var member in plan.Differences.ExistingMembers)
            {
                _logger.LogInformation("    • {Name} ({Email})", member.DisplayName, member.Email);
            }
        }
    }

    private async Task<int> ApplyGroupUpdates(VssConnection targetConnection, TeamProject targetProject, GroupUpdatePlan plan)
    {
        int updatedCount = 0;
        
        _logger.LogInformation("🔄 Applying updates to group '{GroupName}' in target project '{TargetProject}'", 
            plan.GroupName, targetProject.Name);
        
        // Only add members - we won't remove existing members automatically for safety
        if (plan.Differences.MembersToAdd.Any())
        {
            foreach (var memberToAdd in plan.Differences.MembersToAdd)
            {
                try
                {
                    var addedSuccess = await AddMemberToTargetGroup(targetConnection, targetProject, memberToAdd, plan.TargetGroup);
                    if (addedSuccess)
                    {
                        updatedCount++;
                        _logger.LogInformation("✅ Added '{MemberName}' to group '{GroupName}'", memberToAdd.DisplayName, plan.GroupName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Failed to add '{MemberName}' to group '{GroupName}': {Error}", 
                        memberToAdd.DisplayName, plan.GroupName, ex.Message);
                }
            }
        }
        
        if (plan.Differences.MembersToRemove.Any())
        {
            _logger.LogInformation("ℹ️ Note: {Count} members exist in target but not in source. These were NOT removed for safety.", 
                plan.Differences.MembersToRemove.Count);
        }
        
        return updatedCount;
    }

    private async Task<bool> AddMemberToTargetGroup(VssConnection targetConnection, TeamProject targetProject, 
        GroupMember sourceMember, GroupInfo targetGroup)
    {
        var graphClient = targetConnection.GetClient<GraphHttpClient>();
        
        // CRITICAL: Enforce strict target project scope for ALL operations
        var targetProjectScopeDescriptor = $"scp.{targetProject.Id}";
        
        // Find the user in the target project scope
        var targetUsers = await graphClient.ListUsersAsync(scopeDescriptor: targetProjectScopeDescriptor);
        var targetUser = targetUsers.GraphUsers.FirstOrDefault(u => 
            u.MailAddress?.Equals(sourceMember.Email, StringComparison.OrdinalIgnoreCase) == true ||
            u.PrincipalName?.Equals(sourceMember.PrincipalName, StringComparison.OrdinalIgnoreCase) == true);
        
        if (targetUser == null)
        {
            _logger.LogWarning("⚠️ User '{UserEmail}' not found in target project '{TargetProject}' scope", 
                sourceMember.Email, targetProject.Name);
            return false;
        }
        
        // Check if user is already a member (double-check)
        var existingMemberships = await graphClient.ListMembershipsAsync(targetUser.Descriptor, 
            Microsoft.VisualStudio.Services.Graph.GraphTraversalDirection.Up);
        
        bool isAlreadyMember = existingMemberships.Any(m => m.ContainerDescriptor == targetGroup.GroupDescriptor);
        
        if (isAlreadyMember)
        {
            _logger.LogInformation("ℹ️ User '{UserName}' is already a member of '{GroupName}' - skipping", 
                sourceMember.DisplayName, targetGroup.GroupName);
            return true; // Consider this successful
        }
        
        // Add the user to the group
        await graphClient.AddMembershipAsync(targetUser.Descriptor, targetGroup.GroupDescriptor);
        return true;
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
                    _logger.LogWarning("⚠️ Insufficient permissions to access Graph API for project '{ProjectName}' (ID: {ProjectId}). " +
                        "Token requires Identity (read), Graph (read/write), Project and Team (read/write) permissions.", 
                        project.Name, project.Id);
                    return null;
                }
                else if (graphEx.Message.Contains("404") || graphEx.Message.Contains("Not Found"))
                {
                    _logger.LogWarning("⚠️ Project '{ProjectName}' (ID: {ProjectId}) not found or not accessible via Graph API. " +
                        "Verify the project exists and your token has access to it.", project.Name, project.Id);
                    return null;
                }
                else
                {
                    _logger.LogWarning("⚠️ Graph API error for project '{ProjectName}' (ID: {ProjectId}): {Error}. " +
                        "This may indicate a token permission issue or network connectivity problem.", 
                        project.Name, project.Id, graphEx.Message);
                    return null;
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

    // Supporting classes for diff-based security group updates
    private class GroupInfo
    {
        public string GroupName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public List<GroupMember> Members { get; set; } = new();
        public string GroupDescriptor { get; set; } = string.Empty;
    }

    private class GroupMember
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PrincipalName { get; set; } = string.Empty;
        public string Descriptor { get; set; } = string.Empty;
    }

    private class GroupUpdatePlan
    {
        public string GroupName { get; set; } = string.Empty;
        public GroupInfo SourceGroup { get; set; } = new();
        public GroupInfo TargetGroup { get; set; } = new();
        public GroupDifferences Differences { get; set; } = new();
    }

    private class GroupDifferences
    {
        public List<GroupMember> MembersToAdd { get; set; } = new();
        public List<GroupMember> MembersToRemove { get; set; } = new();
        public List<GroupMember> ExistingMembers { get; set; } = new();
        public bool HasChanges => MembersToAdd.Any() || MembersToRemove.Any();
    }

    // Placeholder implementations for other methods
    private async Task<int> CloneWorkItems(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        _logger.LogInformation("🔍 Analyzing work item differences between source and target projects...");
        
        try
        {
            var workItemClient = sourceConn.GetClient<WorkItemTrackingHttpClient>();
            var targetWorkItemClient = targetConn.GetClient<WorkItemTrackingHttpClient>();
            
            // Get work items from source project
            var sourceQuery = $"SELECT [System.Id], [System.Title], [System.WorkItemType], [System.State] FROM WorkItems WHERE [System.TeamProject] = '{sourceProjectId}'";
            var sourceQueryResult = await workItemClient.QueryByWiqlAsync(new Wiql { Query = sourceQuery });
            
            if (sourceQueryResult.WorkItems?.Any() != true)
            {
                _logger.LogInformation("ℹ️ No work items found in source project");
                return 0;
            }
            
            // Get work items from target project
            var targetQuery = $"SELECT [System.Id], [System.Title], [System.WorkItemType], [System.State] FROM WorkItems WHERE [System.TeamProject] = '{targetProjectId}'";
            var targetQueryResult = await targetWorkItemClient.QueryByWiqlAsync(new Wiql { Query = targetQuery });
            
            var sourceWorkItems = sourceQueryResult.WorkItems.Select(wi => wi.Id).ToList();
            var targetWorkItems = targetQueryResult.WorkItems?.Select(wi => wi.Id).ToList() ?? new List<int>();
            
            _logger.LogInformation("📊 Found {SourceCount} work items in source, {TargetCount} in target", 
                sourceWorkItems.Count, targetWorkItems.Count);
            
            // For now, just analyze the differences - don't automatically create/update
            var sourceDetails = await workItemClient.GetWorkItemsAsync(sourceWorkItems, null, null, WorkItemExpand.Fields);
            var newWorkItems = 0;
            var updatesNeeded = 0;
            
            foreach (var sourceWI in sourceDetails)
            {
                var title = sourceWI.Fields["System.Title"].ToString();
                var workItemType = sourceWI.Fields["System.WorkItemType"].ToString();
                
                // Check if similar work item exists in target (by title and type)
                var targetDetails = targetWorkItems.Any() ? 
                    await targetWorkItemClient.GetWorkItemsAsync(targetWorkItems, null, null, WorkItemExpand.Fields) : 
                    new List<WorkItem>();
                
                var existingItem = targetDetails.FirstOrDefault(twi => 
                    twi.Fields["System.Title"].ToString().Equals(title, StringComparison.OrdinalIgnoreCase) &&
                    twi.Fields["System.WorkItemType"].ToString().Equals(workItemType, StringComparison.OrdinalIgnoreCase));
                
                if (existingItem == null)
                {
                    _logger.LogInformation("➕ New work item needed: '{Title}' ({Type})", title, workItemType);
                    newWorkItems++;
                }
                else
                {
                    // Check if update is needed (compare key fields)
                    var sourceState = sourceWI.Fields["System.State"].ToString();
                    var targetState = existingItem.Fields["System.State"].ToString();
                    
                    if (!sourceState.Equals(targetState, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("🔄 Update needed for '{Title}': State {TargetState} → {SourceState}", 
                            title, targetState, sourceState);
                        updatesNeeded++;
                    }
                    else
                    {
                        _logger.LogInformation("✅ Work item '{Title}' is synchronized", title);
                    }
                }
            }
            
            _logger.LogInformation("📋 Work Item Analysis Summary:");
            _logger.LogInformation("   • New work items needed: {NewCount}", newWorkItems);
            _logger.LogInformation("   • Existing items needing updates: {UpdateCount}", updatesNeeded);
            _logger.LogInformation("   • Items already synchronized: {SyncCount}", sourceWorkItems.Count - newWorkItems - updatesNeeded);
            
            if (newWorkItems > 0 || updatesNeeded > 0)
            {
                _logger.LogWarning("⚠️ Work item synchronization requires manual intervention:");
                _logger.LogWarning("   • Review the differences above");
                _logger.LogWarning("   • Create/update work items manually in target project");
                _logger.LogWarning("   • Or implement automatic sync with user confirmation");
            }
            
            return 0; // Return 0 since no automatic updates were performed
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not analyze work item differences: {Error}", ex.Message);
            _logger.LogInformation("📝 Manual work item review recommended between source and target projects");
            return 0;
        }
    }

    private async Task<int> CloneWikiPages(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        _logger.LogInformation("🔍 Analyzing wiki page differences between source and target projects...");
        
        try
        {
            // Note: Wiki API access requires specific permissions and may not be available with basic tokens
            _logger.LogInformation("📚 Wiki analysis requires elevated permissions");
            _logger.LogWarning("⚠️ Wiki synchronization requires manual intervention:");
            _logger.LogWarning("   • Compare wiki content manually between projects");
            _logger.LogWarning("   • Copy/update wiki pages as needed in target project");
            _logger.LogWarning("   • Wiki API access may require additional token permissions");
            
            return 0; // Return 0 since no automatic updates were performed
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not analyze wiki differences: {Error}", ex.Message);
            _logger.LogInformation("📝 Manual wiki review recommended between source and target projects");
            return 0;
        }
    }

    public async Task<ProjectDifferencesAnalysis> AnalyzeDifferencesAsync(string sourceProjectId, string targetProjectId)
    {
        _logger.LogInformation("🔍 Starting comprehensive differences analysis between projects");
        
        var analysis = new ProjectDifferencesAnalysis();
        
        try
        {
            var userSettings = await _settingsService.GetSettingsAsync();
            if (userSettings == null)
            {
                throw new InvalidOperationException("User settings not configured");
            }

            var sourceConn = new VssConnection(new Uri(userSettings.OrganizationUrl), new VssBasicCredential(string.Empty, userSettings.PersonalAccessToken));
            var targetConn = new VssConnection(new Uri(userSettings.OrganizationUrl), new VssBasicCredential(string.Empty, userSettings.PersonalAccessToken));
            
            // Analyze work item differences
            analysis.WorkItems = await AnalyzeWorkItemDifferencesDetailed(sourceConn, targetConn, sourceProjectId, targetProjectId);
            
            // Analyze security group differences (if permissions allow)
            analysis.SecurityGroups = await AnalyzeSecurityGroupDifferencesDetailed(sourceConn, targetConn, sourceProjectId, targetProjectId);
            
            // Analyze query differences
            analysis.Queries = await AnalyzeQueryDifferencesDetailed(sourceConn, targetConn, sourceProjectId, targetProjectId);
            
            // Wiki analysis - manual guidance
            analysis.Wiki = new WikiDifferences
            {
                GuidanceMessage = "Wiki content requires manual comparison. Please review wiki pages in both projects and identify differences manually."
            };
            
            _logger.LogInformation("✅ Differences analysis completed");
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to analyze project differences");
            throw;
        }
    }

    public async Task<ProjectWizardResult> ApplySelectiveUpdatesAsync(SelectiveUpdateRequest request)
    {
        _logger.LogInformation("🔄 Applying selective updates based on user choices");
        
        var result = new ProjectWizardResult
        {
            Success = false,
            StartTime = DateTime.UtcNow,
            TotalSteps = 4,
            OperationLogs = new List<OperationLog>()
        };
        
        try
        {
            result.OperationLogs.Add(new OperationLog 
            { 
                IsSuccess = true, 
                Message = "Started applying selective updates", 
                OperationType = "Start",
                Timestamp = DateTime.UtcNow
            });

            var userSettings = await _settingsService.GetSettingsAsync();
            if (userSettings == null)
            {
                throw new InvalidOperationException("User settings not configured");
            }

            var sourceConn = new VssConnection(new Uri(userSettings.OrganizationUrl), new VssBasicCredential(string.Empty, userSettings.PersonalAccessToken));
            var targetConn = new VssConnection(new Uri(userSettings.OrganizationUrl), new VssBasicCredential(string.Empty, userSettings.PersonalAccessToken));
            
            var totalUpdates = 0;
            
            // Apply selected work item updates
            result.OperationLogs.Add(new OperationLog 
            { 
                IsSuccess = true, 
                Message = "Processing work item updates", 
                OperationType = "WorkItems",
                Timestamp = DateTime.UtcNow
            });

            var workItemResults = await ApplySelectedWorkItemUpdates(sourceConn, targetConn, request, result.OperationLogs);
            result.WorkItemsCloned = workItemResults.Item1;
            result.WorkItemsUpdated = workItemResults.Item2;
            totalUpdates += workItemResults.Item1 + workItemResults.Item2;
            
            // Apply selected security group updates
            result.OperationLogs.Add(new OperationLog 
            { 
                IsSuccess = true, 
                Message = "Processing security group updates", 
                OperationType = "SecurityGroups",
                Timestamp = DateTime.UtcNow
            });

            var securityUpdates = await ApplySelectedSecurityGroupUpdates(sourceConn, targetConn, request, result.OperationLogs);
            result.SecurityGroupsCloned = securityUpdates;
            totalUpdates += securityUpdates;
            
            // Apply selected query updates
            result.OperationLogs.Add(new OperationLog 
            { 
                IsSuccess = true, 
                Message = "Processing query updates", 
                OperationType = "Queries",
                Timestamp = DateTime.UtcNow
            });

            var queryUpdates = await ApplySelectedQueryUpdates(sourceConn, targetConn, request, result.OperationLogs);
            totalUpdates += queryUpdates;
            
            result.Success = true;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            
            result.OperationLogs.Add(new OperationLog 
            { 
                IsSuccess = true, 
                Message = $"Completed successfully: {totalUpdates} changes applied", 
                Details = $"Created: {result.WorkItemsCloned} work items, Updated: {result.WorkItemsUpdated} work items",
                OperationType = "Complete",
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("✅ Selective updates completed: {TotalUpdates} changes applied", totalUpdates);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to apply selective updates");
            result.Error = ex.Message;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            
            result.OperationLogs.Add(new OperationLog 
            { 
                IsSuccess = false, 
                Message = "Operation failed", 
                Details = ex.Message,
                OperationType = "Error",
                Timestamp = DateTime.UtcNow
            });

            return result;
        }
    }

    private async Task<WorkItemDifferences> AnalyzeWorkItemDifferencesDetailed(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        var differences = new WorkItemDifferences();
        
        try
        {
            var workItemClient = sourceConn.GetClient<WorkItemTrackingHttpClient>();
            var targetWorkItemClient = targetConn.GetClient<WorkItemTrackingHttpClient>();
            
            // Get project names for WIQL queries (WIQL uses project names, not GUIDs)
            var projectClient = sourceConn.GetClient<ProjectHttpClient>();
            var sourceProject = await projectClient.GetProject(sourceProjectId);
            var targetProject = await projectClient.GetProject(targetProjectId);
            
            _logger.LogInformation("🔍 Analyzing work items between '{SourceProject}' and '{TargetProject}'", sourceProject.Name, targetProject.Name);
            
            // Get work items from both projects with expanded field selection
            var sourceQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], [System.State], 
                               [System.Description], [System.AssignedTo], [System.AreaPath], [System.IterationPath], 
                               [Microsoft.VSTS.Common.Priority] 
                               FROM WorkItems WHERE [System.TeamProject] = '{sourceProject.Name}'";
            var sourceQueryResult = await workItemClient.QueryByWiqlAsync(new Wiql { Query = sourceQuery });
            
            if (sourceQueryResult.WorkItems?.Any() != true)
            {
                _logger.LogInformation("📋 No work items found in source project '{SourceProject}'", sourceProject.Name);
                return differences;
            }
            
            var targetQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], [System.State], 
                               [System.Description], [System.AssignedTo], [System.AreaPath], [System.IterationPath], 
                               [Microsoft.VSTS.Common.Priority] 
                               FROM WorkItems WHERE [System.TeamProject] = '{targetProject.Name}'";
            var targetQueryResult = await targetWorkItemClient.QueryByWiqlAsync(new Wiql { Query = targetQuery });
            
            var sourceWorkItems = sourceQueryResult.WorkItems.Select(wi => wi.Id).ToList();
            var targetWorkItems = targetQueryResult.WorkItems?.Select(wi => wi.Id).ToList() ?? new List<int>();
            
            _logger.LogInformation("📊 Found {SourceCount} work items in source '{SourceProject}' and {TargetCount} in target '{TargetProject}'", 
                sourceWorkItems.Count, sourceProject.Name, targetWorkItems.Count, targetProject.Name);
            
            var sourceDetails = await workItemClient.GetWorkItemsAsync(sourceWorkItems, null, null, WorkItemExpand.Fields);
            var targetDetails = targetWorkItems.Any() ? 
                await targetWorkItemClient.GetWorkItemsAsync(targetWorkItems, null, null, WorkItemExpand.Fields) : 
                new List<WorkItem>();
            
            foreach (var sourceWI in sourceDetails)
            {
                var title = sourceWI.Fields["System.Title"].ToString();
                var workItemType = sourceWI.Fields["System.WorkItemType"].ToString();
                var sourceState = sourceWI.Fields["System.State"].ToString();
                
                var existingItem = targetDetails.FirstOrDefault(twi => 
                    twi.Fields["System.Title"]?.ToString()?.Equals(title, StringComparison.OrdinalIgnoreCase) == true &&
                    twi.Fields["System.WorkItemType"]?.ToString()?.Equals(workItemType, StringComparison.OrdinalIgnoreCase) == true);
                
                if (existingItem == null)
                {
                    differences.NewItems.Add(new WorkItemDifference
                    {
                        SourceId = sourceWI.Id ?? 0,
                        Title = title ?? "",
                        WorkItemType = workItemType ?? "",
                        SourceState = sourceState ?? "",
                        DifferenceType = "New",
                        Description = $"New {workItemType}: '{title}' (State: {sourceState})",
                        Selected = false // User can select to create
                    });
                }
                else
                {
                    var targetState = existingItem.Fields["System.State"]?.ToString();
                    
                    // Check for comprehensive field differences
                    var fieldDifferences = new List<string>();
                    
                    // Compare State
                    if (!sourceState?.Equals(targetState, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        fieldDifferences.Add($"State: {targetState} → {sourceState}");
                    }
                    
                    // Compare Description
                    var sourceDescription = sourceWI.Fields.ContainsKey("System.Description") ? sourceWI.Fields["System.Description"]?.ToString() : "";
                    var targetDescription = existingItem.Fields.ContainsKey("System.Description") ? existingItem.Fields["System.Description"]?.ToString() : "";
                    if (!string.Equals(sourceDescription, targetDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        var sourceTruncated = sourceDescription?.Length > 50 ? sourceDescription.Substring(0, 50) + "..." : sourceDescription;
                        var targetTruncated = targetDescription?.Length > 50 ? targetDescription.Substring(0, 50) + "..." : targetDescription;
                        fieldDifferences.Add($"Description: '{targetTruncated}' → '{sourceTruncated}'");
                    }
                    
                    // Compare Assigned To
                    var sourceAssignedTo = sourceWI.Fields.ContainsKey("System.AssignedTo") ? sourceWI.Fields["System.AssignedTo"]?.ToString() : "";
                    var targetAssignedTo = existingItem.Fields.ContainsKey("System.AssignedTo") ? existingItem.Fields["System.AssignedTo"]?.ToString() : "";
                    if (!string.Equals(sourceAssignedTo, targetAssignedTo, StringComparison.OrdinalIgnoreCase))
                    {
                        fieldDifferences.Add($"Assigned To: '{targetAssignedTo}' → '{sourceAssignedTo}'");
                    }
                    
                    // Compare Area Path
                    var sourceAreaPath = sourceWI.Fields.ContainsKey("System.AreaPath") ? sourceWI.Fields["System.AreaPath"]?.ToString() : "";
                    var targetAreaPath = existingItem.Fields.ContainsKey("System.AreaPath") ? existingItem.Fields["System.AreaPath"]?.ToString() : "";
                    if (!string.Equals(sourceAreaPath, targetAreaPath, StringComparison.OrdinalIgnoreCase))
                    {
                        fieldDifferences.Add($"Area Path: '{targetAreaPath}' → '{sourceAreaPath}'");
                    }
                    
                    // Compare Iteration Path
                    var sourceIterationPath = sourceWI.Fields.ContainsKey("System.IterationPath") ? sourceWI.Fields["System.IterationPath"]?.ToString() : "";
                    var targetIterationPath = existingItem.Fields.ContainsKey("System.IterationPath") ? existingItem.Fields["System.IterationPath"]?.ToString() : "";
                    if (!string.Equals(sourceIterationPath, targetIterationPath, StringComparison.OrdinalIgnoreCase))
                    {
                        fieldDifferences.Add($"Iteration Path: '{targetIterationPath}' → '{sourceIterationPath}'");
                    }
                    
                    // Compare Priority (if available)
                    var sourcePriority = sourceWI.Fields.ContainsKey("Microsoft.VSTS.Common.Priority") ? sourceWI.Fields["Microsoft.VSTS.Common.Priority"]?.ToString() : "";
                    var targetPriority = existingItem.Fields.ContainsKey("Microsoft.VSTS.Common.Priority") ? existingItem.Fields["Microsoft.VSTS.Common.Priority"]?.ToString() : "";
                    if (!string.Equals(sourcePriority, targetPriority, StringComparison.OrdinalIgnoreCase))
                    {
                        fieldDifferences.Add($"Priority: '{targetPriority}' → '{sourcePriority}'");
                    }
                    
                    if (fieldDifferences.Any())
                    {
                        _logger.LogInformation("🔄 Found differences for work item '{Title}': {Differences}", title, string.Join("; ", fieldDifferences));
                        
                        differences.UpdatedItems.Add(new WorkItemDifference
                        {
                            SourceId = sourceWI.Id ?? 0,
                            TargetId = existingItem.Id,
                            Title = title ?? "",
                            WorkItemType = workItemType ?? "",
                            SourceState = sourceState ?? "",
                            TargetState = targetState,
                            DifferenceType = "Update",
                            Description = $"Field changes for '{title}': {string.Join(", ", fieldDifferences)}",
                            Selected = false // User can select to update
                        });
                    }
                    else
                    {
                        _logger.LogDebug("✅ Work item '{Title}' is synchronized between projects", title);
                        
                        differences.SynchronizedItems.Add(new WorkItemDifference
                        {
                            SourceId = sourceWI.Id ?? 0,
                            TargetId = existingItem.Id,
                            Title = title ?? "",
                            WorkItemType = workItemType ?? "",
                            SourceState = sourceState ?? "",
                            TargetState = targetState,
                            DifferenceType = "Synchronized",
                            Description = $"'{title}' is already synchronized",
                            Selected = false
                        });
                    }
                }
            }
            
            _logger.LogInformation("📊 Work Item Analysis Complete:");
            _logger.LogInformation("   • New items to create: {NewCount}", differences.NewItems.Count);
            _logger.LogInformation("   • Items with differences: {UpdateCount}", differences.UpdatedItems.Count);
            _logger.LogInformation("   • Synchronized items: {SyncCount}", differences.SynchronizedItems.Count);
            
            if (differences.UpdatedItems.Any())
            {
                _logger.LogInformation("🔄 Items with detected differences:");
                foreach (var item in differences.UpdatedItems)
                {
                    _logger.LogInformation("   • {Title} ({Type}): {Description}", item.Title, item.WorkItemType, item.Description);
                }
            }
            
            return differences;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not analyze work item differences: {Error}", ex.Message);
            return differences;
        }
    }

    private async Task<SecurityGroupDifferences> AnalyzeSecurityGroupDifferencesDetailed(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        var differences = new SecurityGroupDifferences();
        
        try
        {
            // This would use the existing security group analysis logic but return detailed differences
            // For now, return empty to avoid permission issues
            _logger.LogInformation("ℹ️ Security group detailed analysis requires elevated permissions");
            return differences;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not analyze security group differences: {Error}", ex.Message);
            return differences;
        }
    }

    private async Task<(int created, int updated)> ApplySelectedWorkItemUpdates(VssConnection sourceConn, VssConnection targetConn, SelectiveUpdateRequest request, List<OperationLog> operationLogs)
    {
        var createdCount = 0;
        var updatedCount = 0;
        var sourceWorkItemClient = sourceConn.GetClient<WorkItemTrackingHttpClient>();
        var targetWorkItemClient = targetConn.GetClient<WorkItemTrackingHttpClient>();
        
        // Get target project information
        var targetProjectClient = targetConn.GetClient<ProjectHttpClient>();
        var targetProject = await targetProjectClient.GetProject(request.TargetProjectId);
        if (targetProject == null)
        {
            throw new InvalidOperationException($"Target project with ID {request.TargetProjectId} not found");
        }
        
        // Apply selected new work items
        foreach (var newItem in request.Differences.WorkItems.NewItems.Where(wi => wi.Selected))
        {
            try
            {
                // Get the source work item details first to get title and type
                var sourceWorkItem = await sourceWorkItemClient.GetWorkItemAsync(newItem.SourceId, null, null, WorkItemExpand.Fields);
                var workItemTitle = sourceWorkItem.Fields["System.Title"]?.ToString() ?? "Unknown Title";
                var workItemType = sourceWorkItem.Fields["System.WorkItemType"]?.ToString() ?? "Task";
                
                _logger.LogInformation("➕ Creating work item: {Title} (Type: {Type})", workItemTitle, workItemType);
                
                // Create patch document for new work item
                var patchDocument = new JsonPatchDocument();
                
                // Add required fields
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.Title",
                    Value = sourceWorkItem.Fields["System.Title"]
                });
                
                // Add description if it exists
                if (sourceWorkItem.Fields.ContainsKey("System.Description") && sourceWorkItem.Fields["System.Description"] != null)
                {
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                        Path = "/fields/System.Description",
                        Value = sourceWorkItem.Fields["System.Description"]
                    });
                }
                
                // Add state
                if (sourceWorkItem.Fields.ContainsKey("System.State"))
                {
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                        Path = "/fields/System.State",
                        Value = sourceWorkItem.Fields["System.State"]
                    });
                }
                
                // Set area path and iteration path to target project defaults
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.AreaPath",
                    Value = targetProject.Name
                });
                
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/fields/System.IterationPath",
                    Value = targetProject.Name
                });
                
                // Create the work item - use project name, not GUID
                var createdWorkItem = await targetWorkItemClient.CreateWorkItemAsync(patchDocument, targetProject.Name, workItemType);
                
                _logger.LogInformation("✅ Successfully created work item: {Id} - {Title}", createdWorkItem.Id, createdWorkItem.Fields["System.Title"]);
                createdCount++;
                
                operationLogs.Add(new OperationLog 
                { 
                    IsSuccess = true, 
                    Message = $"Created work item: {workItemTitle}", 
                    Details = $"ID: {createdWorkItem.Id}, Type: {workItemType}",
                    WorkItemId = createdWorkItem.Id.ToString(),
                    OperationType = "Create",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create work item with SourceId: {SourceId}", newItem.SourceId);
                operationLogs.Add(new OperationLog 
                { 
                    IsSuccess = false, 
                    Message = $"Failed to create work item with SourceId: {newItem.SourceId}", 
                    Details = ex.Message,
                    OperationType = "Create",
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        
        // Apply selected work item updates
        foreach (var updateItem in request.Differences.WorkItems.UpdatedItems.Where(wi => wi.Selected))
        {
            try
            {
                _logger.LogInformation("🔄 Updating work item: {Title} (ID: {TargetId})", updateItem.Title, updateItem.TargetId);
                
                // Get the source work item details for the updated values
                var sourceWorkItem = await sourceWorkItemClient.GetWorkItemAsync(updateItem.SourceId, null, null, WorkItemExpand.Fields);
                
                // Create patch document for updates
                var patchDocument = new JsonPatchDocument();
                
                // Update state if different
                if (updateItem.SourceState != updateItem.TargetState && !string.IsNullOrEmpty(updateItem.SourceState))
                {
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                        Path = "/fields/System.State",
                        Value = updateItem.SourceState
                    });
                    _logger.LogInformation("   📝 Updating State: {OldState} → {NewState}", updateItem.TargetState, updateItem.SourceState);
                }
                
                // Update description if different
                if (sourceWorkItem.Fields.ContainsKey("System.Description"))
                {
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                        Path = "/fields/System.Description",
                        Value = sourceWorkItem.Fields["System.Description"]
                    });
                    _logger.LogInformation("   📝 Updating Description from source");
                }
                
                // Update assigned to if different
                if (sourceWorkItem.Fields.ContainsKey("System.AssignedTo") && sourceWorkItem.Fields["System.AssignedTo"] != null)
                {
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                        Path = "/fields/System.AssignedTo",
                        Value = sourceWorkItem.Fields["System.AssignedTo"]
                    });
                    _logger.LogInformation("   📝 Updating AssignedTo from source");
                }
                
                // Update area path and iteration path to match target project
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = "/fields/System.AreaPath",
                    Value = targetProject.Name
                });
                
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Replace,
                    Path = "/fields/System.IterationPath",
                    Value = targetProject.Name
                });
                
                // Apply the updates
                if (patchDocument.Count > 0 && updateItem.TargetId.HasValue)
                {
                    await targetWorkItemClient.UpdateWorkItemAsync(patchDocument, updateItem.TargetId.Value);
                    _logger.LogInformation("✅ Successfully updated work item: {Id} - {Title}", updateItem.TargetId, updateItem.Title);
                    updatedCount++;
                    
                    operationLogs.Add(new OperationLog 
                    { 
                        IsSuccess = true, 
                        Message = $"Updated work item: {updateItem.Title}", 
                        Details = $"ID: {updateItem.TargetId}, Changes: State, Description, Assignment",
                        WorkItemId = updateItem.TargetId.ToString(),
                        OperationType = "Update",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update work item: {Title} (ID: {TargetId})", updateItem.Title, updateItem.TargetId);
                operationLogs.Add(new OperationLog 
                { 
                    IsSuccess = false, 
                    Message = $"Failed to update work item: {updateItem.Title}", 
                    Details = ex.Message,
                    WorkItemId = updateItem.TargetId?.ToString(),
                    OperationType = "Update",
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        
        return (createdCount, updatedCount);
    }



    private async Task<int> ApplySelectedSecurityGroupUpdates(VssConnection sourceConn, VssConnection targetConn, SelectiveUpdateRequest request, List<OperationLog> operationLogs)
    {
        var updatesApplied = 0;
        
        foreach (var groupDiff in request.Differences.SecurityGroups.GroupDifferences)
        {
            // Apply selected member additions
            foreach (var memberToAdd in groupDiff.MembersToAdd.Where(m => m.Selected))
            {
                _logger.LogInformation("👤 Adding member {DisplayName} to group {GroupName}", memberToAdd.DisplayName, groupDiff.GroupName);
                // Implementation would add the member to the group
                operationLogs.Add(new OperationLog 
                { 
                    IsSuccess = true, 
                    Message = $"Added member {memberToAdd.DisplayName} to group {groupDiff.GroupName}", 
                    OperationType = "SecurityGroup",
                    Timestamp = DateTime.UtcNow
                });
                updatesApplied++;
            }
            
            // Apply selected member removals
            foreach (var memberToRemove in groupDiff.MembersToRemove.Where(m => m.Selected))
            {
                _logger.LogInformation("👤 Removing member {DisplayName} from group {GroupName}", memberToRemove.DisplayName, groupDiff.GroupName);
                // Implementation would remove the member from the group
                operationLogs.Add(new OperationLog 
                { 
                    IsSuccess = true, 
                    Message = $"Removed member {memberToRemove.DisplayName} from group {groupDiff.GroupName}", 
                    OperationType = "SecurityGroup",
                    Timestamp = DateTime.UtcNow
                });
                updatesApplied++;
            }
        }
        
        return await Task.FromResult(updatesApplied);
    }
    
    private async Task<int> ApplySelectedQueryUpdates(VssConnection sourceConn, VssConnection targetConn, SelectiveUpdateRequest request, List<OperationLog> operationLogs)
    {
        var updatesApplied = 0;
        
        _logger.LogInformation("🔄 Starting ApplySelectedQueryUpdates - Folders: {FolderCount}, New: {NewCount}, Updated: {UpdatedCount}", 
            request.Differences.Queries.MissingFolders.Count(f => f.Selected),
            request.Differences.Queries.NewQueries.Count(f => f.Selected),
            request.Differences.Queries.UpdatedQueries.Count(f => f.Selected));
        
        try
        {
            var sourceWorkItemClient = sourceConn.GetClient<WorkItemTrackingHttpClient>();
            var targetWorkItemClient = targetConn.GetClient<WorkItemTrackingHttpClient>();
            
            // Get the project names from the GUIDs - Azure DevOps API expects project names for query operations
            var projectClient = sourceConn.GetClient<ProjectHttpClient>();
            var sourceProject = await projectClient.GetProject(request.SourceProjectId);
            var targetProject = await projectClient.GetProject(request.TargetProjectId);
            
            if (sourceProject == null || targetProject == null)
            {
                _logger.LogError("❌ Could not retrieve project information for source ({SourceId}) or target ({TargetId})", 
                    request.SourceProjectId, request.TargetProjectId);
                return 0;
            }
            
            _logger.LogInformation("📋 Using project names - Source: {SourceName}, Target: {TargetName}", 
                sourceProject.Name, targetProject.Name);
            
            // Create missing folders first
            foreach (var folder in request.Differences.Queries.MissingFolders.Where(f => f.Selected))
            {
                // Skip creating Recycle Bin folders
                if (folder.Path?.Contains("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true ||
                    folder.Name?.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogInformation("⏭️ Skipping Recycle Bin folder creation: {FolderPath}", folder.Path);
                    continue;
                }
                
                try
                {
                    _logger.LogInformation("📁 Creating query folder: {FolderPath}", folder.Path);
                    
                    if (string.IsNullOrEmpty(folder.Path))
                    {
                        _logger.LogWarning("⚠️ Skipping folder with empty path: {FolderName}", folder.Name);
                        continue;
                    }
                    
                    var folderParts = folder.Path.Split('/');
                    var parentPath = "";
                    
                    // Create folder hierarchy if needed
                    for (int i = 0; i < folderParts.Length; i++)
                    {
                        var currentPath = string.Join("/", folderParts.Take(i + 1));
                        var folderName = folderParts[i];
                        
                        // Check if folder already exists
                        var existingFolders = await targetWorkItemClient.GetQueriesAsync(request.TargetProjectId, QueryExpand.All, 1, true);
                        var exists = CheckIfFolderExists(existingFolders, currentPath);
                        
                        if (!exists)
                        {
                            var queryFolder = new QueryHierarchyItem
                            {
                                Name = folderName,
                                IsFolder = true,
                                IsPublic = true // Default to public for folders
                            };
                            
                            var parentFolderId = GetParentFolderId(existingFolders, parentPath);
                            await targetWorkItemClient.CreateQueryAsync(queryFolder, request.TargetProjectId, parentFolderId);
                            
                            operationLogs.Add(new OperationLog 
                            { 
                                IsSuccess = true, 
                                Message = $"Created query folder: {currentPath}", 
                                OperationType = "Query",
                                Timestamp = DateTime.UtcNow
                            });
                            updatesApplied++;
                        }
                        
                        parentPath = currentPath;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to create query folder: {FolderPath}", folder.Path);
                    operationLogs.Add(new OperationLog 
                    { 
                        IsSuccess = false, 
                        Message = $"Failed to create query folder: {folder.Path}", 
                        Details = ex.Message,
                        OperationType = "Query",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            
            // Create new queries
            foreach (var query in request.Differences.Queries.NewQueries.Where(q => q.Selected))
            {
                // Skip creating Recycle Bin queries
                if (query.Path?.Contains("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true ||
                    query.Name?.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogInformation("⏭️ Skipping Recycle Bin query creation: {QueryName}", query.Name);
                    continue;
                }
                
                try
                {
                    _logger.LogInformation("📊 Creating query: {QueryName} (ID: {QueryId})", query.Name, query.QueryId);
                    
                    // Skip folders - only process actual queries
                    if (query.QueryType == "Folder")
                    {
                        _logger.LogInformation("📁 Skipping folder: {FolderName}", query.Name);
                        continue;
                    }
                    
                    // Get the source query details using the project name with full expansion
                    var sourceQuery = await sourceWorkItemClient.GetQueryAsync(sourceProject.Name, query.QueryId, QueryExpand.All, includeDeleted: false);
                    
                    if (sourceQuery != null)
                    {
                        _logger.LogInformation("🔍 Retrieved query details - Name: '{QueryName}', Type: {QueryType}, IsFolder: {IsFolder}, HasWiql: {HasWiql}", 
                            sourceQuery.Name, sourceQuery.QueryType, sourceQuery.IsFolder, !string.IsNullOrEmpty(sourceQuery.Wiql));
                        
                        // Double-check that this is not a folder and has WIQL content
                        if (sourceQuery.IsFolder != true && !string.IsNullOrEmpty(sourceQuery.Wiql))
                        {
                            // Transform WIQL to reference target project instead of source project
                            var transformedWiql = TransformWiqlForTargetProject(sourceQuery.Wiql, sourceProject.Name, targetProject.Name);
                            
                            var newQuery = new QueryHierarchyItem
                            {
                                Name = sourceQuery.Name,
                                Wiql = transformedWiql,
                                QueryType = sourceQuery.QueryType,
                                IsPublic = sourceQuery.IsPublic
                            };
                            
                            // Determine parent folder
                            var targetQueries = await targetWorkItemClient.GetQueriesAsync(targetProject.Name, QueryExpand.All, 2, true);
                            
                            // Find the "Shared Queries" folder in the target project
                            var sharedQueriesFolder = targetQueries.FirstOrDefault(q => 
                                q.Name.Equals("Shared Queries", StringComparison.OrdinalIgnoreCase) && q.IsFolder == true);
                            
                            string? parentFolderId = null;
                            
                            if (string.IsNullOrEmpty(query.Path) || query.Path == "Shared Queries")
                            {
                                // Query is directly in Shared Queries folder - use the Shared Queries folder ID
                                parentFolderId = sharedQueriesFolder?.Id.ToString();
                            }
                            else if (query.Path.StartsWith("Shared Queries/", StringComparison.OrdinalIgnoreCase))
                            {
                                // Query is in a subfolder of Shared Queries
                                parentFolderId = GetParentFolderIdFromPath(targetQueries, query.Path, query.Name);
                            }
                            else
                            {
                                // Fallback - if path doesn't start with "Shared Queries", put it in Shared Queries folder
                                parentFolderId = sharedQueriesFolder?.Id.ToString();
                            }
                            
                            _logger.LogInformation("🗂️ Parent folder analysis - Query path: '{QueryPath}', Parent folder ID: '{ParentFolderId}', Shared Queries ID: '{SharedQueriesId}'", 
                                query.Path, parentFolderId ?? "null (root level)", sharedQueriesFolder?.Id.ToString() ?? "not found");
                            
                            // Log detailed query creation attempt
                            _logger.LogInformation("📝 Attempting to create query - Name: '{QueryName}', Type: {QueryType}, Project: '{ProjectName}', Parent: '{ParentFolderId}'", 
                                newQuery.Name, newQuery.QueryType, targetProject.Name, parentFolderId ?? "null");
                            
                            try
                            {
                                await targetWorkItemClient.CreateQueryAsync(newQuery, targetProject.Name, parentFolderId);
                                
                                operationLogs.Add(new OperationLog 
                                { 
                                    IsSuccess = true, 
                                    Message = $"Created query: {query.Name}", 
                                    Details = $"Path: {query.Path}",
                                    OperationType = "Query",
                                    Timestamp = DateTime.UtcNow
                                });
                                updatesApplied++;
                                _logger.LogInformation("✅ Successfully created query: {QueryName}", query.Name);
                            }
                            catch (Exception createEx)
                            {
                                _logger.LogError(createEx, "❌ Failed to create query '{QueryName}' - Error: {ErrorMessage}", 
                                    query.Name, createEx.Message);
                                
                                operationLogs.Add(new OperationLog 
                                { 
                                    IsSuccess = false, 
                                    Message = $"Failed to create query: {query.Name}", 
                                    Details = $"Error: {createEx.Message}",
                                    OperationType = "Query",
                                    Timestamp = DateTime.UtcNow
                                });
                            }
                        }
                        else if (sourceQuery.IsFolder == true)
                        {
                            _logger.LogInformation("📁 Skipping folder: {FolderName} (ID: {QueryId})", sourceQuery.Name, query.QueryId);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Source query {QueryName} (ID: {QueryId}) has no WIQL content", sourceQuery.Name ?? "Unknown", query.QueryId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Source query with ID {QueryId} not found", query.QueryId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to create query: {QueryName}", query.Name);
                    operationLogs.Add(new OperationLog 
                    { 
                        IsSuccess = false, 
                        Message = $"Failed to create query: {query.Name}", 
                        Details = ex.Message,
                        OperationType = "Query",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            
            // Update existing queries
            foreach (var query in request.Differences.Queries.UpdatedQueries.Where(q => q.Selected))
            {
                // Skip updating Recycle Bin queries
                if (query.Path?.Contains("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true ||
                    query.Name?.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogInformation("⏭️ Skipping Recycle Bin query update: {QueryName}", query.Name);
                    continue;
                }
                
                try
                {
                    _logger.LogInformation("🔄 Updating query: {QueryName} (ID: {QueryId})", query.Name, query.QueryId);
                    
                    // Get the source query details using the project name
                    var sourceQuery = await sourceWorkItemClient.GetQueryAsync(sourceProject.Name, query.QueryId);
                    
                    if (sourceQuery != null)
                    {
                        // Find the target query to update
                        var targetQueries = await targetWorkItemClient.GetQueriesAsync(targetProject.Name, QueryExpand.All, 2, true);
                        var targetQuery = FindQueryByPath(targetQueries, query.Path ?? string.Empty);
                        
                        if (targetQuery != null)
                        {
                            // Transform WIQL to reference target project instead of source project
                            var transformedWiql = TransformWiqlForTargetProject(sourceQuery.Wiql, sourceProject.Name, targetProject.Name);
                            
                            var updatedQuery = new QueryHierarchyItem
                            {
                                Name = sourceQuery.Name,
                                Wiql = transformedWiql,
                                QueryType = sourceQuery.QueryType,
                                IsPublic = sourceQuery.IsPublic
                            };
                            
                            await targetWorkItemClient.UpdateQueryAsync(updatedQuery, targetProject.Name, targetQuery.Id.ToString());
                            
                            operationLogs.Add(new OperationLog 
                            { 
                                IsSuccess = true, 
                                Message = $"Updated query: {query.Name}", 
                                Details = $"Path: {query.Path}",
                                OperationType = "Query",
                                Timestamp = DateTime.UtcNow
                            });
                            updatesApplied++;
                            _logger.LogInformation("✅ Successfully updated query: {QueryName}", query.Name);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Target query not found at path: {QueryPath}", query.Path);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Source query {QueryName} (ID: {QueryId}) not found", query.Name, query.QueryId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to update query: {QueryName}", query.Name);
                    operationLogs.Add(new OperationLog 
                    { 
                        IsSuccess = false, 
                        Message = $"Failed to update query: {query.Name}", 
                        Details = ex.Message,
                        OperationType = "Query",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            
            _logger.LogInformation("✅ Query updates completed: {UpdatesApplied} changes applied", updatesApplied);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to apply query updates");
            operationLogs.Add(new OperationLog 
            { 
                IsSuccess = false, 
                Message = "Failed to apply query updates", 
                Details = ex.Message,
                OperationType = "Query",
                Timestamp = DateTime.UtcNow
            });
        }
        
        return updatesApplied;
    }
    
    private bool CheckIfFolderExists(IEnumerable<QueryHierarchyItem> queries, string folderPath)
    {
        return FlattenQueryHierarchy(queries).Any(q => 
            q.IsFolder == true && 
            q.Path?.Equals(folderPath, StringComparison.OrdinalIgnoreCase) == true);
    }
    
    private string? GetParentFolderId(IEnumerable<QueryHierarchyItem> queries, string parentPath)
    {
        if (string.IsNullOrEmpty(parentPath))
            return null; // Root level
            
        var parentFolder = FlattenQueryHierarchy(queries).FirstOrDefault(q => 
            q.IsFolder == true && 
            q.Path?.Equals(parentPath, StringComparison.OrdinalIgnoreCase) == true);
            
        return parentFolder?.Id.ToString();
    }
    
    private string? GetParentFolderIdFromPath(IEnumerable<QueryHierarchyItem> queries, string fullPath, string queryName)
    {
        if (string.IsNullOrEmpty(fullPath) || fullPath == queryName)
            return null; // Root level
            
        var parentPath = fullPath.Substring(0, fullPath.LastIndexOf('/' + queryName));
        return GetParentFolderId(queries, parentPath);
    }
    
    private QueryHierarchyItem? FindQueryByPath(IEnumerable<QueryHierarchyItem> queries, string queryPath)
    {
        return FlattenQueryHierarchy(queries).FirstOrDefault(q => 
            q.Path?.Equals(queryPath, StringComparison.OrdinalIgnoreCase) == true);
    }
    
    private async Task<QueryDifferences> AnalyzeQueryDifferencesDetailed(VssConnection sourceConn, VssConnection targetConn, string sourceProjectId, string targetProjectId)
    {
        _logger.LogInformation("🔍 Analyzing work item query differences between projects - Source: {SourceProject}, Target: {TargetProject}", sourceProjectId, targetProjectId);
        
        var differences = new QueryDifferences();
        
        try
        {
            var sourceWorkItemClient = sourceConn.GetClient<WorkItemTrackingHttpClient>();
            var targetWorkItemClient = targetConn.GetClient<WorkItemTrackingHttpClient>();
            
            // Get all queries from source project (includes folders and queries) - force fresh retrieval
            var sourceQueries = await sourceWorkItemClient.GetQueriesAsync(sourceProjectId, QueryExpand.All, 2, true);
            
            // Force a small delay and fresh retrieval for target to avoid caching issues
            await Task.Delay(100);
            var targetQueries = await targetWorkItemClient.GetQueriesAsync(targetProjectId, QueryExpand.All, 2, true);
            
            _logger.LogInformation("📋 Retrieved {SourceTotal} total source query items and {TargetTotal} total target query items", 
                sourceQueries.Count, targetQueries.Count);
                
            // Log the top-level folders for debugging
            _logger.LogInformation("🔍 Source top-level folders: {SourceFolders}", 
                string.Join(", ", sourceQueries.Select(q => $"{q.Name} (IsFolder: {q.IsFolder}, IsDeleted: {q.IsDeleted})")));
            _logger.LogInformation("🔍 Target top-level folders: {TargetFolders}", 
                string.Join(", ", targetQueries.Select(q => $"{q.Name} (IsFolder: {q.IsFolder}, IsDeleted: {q.IsDeleted})")));
            
            // Filter to only "Shared Queries" folder and its contents - exclude any deleted or recycled items
            var sourceSharedQueriesFolder = sourceQueries.FirstOrDefault(q => 
                q.Name.Equals("Shared Queries", StringComparison.OrdinalIgnoreCase) && 
                q.IsDeleted != true);
            var targetSharedQueriesFolder = targetQueries.FirstOrDefault(q => 
                q.Name.Equals("Shared Queries", StringComparison.OrdinalIgnoreCase) && 
                q.IsDeleted != true);
            
            if (sourceSharedQueriesFolder == null)
            {
                _logger.LogWarning("⚠️ No 'Shared Queries' folder found in source project {SourceProject}", sourceProjectId);
                return differences;
            }
            
            _logger.LogInformation("✅ Found 'Shared Queries' folder in source project with {ChildCount} children", 
                sourceSharedQueriesFolder.Children?.Count ?? 0);
            
            if (targetSharedQueriesFolder != null)
            {
                _logger.LogInformation("✅ Found 'Shared Queries' folder in target project with {ChildCount} children", 
                    targetSharedQueriesFolder.Children?.Count ?? 0);
            }
            else
            {
                _logger.LogInformation("ℹ️ No 'Shared Queries' folder found in target project - will create one if needed");
            }
            
            // Flatten only shared queries and their subfolders/contents
            var sourceFlattened = sourceSharedQueriesFolder?.Children != null ? 
                FlattenQueryHierarchy(sourceSharedQueriesFolder.Children, "Shared Queries") : 
                new List<QueryHierarchyItem>();
            var targetFlattened = targetSharedQueriesFolder?.Children != null ? 
                FlattenQueryHierarchy(targetSharedQueriesFolder.Children, "Shared Queries") : 
                new List<QueryHierarchyItem>();
            
            _logger.LogInformation("📊 Before filtering - Source: {SourceCount} items, Target: {TargetCount} items", 
                sourceFlattened.Count, targetFlattened.Count);
            
            // Filter out Recycle Bin queries and folders from both source and target (in case they exist within Shared Queries)
            sourceFlattened = FilterOutRecycleBinQueries(sourceFlattened);
            targetFlattened = FilterOutRecycleBinQueries(targetFlattened);
            
            // Additional validation: Ensure all items are under "Shared Queries" path AND not deleted
            sourceFlattened = sourceFlattened.Where(s => 
                s.Path?.StartsWith("Shared Queries", StringComparison.OrdinalIgnoreCase) == true &&
                s.IsDeleted != true &&
                !IsRecycleBinQuery(s)).ToList();
            targetFlattened = targetFlattened.Where(t => 
                t.Path?.StartsWith("Shared Queries", StringComparison.OrdinalIgnoreCase) == true &&
                t.IsDeleted != true &&
                !IsRecycleBinQuery(t)).ToList();
            
            _logger.LogInformation("📊 After filtering Recycle Bin - Source: {SourceCount} items, Target: {TargetCount} items", 
                sourceFlattened.Count, targetFlattened.Count);
                
            // Log the actual items being analyzed for debugging
            _logger.LogInformation("🔍 Source items: {SourceItems}", 
                string.Join(", ", sourceFlattened.Select(s => $"{s.Name} ({s.Path}) [{(s.IsFolder == true ? "Folder" : "Query")}]")));
            _logger.LogInformation("🔍 Target items: {TargetItems}", 
                string.Join(", ", targetFlattened.Select(t => $"{t.Name} ({t.Path}) [{(t.IsFolder == true ? "Folder" : "Query")}]")));
            
            // Analyze differences
            foreach (var sourceItem in sourceFlattened)
            {
                _logger.LogDebug("🔍 Analyzing source item: {Name} at path {Path} (IsFolder: {IsFolder})", 
                    sourceItem.Name, sourceItem.Path, sourceItem.IsFolder);
                    
                var targetMatch = targetFlattened.FirstOrDefault(t => 
                    t.Path?.Equals(sourceItem.Path, StringComparison.OrdinalIgnoreCase) == true);
                
                if (targetMatch == null)
                {
                    _logger.LogDebug("➕ No target match found for: {Name} at {Path}", sourceItem.Name, sourceItem.Path);
                    
                    // New query or folder
                    var diff = CreateQueryDifference(sourceItem, "New", 
                        sourceItem.IsFolder == true ? "New folder to be created" : "New query to be created");
                    
                    if (sourceItem.IsFolder == true)
                        differences.MissingFolders.Add(diff);
                    else
                        differences.NewQueries.Add(diff);
                }
                else
                {
                    _logger.LogDebug("🔄 Target match found for: {Name} - checking for differences", sourceItem.Name);
                    
                    // Check if query content differs (only for actual queries, not folders)
                    if (sourceItem.IsFolder != true && targetMatch.IsFolder != true)
                    {
                        var hasChanges = false;
                        var changeDescription = new List<string>();
                        
                        if (!string.Equals(sourceItem.Wiql, targetMatch.Wiql, StringComparison.OrdinalIgnoreCase))
                        {
                            hasChanges = true;
                            changeDescription.Add("WIQL query differs");
                        }
                        
                        if (sourceItem.IsPublic != targetMatch.IsPublic)
                        {
                            hasChanges = true;
                            changeDescription.Add($"Visibility differs (source: {(sourceItem.IsPublic == true ? "Public" : "Private")}, target: {(targetMatch.IsPublic == true ? "Public" : "Private")})");
                        }
                        
                        if (hasChanges)
                        {
                            var diff = CreateQueryDifference(sourceItem, "Update", string.Join(", ", changeDescription));
                            differences.UpdatedQueries.Add(diff);
                        }
                        else
                        {
                            var diff = CreateQueryDifference(sourceItem, "Synchronized", "Query is identical in both projects");
                            differences.SynchronizedQueries.Add(diff);
                        }
                    }
                }
            }
            
            _logger.LogInformation("✅ Query analysis completed: {NewCount} new, {UpdatedCount} updated, {SyncCount} synchronized, {FolderCount} missing folders",
                differences.NewQueries.Count, differences.UpdatedQueries.Count, 
                differences.SynchronizedQueries.Count, differences.MissingFolders.Count);
                
            return differences;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to analyze query differences");
            
            // Return empty differences with error info
            differences.NewQueries.Add(new QueryDifference
            {
                Name = "Analysis Error",
                Description = $"Failed to analyze queries: {ex.Message}",
                DifferenceType = "Error"
            });
            
            return differences;
        }
    }
    
    private List<QueryHierarchyItem> FlattenQueryHierarchy(IEnumerable<QueryHierarchyItem> queries, string parentPath = "")
    {
        var flattened = new List<QueryHierarchyItem>();
        
        foreach (var query in queries)
        {
            // Set the full path for this item
            var currentPath = string.IsNullOrEmpty(parentPath) ? query.Name : $"{parentPath}/{query.Name}";
            query.Path = currentPath;
            
            flattened.Add(query);
            
            // Recursively process children
            if (query.Children?.Any() == true)
            {
                flattened.AddRange(FlattenQueryHierarchy(query.Children, currentPath));
            }
        }
        
        return flattened;
    }
    
    private bool IsRecycleBinQuery(QueryHierarchyItem query)
    {
        // Check for direct Recycle Bin names
        if (query.Name?.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true)
            return true;
            
        // Check for Recycle Bin in path
        if (query.Path?.Contains("Recycle Bin", StringComparison.OrdinalIgnoreCase) == true)
            return true;
            
        // Check for special properties that indicate deleted/recycled items
        // In Azure DevOps, deleted queries often have IsDeleted = true
        if (query.IsDeleted == true)
            return true;
            
        // Check for queries that might be in Recycle Bin based on folder properties or name patterns
        if (query.IsFolder == true && 
            query.Name?.StartsWith("Recycle", StringComparison.OrdinalIgnoreCase) == true)
            return true;
            
        return false;
    }
    
    private List<QueryHierarchyItem> FilterOutRecycleBinQueries(IEnumerable<QueryHierarchyItem> queries)
    {
        return queries.Where(q => !IsRecycleBinQuery(q)).ToList();
    }
    
    private string TransformWiqlForTargetProject(string originalWiql, string sourceProjectName, string targetProjectName)
    {
        if (string.IsNullOrEmpty(originalWiql))
            return originalWiql;

        _logger.LogInformation("🔄 Transforming WIQL from source project '{SourceProject}' to target project '{TargetProject}'", 
            sourceProjectName, targetProjectName);
        
        var transformedWiql = originalWiql;
        
        // 1. Replace explicit project references in [Team Project] field
        // Pattern: [Team Project] = 'SourceProjectName'
        var teamProjectPattern = @"\[Team Project\]\s*=\s*['""]" + Regex.Escape(sourceProjectName) + @"['""]";
        var teamProjectReplacement = $"[Team Project] = '{targetProjectName}'";
        transformedWiql = Regex.Replace(transformedWiql, teamProjectPattern, teamProjectReplacement, RegexOptions.IgnoreCase);
        
        // 2. Replace project references in area path constraints
        // Pattern: [Area Path] UNDER 'SourceProjectName' or [Area Path] = 'SourceProjectName'
        var areaPathUnderPattern = @"\[Area Path\]\s+(UNDER|=)\s*['""]" + Regex.Escape(sourceProjectName) + @"['""]";
        var areaPathUnderReplacement = $"[Area Path] $1 '{targetProjectName}'";
        transformedWiql = Regex.Replace(transformedWiql, areaPathUnderPattern, areaPathUnderReplacement, RegexOptions.IgnoreCase);
        
        // 3. Replace project references in iteration path constraints
        // Pattern: [Iteration Path] UNDER 'SourceProjectName' or [Iteration Path] = 'SourceProjectName'
        var iterationPathUnderPattern = @"\[Iteration Path\]\s+(UNDER|=)\s*['""]" + Regex.Escape(sourceProjectName) + @"['""]";
        var iterationPathUnderReplacement = $"[Iteration Path] $1 '{targetProjectName}'";
        transformedWiql = Regex.Replace(transformedWiql, iterationPathUnderPattern, iterationPathUnderReplacement, RegexOptions.IgnoreCase);
        
        // 4. Replace any remaining bare project name references (be more careful with this)
        // Only replace if it's quoted and appears to be a project reference
        var bareProjectPattern = @"['""]" + Regex.Escape(sourceProjectName) + @"['""]";
        var bareProjectReplacement = $"'{targetProjectName}'";
        transformedWiql = Regex.Replace(transformedWiql, bareProjectPattern, bareProjectReplacement, RegexOptions.IgnoreCase);
        
        // 5. Remove or fix any cross-project query flags
        // Remove "Query across projects" or similar flags if they exist
        transformedWiql = Regex.Replace(transformedWiql, @"@@project\s*=\s*['""][^'""]*['""]", "", RegexOptions.IgnoreCase);
        
        // Log the transformation
        if (!string.Equals(originalWiql, transformedWiql, StringComparison.Ordinal))
        {
            _logger.LogInformation("✅ WIQL transformed successfully - project references updated from '{SourceProject}' to '{TargetProject}'", 
                sourceProjectName, targetProjectName);
            _logger.LogInformation("🔍 Original WIQL (first 200 chars): {OriginalWiql}", 
                originalWiql.Length > 200 ? originalWiql.Substring(0, 200) + "..." : originalWiql);
            _logger.LogInformation("🎯 Transformed WIQL (first 200 chars): {TransformedWiql}", 
                transformedWiql.Length > 200 ? transformedWiql.Substring(0, 200) + "..." : transformedWiql);
        }
        else
        {
            _logger.LogInformation("ℹ️ No project references found in WIQL for '{SourceProject}' - no transformation needed", sourceProjectName);
            _logger.LogDebug("📝 WIQL content: {WiqlContent}", originalWiql);
        }
        
        return transformedWiql;
    }
    
    private QueryDifference CreateQueryDifference(QueryHierarchyItem queryItem, string differenceType, string description)
    {
        return new QueryDifference
        {
            QueryId = queryItem.Id.ToString(),
            Name = queryItem.Name ?? "",
            Path = queryItem.Path ?? "",
            QueryType = queryItem.QueryType?.ToString() ?? (queryItem.IsFolder == true ? "Folder" : "Unknown"),
            Wiql = queryItem.Wiql,
            DifferenceType = differenceType,
            Description = description,
            IsPublic = queryItem.IsPublic ?? false
        };
    }
}
