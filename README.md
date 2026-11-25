# EnterpriseLMS

EnterpriseLMS is a learning management system built with .NET 10. It provides a REST API for learning workflows and a Razor Pages web application for the user interface.

## Features

- User registration, authentication, and profile management
- Organization and membership management
- Course, section, and lesson authoring
- Course enrollment and progress tracking
- Lesson completion and course progress reporting
- Quizzes, questions, options, publishing, and scoring workflows
- Assignments and reporting
- JWT authentication and role-based authorization
- SQL Server persistence with Entity Framework Core
- Swagger/OpenAPI documentation for the API
- Rate limiting and private file storage support

## Solution structure

| Project | Description |
| --- | --- |
| `LMS.Api` | ASP.NET Core REST API and API middleware |
| `LMS.Web` | Razor Pages web application |
| `LMS.Application` | Application features, handlers, validators, and interfaces |
| `LMS.Domain` | Domain entities and business rules |
| `LMS.Infrastructure` | Entity Framework Core, SQL Server, Identity, repositories, and storage |
| `LMS.Tests` | Unit and integration-oriented test suite |

The solution follows a layered architecture:

```text
LMS.Web / LMS.Api
		|
LMS.Application
		|
LMS.Domain
		|
LMS.Infrastructure
```

## Requirements

- .NET 10 SDK
- SQL Server or SQL Server Express
- Visual Studio 2026 or another .NET-compatible IDE
- Optional: Docker for SQL Server integration tests

## Getting started

1. Clone the repository and move into the project directory:

   ```powershell
   git clone https://github.com/polojuanilkumar/EnterpriseLMS.git
   cd EnterpriseLMS
   ```

2. Restore dependencies:

   ```powershell
   dotnet restore .\EnterpriseLMS.slnx
   ```

3. Configure the database connection and application secrets. Use ASP.NET Core User Secrets for local development rather than committing credentials to source control:

   ```powershell
   dotnet user-secrets --project .\LMS.Api\LMS.Api.csproj set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=EnterpriseLMS;Trusted_Connection=True;TrustServerCertificate=True"
   ```

   Review `LMS.Api/appsettings.json` and `LMS.Web/appsettings.json` for the settings used by each application. Configure JWT and email/storage settings as required by the selected environment.

4. Apply Entity Framework Core migrations if required by the local database:

   ```powershell
   dotnet ef database update --project .\LMS.Infrastructure\LMS.Infrastructure.csproj --startup-project .\LMS.Api\LMS.Api.csproj
   ```

5. Build the solution:

   ```powershell
   dotnet build .\EnterpriseLMS.slnx
   ```

## Run the applications

Run the API:

```powershell
dotnet run --project .\LMS.Api\LMS.Api.csproj
```

Run the Razor Pages web application in a second terminal:

```powershell
dotnet run --project .\LMS.Web\LMS.Web.csproj
```

The local HTTP and HTTPS URLs are defined in the corresponding `Properties/launchSettings.json` files. When the API is running, Swagger is available in the development environment at `/swagger`.

## Testing

Run the complete test project:

```powershell
dotnet test .\LMS.Tests\LMS.Tests.csproj
```

The default tests do not require SQL Server. SQL Server integration tests are opt-in and require `LMS_SQL_INTEGRATION=1`; see [`LMS.Tests/README.md`](LMS.Tests/README.md) for the test design and additional instructions.

## Security and configuration

- Do not commit connection strings, JWT signing keys, passwords, user secrets, or private files.
- Use environment variables or ASP.NET Core User Secrets for local and deployment-specific values.
- Keep database credentials and signing keys outside `appsettings.json` in shared or production environments.
- Review NuGet restore warnings and vulnerability reports before deployment.

## Documentation

Additional feature and deployment documentation is available in the [`docs`](docs) directory.

## License

No license has been specified for this repository yet.
