# Local development

Step 2 provides a local-only scaffold for the React frontend, ASP.NET Core API, SQL Server, Azurite, and stub authentication. It deliberately contains no domain model or EF Core migrations.

## Prerequisites

- Git
- Docker Desktop with Linux containers and Docker Compose

No Azure subscription, Azure resources, local .NET SDK, or local Node.js installation is required for the Docker workflow.

## Start everything

1. Clone the repository and enter its directory.
2. Copy `.env.example` to `.env`.
3. Replace `SQL_SERVER_PASSWORD` in `.env` with a strong local-only SQL Server password. Keep `.env` uncommitted.
4. Run:

   ```powershell
   docker compose up --build
   ```

5. Open <http://localhost:5173>. The page should report `API: healthy`.
6. Check the API directly:

   ```powershell
   Invoke-RestMethod http://localhost:8080/health
   Invoke-WebRequest http://localhost:8080/health/ready
   Invoke-RestMethod http://localhost:8080/api/whoami
   ```

`/health` confirms the API process is running. `/health/ready` additionally checks SQL Server and Azurite. `/api/whoami` shows the development identity and its `Quest.Participant` and `Quest.Manager` roles.

Stop the services with `docker compose down`. Named volumes preserve local SQL Server and Azurite data. To intentionally discard only this project's emulator data, run `docker compose down --volumes`.

## Configuration boundaries

All deployable settings use ASP.NET Core configuration and can be overridden with environment variables (`__` separates nested keys):

| Setting | Local value | Deployment value later |
|---|---|---|
| `ConnectionStrings__QuestDatabase` | Docker SQL Server | Azure SQL connection configuration |
| `Storage__ConnectionString` | Azurite connection | secret-backed Azure Blob connection configuration |
| `Storage__BlobServiceUri` | Azurite endpoint | Azure Blob service endpoint |
| `Authentication__Mode` | `Stub` | `Entra` after playbook Step 5 |

The committed settings contain no usable password or production credential. Compose obtains the SQL password from the ignored `.env` file.

The stub identity is a development seam, not production authentication. Its subject, display name, and roles come from configuration. Step 5 will supply the real Entra authentication implementation and app-role validation without changing domain endpoints.

## Running apps outside Docker

Developers who have .NET 8 and Node.js installed may run SQL Server and Azurite through Compose, then run:

```powershell
dotnet run --project src/api/PAS.AIQuestPortal.Api.csproj
Set-Location src/web
npm install
npm run dev
```

Override the API connection string with user secrets or environment variables; do not put a real password into `appsettings*.json`.
