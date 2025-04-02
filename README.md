# BASTET - Badass Subnetting Tools for Enhanced Transmission

BASTET is a modern, web-based subnet management system that helps network administrators efficiently design, organize, and visualize IP subnet hierarchies. It provides powerful tools for subnet calculations, relationship tracking, and IP allocation management.

## Features

- **Subnet Hierarchy Management**: Create and manage parent-child relationships between subnets
- **Automatic Subnet Calculations**: View subnet masks, broadcast addresses, usable IP ranges
- **Visual Subnet Hierarchy**: Tree-based visualization of your subnet structure
- **IP Address Allocation**: Track and manage allocated/unallocated IP spaces
- **Deleted Subnet Archive**: Recover information from deleted subnets
- **IP Validation**: Built-in validation ensures IP/CIDR configurations are valid

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
  - Issue ID tokens (application uses `ResponseType = "id_token"`)
  - Support `openid` and `profile` scopes
  - Issue tokens with one of these user identifier claims:
    - `preferred_username` (preferred)
    - `name` (ClaimTypes.Name)
    - `email` (ClaimTypes.Email)

- **Role-Based Authorization Requirements**:
  - Issue role claims that map to ASP.NET Core `ClaimTypes.Role`
  - Provide at least one of these roles for users to access the application:
    - `View` - Basic read access to subnet information
    - `Edit` - Permission to create and modify subnets
    - `Delete` - Permission to remove subnets

- **OIDC Configuration**:
  - Register BASTET as a client in your identity provider
  - Configure the following redirect URIs:
    - Login callback: `/signin-oidc` (relative to application base URL)
    - Logout callback: `/signout-callback-oidc` (relative to application base URL)
  - Set the environment variables:
    - `BASTET_OIDC_AUTHORITY`: URL of your OIDC provider
    - `BASTET_OIDC_CLIENT_ID`: Client ID registered with your provider

In development environments, authentication is automatically handled by `DevAuthHandler`, which provides a simulated user with all roles.

### Docker Deployment

BASTET is available as a Docker image through GitHub Container Registry:

1. **Pull the latest image**:
   ```bash
   docker pull ghcr.io/anujshroff/bastet:1
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
     ghcr.io/anujshroff/bastet:1
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
| Logging Configuration | **BASTET_LOG_LEVEL_DEFAULT** | Default logging level for all categories | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Logging Configuration | **BASTET_LOG_LEVEL_ASPNETCORE** | Logging level for ASP.NET Core components | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Logging Configuration | **BASTET_LOG_LEVEL_ENTITYFRAMEWORK** | Logging level for Entity Framework components | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |

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
