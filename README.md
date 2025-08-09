# Azure DevOps Project Manager

A comprehensive .NET 9.0 ASP.NET Core application for cloning and managing Azure DevOps projects with enhanced functionality.

## Features

### 🎯 **Enhanced Project Cloning**
- **Same-Organization Cloning**: Streamlined cloning within the current Azure DevOps organization (no target URL required)
- **Enhanced Service Detection**: Comprehensive Azure DevOps services matching (Boards, Repos, Pipelines, Test Plans, Artifacts, Dashboards, Wiki)
- **Project Administrator Management**: Advanced security group and administrator cloning capabilities
- **Detailed Progress Reporting**: Real-time progress tracking with emoji-based indicators

### 🔧 **Core Capabilities**
- 🔍 Browse Azure DevOps projects and repositories
- 📥 Clone repositories with a simple web interface
- 🏗️ Clone entire projects including work items, repositories, pipelines, and settings
- ⚙️ Configurable clone settings (branch, submodules, local path)
- 🎨 Modern, responsive web interface
- 🔒 Secure authentication using Personal Access Tokens

## Prerequisites

- .NET 9.0 SDK
- An Azure DevOps organization with projects and repositories
- Personal Access Token with appropriate permissions

## Configuration

1. Update the `appsettings.json` file with your Azure DevOps details:

```json
{
  "AdoConnectionSettings": {
    "OrganizationUrl": "https://dev.azure.com/your-organization",
    "PersonalAccessToken": "your-personal-access-token",
    "DefaultClonePath": "C:\\Projects"
  }
}
```

2. To get a Personal Access Token:
   - Go to your Azure DevOps organization
   - Click on your profile picture → Personal access tokens
   - Click "New Token"
   - Select scopes: **Code (read/write)**, **Project and team (read/write)**, **Work Items (read/write)**, **Build (read/write)**, **Release (read/write)**
   - Copy the generated token

## Running the Application

1. Restore packages:

   ```
   dotnet restore
   ```

2. Build the application:

   ```
   dotnet build
   ```

3. Run the application:

   ```
   dotnet run
   ```

4. Open your browser and navigate to `https://localhost:5001` (or the URL shown in the console)

## Usage

### Viewing Projects

- The main page displays all Azure DevOps projects you have access to
- Each project card shows its repositories with basic information

### Cloning Repositories

1. Click the "Clone" button next to any repository
2. Configure the clone settings:
   - **Local Path**: Relative path under the default clone directory
   - **Branch**: Branch to clone (defaults to "main")
   - **Include Submodules**: Whether to include Git submodules
3. Click "Start Clone" to begin the cloning process

### Settings

- Access the Settings page to view configuration instructions
- The page provides guidance on setting up your Personal Access Token

## Dependencies

- **Microsoft.TeamFoundationServer.Client**: Azure DevOps REST API client
- **Microsoft.VisualStudio.Services.Client**: Visual Studio Team Services client  
- **Microsoft.VisualStudio.Services.Graph.Client**: Azure DevOps Graph API for security groups
- **Microsoft.TeamFoundation.WorkItemTracking.WebApi**: Work item tracking API
- **Microsoft.TeamFoundation.Build.WebApi**: Build pipeline API
- **Microsoft.TeamFoundation.Core.WebApi**: Core Azure DevOps API
- **LibGit2Sharp**: Git operations library for .NET
- **Entity Framework Core**: Data access with SQLite
- **Bootstrap 5**: UI framework
- **Bootstrap Icons**: Icon library

## Security Considerations

- Never commit your Personal Access Token to source control
- Consider using user secrets for development: `dotnet user-secrets`
- Use environment variables or Azure Key Vault for production deployments
- Ensure your PAT has minimal required permissions

## Troubleshooting

### Connection Issues

- Verify your Organization URL is correct
- Check that your Personal Access Token is valid and has the required scopes
- Ensure your firewall/proxy allows connections to Azure DevOps

### Clone Issues

- Verify you have write permissions to the default clone path
- Check that Git is installed and accessible
- Ensure the repository URL is accessible with your credentials

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.
