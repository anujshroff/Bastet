# BASTET - Badass Subnetting Tools for Enhanced Transmission

BASTET is a modern, web-based subnet management system that helps network administrators efficiently design, organize, and visualize IP subnet hierarchies. It provides powerful tools for subnet calculations, relationship tracking, and IP allocation management.

## Features

- **Subnet Hierarchy Management**: Create and manage parent-child relationships between subnets
- **Automatic Subnet Calculations**: View subnet masks, broadcast addresses, usable IP ranges
- **Visual Subnet Hierarchy**: Tree-based visualization of your subnet structure
- **IP Address Allocation**: Track and manage allocated/unallocated IP spaces
- **Deleted Subnet Archive**: Recover information from deleted subnets
- **IP Validation**: Built-in validation ensures IP/CIDR configurations are valid
- **Azure Integration**: Import subnets directly from Azure Virtual Networks

## Technologies

- **ASP.NET Core MVC** (.NET 9.0)
- **Entity Framework Core**
- **SQL Server**
- **Bootstrap for responsive UI**
- **JavaScript/jQuery**

## Prerequisites

- **.NET 9.0 SDK**
- **SQL Server** (or compatible database)
- **Modern web browser**

## Installation

### Database Setup

BASTET requires a SQL Server database. Before running the application:

1. **Create a database** on your SQL Server instance
2. **Setup database user access**:
   - For auto-migration: User needs `db_owner` role
   - For manual migration: User needs `db_datareader` and `db_datawriter` roles

#### Manual Database Migration
If you prefer to run migrations manually:

1. Execute the SQL migration script found at `Database/GeneratedMigrationScript.sql`
2. Set `BASTET_AUTO_MIGRATE=false` when running the application

### Authentication Setup

BASTET uses OpenID Connect (OIDC) for authentication in production environments. The identity provider must meet these requirements:

- **OIDC Provider Requirements**:
  - Support OpenID Connect protocol
  - Support either implicit flow (`id_token`) or authorization code flow (`code`) with PKCE
  - Support the following scopes:
    - `openid`
    - `profile`
    - `email`
    - `roles`
    - `offline_access` (for refresh tokens)
  - Issue tokens with one of these user identifier claims (checked in this order):
    - `preferred_username` (checked first)
    - `email` (ClaimTypes.Email, fallback)
    - `name` (ClaimTypes.Name, final fallback)

- **Role-Based Authorization Requirements**:
  - Issue role claims that map to ASP.NET Core `ClaimTypes.Role`
  - Provide at least one of these roles for users to access the application:
    - `View` - Basic read access to subnet information
    - `Edit` - Permission to create and modify subnets
    - `Delete` - Permission to remove subnets
    - `Admin` - Full administrative access

- **OIDC Configuration**:
  - Register BASTET as a client in your identity provider
  - Configure the following redirect URIs:
    - Login callback: `/signin-oidc` (relative to application base URL)
    - Logout callback: `/signout-callback-oidc` (relative to application base URL)
  - Set the required environment variables:
    - `BASTET_OIDC_AUTHORITY`: URL of your OIDC provider
    - `BASTET_OIDC_CLIENT_ID`: Client ID registered with your provider
  - Set optional environment variables based on your authentication flow:
    - `BASTET_OIDC_RESPONSE_TYPE`: Set to `code` (default) for authorization code flow with PKCE, or `id_token` for implicit flow
    - `BASTET_OIDC_CLIENT_SECRET`: Required when using authorization code flow with identity providers that require client authentication (e.g., Microsoft Entra ID)

- **Provider-Specific Notes**:
  - **Auth0**: Supports authorization code flow with PKCE without requiring a client secret
  - **Microsoft Entra ID**: Requires a client secret when using authorization code flow, even with PKCE

In development environments, authentication is automatically handled by `DevAuthHandler`, which provides a simulated user with all roles.

### Docker Deployment

BASTET is available as a Docker image through GitHub Container Registry:

1. **Pull the latest image**:
   ```bash
   docker pull ghcr.io/anujshroff/bastet:latest
   ```

2. **Run the container** with the required environment variables:
   ```bash
   docker run -d \
     --name bastet \
     -p 8080:80 \
     -e BASTET_CONNECTION_STRING="Server=your-server;Database=bastet;User Id=your-user;Password=your-password;" \
     -e BASTET_AUTO_MIGRATE=false \
     -e ASPNETCORE_URLS="http://+:80" \
     -e BASTET_OIDC_CLIENT_ID="your-client-id" \
     -e BASTET_OIDC_AUTHORITY="https://your-identity-provider" \
     ghcr.io/anujshroff/bastet:latest
   ```

3. **Access the application** at http://localhost:8080

   > **Important Note**: The OpenID Connect authentication process requires the application to be served over HTTPS. For production deployments, you will need to set up HTTPS ingress (such as a reverse proxy, load balancer, or API gateway) in front of the Docker container. This is necessary because OIDC security features are designed to work with encrypted connections.

## Environment Variables

