# BASTET - Badass Subnetting Tools for Enhanced Transmission

BASTET is a modern, web-based subnet management system that helps network administrators efficiently design, organize, and visualize IP subnet hierarchies. It provides powerful tools for subnet calculations, relationship tracking, and IP allocation management.

## Features

- **Subnet Hierarchy Management**: Create and manage parent-child relationships between subnets
- **Automatic Subnet Calculations**: View subnet masks, broadcast addresses, usable IP ranges
- **Visual Subnet Hierarchy**: Tree-based visualization of your subnet structure
- **IP Address Allocation**: Track and manage allocated/unallocated IP spaces
- **Deleted Subnet Archive**: Recover information from deleted subnets
- **IP Validation**: Built-in validation ensures IP/CIDR configurations are valid
- **Azure Integration**: Import subnets per-target or in bulk from Azure Virtual Networks, and reconcile against Azure to find subnets whose VNets have been deleted

## Technologies

- **ASP.NET Core MVC** (.NET 10.0)
- **Entity Framework Core**
- **SQL Server**
- **Bootstrap for responsive UI**
- **JavaScript/jQuery**

## Prerequisites

- **.NET 10.0 SDK**
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
| Database Configuration | **BASTET_AUTO_MIGRATE** | Controls whether database migrations are automatically applied on startup | `true` or `false` | `false` | If true, app will need to be able to modify the schema. If false, db_datareader and db_datawriter would be sufficient. Safe with multiple replicas: startup takes a `Bastet:Migration` application lock so only one replica applies migrations while the others wait (up to 5 minutes). |
| Server Configuration | **ASPNETCORE_URLS** | Configures the URLs and ports the application will listen on | `http://+:5000` | ? | In development environments, defaults to settings in launchSettings.json |
| Server Configuration | **WEBSITES_PORT** | Tells Azure App Service which port the app is listening on | `5000` | - | Should match the port specified in ASPNETCORE_URLS when deployed to Azure App Service |
| Server Configuration | **BASTET_CORS_ORIGINS** | Comma-separated list of origins allowed to make cross-origin requests | `https://app.your-domain.com,https://other.your-domain.com` | - | Optional. CORS is disabled unless set, which suits normal use since BASTET serves its own UI and all of its requests are same-origin. Credentials are never allowed, so cross-origin callers are not authenticated. |
| Server Configuration | **BASTET_FRAME_ANCESTORS** | Value for the `Content-Security-Policy: frame-ancestors` response header, controlling who may embed BASTET in a frame | `'self'` or `https://portal.your-domain.com` | `'none'` | Optional. Defaults to `'none'` (no embedding, clickjacking protection). Set to `'self'` or a space-separated list of origins to allow embedding; `X-Frame-Options: DENY` is emitted only when this is `'none'`. |
| Server Configuration | **AZURE_CLIENT_ID** | Specifies the client ID for Azure Managed Identity | `123e4567-e89b-12d3-a456-426614174000` | - | Required when using Managed Identity in Azure for server authentication to SQL server. |
| Server Configuration | **AZURE_TOKEN_CREDENTIALS** | Restricts which credentials DefaultAzureCredential will try | `dev`, `prod`, or a credential name such as `AzureCliCredential` | - | Local development only. Set to `dev` to authenticate with `az login`; leave unset when deployed so Managed Identity is used. See [Local development](#local-development). |
| Authentication Configuration | **BASTET_OIDC_CLIENT_ID** | OpenID Connect client ID | `mvc_client` or `0e0e7c73-5fce-45c1-be7c-0161f462fd9d` | `mvc_client` | Required in non-development environments. Authentication is disabled in development environments. |
| Authentication Configuration | **BASTET_OIDC_AUTHORITY** | OpenID Connect authority URL | `https://identity.your-domain.com` or `https://login.microsoftonline.com/0af80680-dd36-43bf-bf53-b951b9fdd68b` | `https://localhost` | Required in non-development environments. Authentication is disabled in development environments. |
| Authentication Configuration | **BASTET_OIDC_CLIENT_SECRET** | Client secret for authentication with the OIDC provider | `your-client-secret` | - | Required when using authorization code flow with providers that require client authentication (e.g., Microsoft Entra ID). Not needed for providers that support PKCE without client authentication (e.g., Auth0). |
| Authentication Configuration | **BASTET_OIDC_RESPONSE_TYPE** | OIDC response type | `id_token` or `code` | `code` | Controls the authentication flow: `id_token` for implicit flow, `code` for authorization code flow. Set to `code` when using PKCE. |
| Logging Configuration | **BASTET_LOG_LEVEL_DEFAULT** | Default logging level for all categories | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Logging Configuration | **BASTET_LOG_LEVEL_ASPNETCORE** | Logging level for ASP.NET Core components | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Logging Configuration | **BASTET_LOG_LEVEL_ENTITYFRAMEWORK** | Logging level for Entity Framework components | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Warning` | Only applied in non-development environments. In development, falls back to appsettings.json. |
| Feature Configuration | **BASTET_AZURE_IMPORT** | Enables the Azure integration | `true` or `false` | `false` | When enabled, admin users can import subnets from Azure VNets and run Azure Reconcile. Gates the Subnet Azure Import, Bulk Azure Import, and Azure Reconcile flows. |

## Azure Integration

BASTET includes three flows for keeping subnets in step with Azure Virtual Networks: two for importing, and one for finding what Azure no longer has. All are admin-only and gated by the `BASTET_AZURE_IMPORT` environment variable. Imported subnets retain their source Azure Resource ID and the Subnet Details page links back to the matching VNet (or VNet's subnet list) in the Azure portal.

| Flow | Direction | Use it to |
|------|-----------|-----------|
| [Subnet Azure Import](#subnet-azure-import-per-subnet) | Azure → BASTET | Fill in one empty BASTET subnet from the matching Azure VNet |
| [Bulk Azure Import](#bulk-azure-import-across-the-tree) | Azure → BASTET | Import many VNets and subnets across the whole tree in one transaction |
| [Azure Reconcile](#azure-reconcile-find-what-azure-deleted) | Azure → BASTET | Find imported subnets whose Azure VNet or subnet has since been deleted, and remove them |

### Prerequisites

- **Azure Authentication**: The application uses DefaultAzureCredential for authentication to Azure, which tries the following methods in order:
  - Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET)
  - Workload Identity
  - Managed Identity
  - Visual Studio authentication
  - Azure CLI authentication
  - Azure PowerShell authentication
  - Azure Developer CLI authentication

#### Local development

Sign in with `az login`, then set `AZURE_TOKEN_CREDENTIALS=dev` so DefaultAzureCredential only tries the developer tool credentials. The `dotnet run` launch profiles in `src/Bastet/Properties/launchSettings.json` already set this.

This is a **temporary workaround for an Azure.Core regression**, not a permanent requirement. On a machine where the Managed Identity endpoint (169.254.169.254) is unreachable, `ManagedIdentityCredential` throws `AuthenticationFailedException` instead of reporting itself unavailable. Because Managed Identity is a deployed-service credential, that *terminates* the chain before Azure CLI is ever tried, so `az login` is ignored and every Azure call fails after ~27s of IMDS retries.

Azure.Core 1.59.0 introduced this with its managed identity host capability detection; 1.60.0 attempted a fix that does not cover the unreachable-socket path. Azure.Core is a transitive dependency pulled in by `Azure.ResourceManager.Network` (1.16.1 requires `>= 1.60.0`), so it is not pinned in `Bastet.csproj`. Once a release after 1.60.0 completes the fix, `AZURE_TOKEN_CREDENTIALS` can be removed from the launch profiles. See the [Azure.Core changelog](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/CHANGELOG.md) and [credential chains](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains).

### Enabling Azure Import

1. Set the environment variable `BASTET_AZURE_IMPORT=true`
2. Ensure proper Azure authentication is configured
3. Restart the application

### Subnet Azure Import (per-subnet)

Import children of a single, empty Bastet subnet whose address space exactly matches an Azure VNet's prefix.

1. Navigate to a subnet's details page
2. If the subnet has no child subnets or host IP assignments, a "Subnet Azure Import" button will appear (admin role required)
3. Click "Subnet Azure Import" to start the import wizard
4. Follow the multi-step process:
   - Select an Azure Subscription
   - Choose a compatible Virtual Network
   - Select specific subnets to import
5. The selected Azure subnets will be imported as child subnets

### Bulk Azure Import (across the tree)

Import many VNets and their IPv4 subnets in one transaction, applied across the entire BASTET tree. Available from the top-level "Bulk Azure Import" nav link.

1. Click "Bulk Azure Import" in the top navigation bar (admin role required)
2. Pick an Azure subscription
3. Tree-select the VNets and IPv4 subnets you want to import. Anything BASTET already has is greyed out and cannot be selected, so re-importing a subscription only offers what is genuinely new:
   - **Already imported** — imported from this exact Azure resource
   - **Cannot import** — the address is already used by another BASTET subnet, or the target subnet already has children, host IPs, or is fully allocated
   - **Will update existing** — the VNet prefix matches a BASTET subnet that will receive the import

   Use the **Hide already imported** switch to show only what is still importable.
4. Review the server-computed plan — every conflict (overlapping prefixes, would-create-invalid-hierarchy, target subnet already populated, etc.) is surfaced before commit and blocks it
5. Commit — all imports are applied in a single database transaction (all-or-nothing)

The planner matches each selected VNet IPv4 prefix to an existing BASTET subnet (exact match → deepest container → top-level), and either populates that subnet or auto-creates a new one. Scope per session is one subscription, IPv4 only.

**Fully-encompassing subnets:** when an Azure subnet covers its VNet's entire address prefix (e.g. a `10.11.0.0/24` VNet whose only subnet is also `10.11.0.0/24`), it is not created as a child. Instead the target subnet is marked **fully allocated**, since there is no free space left in it. That produces one BASTET subnet, not two.

### Azure Reconcile (find what Azure deleted)

VNets and address prefixes get deleted in Azure over time, but the subnets they created in BASTET stay. A stale subnet also blocks re-importing its address, because BASTET requires network address + CIDR to be unique. Azure Reconcile finds them.

1. Click "Azure Reconcile" in the top navigation bar (admin role required)
2. Pick an Azure subscription — only subnets imported from **that** subscription are considered
3. Review what is reported:

   | Status | Meaning |
   |--------|---------|
   | **VNet deleted** | The VNet this subnet was imported from no longer exists |
   | **Prefix removed** | The VNet still exists but no longer has this address prefix |
   | **Subnet deleted** | The Azure subnet this was imported from no longer exists |
   | **Prefix changed** | The Azure subnet still exists but has been re-addressed |
   | **Needs review** | Reported but not deletable — see below |

4. Select what to remove. Rows that would archive child subnets or host IP assignments show a cascade count first
5. Type `approved` to confirm. Deletions run in one transaction and the subnets are archived to Deleted Subnets, not destroyed

Only subnets carrying an Azure Resource ID are ever considered, so subnets you created by hand are never touched — as are subnets imported from a subscription other than the one being scanned.

**Safety:** if Azure cannot be read (expired credentials, a transient outage), reconcile reports the error and offers **nothing** for deletion. An empty answer from Azure and an unanswered question are not the same thing, and only one of them means "everything was deleted".

**Needs review** covers drift that deleting cannot fix. If a fully-encompassing Azure subnet is deleted but its VNet and prefix survive, the BASTET subnet stays marked fully allocated with nothing backing that flag. Reconcile reports it and leaves it alone — the flag can also be set by hand, so it is never cleared automatically.

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

This project was almost entirely generated using AI, leveraging various Claude models from Anthropic across tools including **Cline** and **Claude Code**. It serves as a testament to the capabilities of modern AI in automating complex development tasks and streamlining the software creation process.
