# Azure DevOps FeatureManagement API Implementation

## Overview
This document describes the implementation of the Azure DevOps FeatureManagement API to properly control service visibility during project cloning operations.

## Problem Statement
The original issue was: **"Clone Project Settings & Service Visibility...these did not match on clone"**

Services (Boards, Repos, Pipelines, Test Plans, Artifacts) were not being properly enabled/disabled in target projects to match the source project configuration.

## Solution Approach

### Previous Approach (Problematic)
- Used project capabilities API
- Attempted to modify `processTemplate` and `versioncontrol` capabilities
- Inconsistent results and limited service control

### New Approach (Authoritative)
Based on Stack Overflow reference, implemented direct Azure DevOps FeatureManagement API calls:

**API Endpoint Pattern:**
```
PATCH https://{account}.visualstudio.com/_apis/FeatureManagement/FeatureStates/host/project/{project-id}/{feature-id}?api-version=4.1-preview.1
```

**Request Body:**
```json
{
    "featureId": "{feature-id}",
    "scope": {
        "settingScope": "project", 
        "userScoped": false
    },
    "state": 0  // 0=disabled, 1=enabled
}
```

## Feature ID Mappings

| Service Name | Azure DevOps Feature ID |
|-------------|-------------------------|
| Boards | `ms.vss-work.agile` |
| Repos | `ms.vss-code.version-control` |
| Pipelines | `ms.vss-build.pipelines` |
| Test Plans | `ms.vss-test-web.test` |
| Artifacts | `ms.azure-artifacts.feature` |

## Implementation Details

### Key Components

1. **HTTP Client Setup**: Direct REST API calls using `HttpClient`
2. **Authentication**: Personal Access Token (PAT) via Basic authentication
3. **Service Mapping**: Proper feature ID mapping for each Azure DevOps service
4. **State Control**: Binary enable/disable (1/0) for each service
5. **Enhanced Logging**: Full API request/response logging for debugging
6. **Error Handling**: Graceful fallback with detailed error reporting

### Code Location
- **File**: `Services/ProjectCloneService.cs`
- **Method**: `ApplyServiceSettingsWithFeatureManagement()`
- **Lines**: ~884-1000

### Key Features

✅ **Direct API Control**: Uses official Azure DevOps FeatureManagement API  
✅ **Proper Authentication**: PAT-based authentication for API access  
✅ **Service State Mapping**: Correct feature IDs for all major services  
✅ **Detailed Logging**: Full request/response logging for troubleshooting  
✅ **Error Recovery**: Fallback mechanisms and user guidance  
✅ **Propagation Timing**: 5-second delay for changes to take effect  

## Authentication Requirements

The implementation requires a Personal Access Token (PAT) with appropriate permissions:
- Set `AZURE_DEVOPS_PAT` environment variable
- PAT must have project administration permissions
- Basic authentication header: `Authorization: Basic <base64(:PAT)>`

## Usage

This implementation is automatically used during project cloning when service visibility configuration is detected in the source project. The system will:

1. Detect enabled services in the source project
2. Map services to proper Azure DevOps feature IDs  
3. Make PATCH requests to FeatureManagement API
4. Apply the same ON/OFF state to the target project
5. Verify changes and provide detailed logging

## Benefits

🎯 **Reliable Service Control**: Uses the official API for service state management  
🔍 **Better Visibility**: Detailed logging shows exactly what API calls are made  
⚡ **Faster Response**: Direct API calls vs. project capability modifications  
🛡️ **Robust Error Handling**: Clear error messages and fallback instructions  
📈 **Scalable Approach**: Easily extensible for additional Azure DevOps services  

## Future Enhancements

- Support for additional services (Wiki, Dashboards, etc.)
- Bulk service operations for better performance
- Service dependency validation
- Custom service configuration profiles

## Reference
Based on authoritative Stack Overflow solution for Azure DevOps service enable/disable API patterns.