BASTET supports configuration through environment variables:

| Category | Variable | Description | Example/Values | Default | Notes |
|----------|----------|-------------|----------------|---------|-------|
| Database Configuration | **BASTET_CONNECTION_STRING** | Database connection string | `Server=your-server.database.windows.net;Authentication=Active Directory Default; Encrypt=True; Database=bastet;` | - | Required in non-development environments. In development, falls back to appsettings.json if not specified. |
| Database Configuration | **BASTET_AUTO_MIGRATE** | Controls whether database migrations are automatically applied on startup | `true` or `false` | `false` | If true, app will need to be able to modify the schema. If false, db_datareader and db_datawriter would be sufficient. |
| Server Configuration | **ASPNETCORE_URLS** | Configures the URLs and ports the application will listen on | `http://+:5000` | ? | In development environments, defaults to settings in launchSettings.json |
| Server Configuration | **WEBSITES_PORT** | Tells Azure App Service which port the app is listening on | `5000` | - | Should match the port specified in ASPNETCORE_URLS when deployed to Azure App Service |
| Server Configuration | **AZURE_CLIENT_ID** | Specifies the client ID for Azure Managed Identity | `123e4567-e89b-12d3-a456-426614174000` | - | Required when using Managed Identity in Azure for server authentication to SQL server. |
| Authentication Configuration | **BASTET_OIDC_CLIENT_ID** | OpenID Connect client ID | `mvc_client` or `0e0e7c73-5fce-45c1-be7c-0161f462fd9d` | `mvc_client` | Required in non-development environments. Authentication is disabled in development environments. |
| Authentication Configuration | **BASTET_OIDC_AUTHORITY** | OpenID Connect authority URL | `https://identity.your-domain.com` or `https://login.microsoftonline.com/0af80680-dd36-43bf-bf53-b951b9fdd68b` | `https://localhost` | Required in non-development environments. Authentication is disabled in development environments. |
| Authentication Configuration | **BASTET_OIDC_CLIENT_SECRET** | Client secret for authentication with the OIDC provider | `your-client-secret` | - | Required when using authorization code flow with providers that require client authentication (e.g., Microsoft Entra ID). Not needed for providers that support PKCE without client authentication (e.g., Auth0). |
| Authentication Configuration | **BASTET_OIDC_RESPONSE_TYPE** | OIDC response type | `id_token` or `code` | `code` | Controls the authentication flow: `id_token` for implicit flow, `code` for authorization code flow. Set to `code` when using PKCE. |
| Logging Configuration | **BASTET_LOG_LEVEL_DEFAULT** | Default logging level for all categories | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Logging Configuration | **BASTET_LOG_LEVEL_ASPNETCORE** | Logging level for ASP.NET Core components | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Logging Configuration | **BASTET_LOG_LEVEL_ENTITYFRAMEWORK** | Logging level for Entity Framework components | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Feature Configuration | **BASTET_AZURE_IMPORT** | Enables Azure VNet subnet import functionality | `true` or `false` | `false` | Admin users can import subnets from Azure VNets when enabled |

## Azure Integration

BASTET includes a feature to import subnets directly from Azure Virtual Networks, allowing network administrators to easily synchronize their cloud and on-premises network configurations.

### Prerequisites

- **Azure Authentication**: The application uses DefaultAzureCredential for authentication to Azure, which supports various authentication methods:
  - Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET)
  - Managed Identity
  - Visual Studio Code authentication
  - Azure CLI authentication
  - Interactive browser authentication

### Enabling Azure Import

1. Set the environment variable `BASTET_AZURE_IMPORT=true`
2. Ensure proper Azure authentication is configured
3. Restart the application

### Importing Subnets from Azure

1. Navigate to a subnet's details page
2. If the subnet has no child subnets or host IP assignments, an "Azure Import" button will appear (admin role required)
3. Click "Azure Import" to start the import wizard
4. Follow the multi-step process:
   - Select an Azure Subscription
   - Choose a compatible Virtual Network
   - Select specific subnets to import
5. The selected Azure subnets will be imported as child subnets

## Usage

### Creating a Root Subnet

1. Navigate to the Subnet Hierarchy page
2. Click "Create Subnet"
3. Enter subnet details:
   - Name: e.g., "Corporate Network"
   - Network Address: e.g., "10.0.0.0"
   - CIDR: e.g., "8"
   - Description (optional)
4. Submit the form

### Creating Child Subnets

1. View a parent subnet's details
2. Locate unallocated IP ranges
3. Click "Create Subnet" next to an available range
4. Enter subnet details including the pre-filled network address

### Viewing Subnet Details

- Click on any subnet in the hierarchy to view detailed information
- See calculated properties like subnet mask, broadcast address, etc.
- View the list of child subnets and unallocated IP ranges

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## AI Notice

This project was almost entirely generated using AI, leveraging the power of **Cline** with **Claude 3.7**. It serves as a testament to the capabilities of modern AI in automating complex development tasks and streamlining the software creation process.
