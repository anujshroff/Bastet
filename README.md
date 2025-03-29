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

1. Clone the repository
   ```
   git clone https://github.com/anujshroff/bastet.git
   cd bastet
   ```

2. Configure the database connection in `appsettings.json`
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=Bastet;Trusted_Connection=True;MultipleActiveResultSets=true"
   }
   ```

3. Apply database migrations
   ```
   dotnet ef database update
   ```

4. Run the application
   ```
   dotnet run
   ```

5. Access the application at `http://localhost:5139` (or the configured port)

## Environment Variables

BASTET supports configuration through environment variables, following a consistent BASTET_ prefix naming convention:

### Database Configuration

- **BASTET_CONNECTION_STRING**: Database connection string
  - Example: `Data Source=myserver;Initial Catalog=bastet;User Id=myuser;Password=mypassword;`
  - Required in production environments
  - In development, falls back to appsettings.json if not specified

### Authentication Configuration

- **BASTET_OIDC_CLIENT_ID**: OpenID Connect client ID
  - Example: `mvc_client`
  - Default: `mvc_client`

- **BASTET_OIDC_AUTHORITY**: OpenID Connect authority URL
  - Example: `https://identity.example.com`
  - Default: `https://localhost`

### Database Migration

- **BASTET_AUTO_MIGRATE**: Controls whether database migrations are automatically applied on startup
  - Values: `true` or `false`
  - Default: `false`

### Logging Configuration

- **BASTET_LOG_LEVEL_DEFAULT**: Default logging level for all categories
  - Values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`
  - Default in production: `Warning`
  - Only applied in non-development environments

- **BASTET_LOG_LEVEL_ASPNETCORE**: Logging level for ASP.NET Core components
  - Values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`
  - Default in production: `Warning`
  - Only applied in non-development environments

- **BASTET_LOG_LEVEL_ENTITYFRAMEWORK**: Logging level for Entity Framework components
  - Values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`
  - Default in production: `Warning`
  - Only applied in non-development environments

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
