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
| `Authentication__Mode` | `Demo` | `Entra` |

The committed settings contain no usable password or production credential. Compose obtains the SQL password from the ignored `.env` file.

Demo identity is a Development/Test-only seam. Its subject, display name, and roles come from the fixed server configuration. Production rejects Demo mode and never falls back to it.

## Running apps outside Docker

Developers who have .NET 8 and Node.js installed may run SQL Server and Azurite through Compose, then run:

```powershell
dotnet run --project src/api/PAS.AIQuestPortal.Api.csproj
Set-Location src/web
npm install
npm run dev
```

Override the API connection string with user secrets or environment variables; do not put a real password into `appsettings*.json`.

## Entra activation

ENTRA FRONTEND AND BACKEND CODE: **READY**

LIVE ENTRA APP CONFIGURATION: **PENDING**

The production frontend fails closed unless all of these Vite build/runtime values are supplied:

| Setting | Purpose |
|---|---|
| `VITE_ENTRA_TENANT_ID` | Microsoft Entra tenant ID |
| `VITE_ENTRA_CLIENT_ID` | SPA application (client) ID |
| `VITE_ENTRA_REDIRECT_URI` | Registered SPA redirect URI |
| `VITE_ENTRA_API_SCOPE` | Delegated scope exposed by the PAS AI Quest API |

The API requires these server settings:

| Setting | Purpose |
|---|---|
| `Authentication__Mode=Entra` | Select JWT bearer authentication; no Demo handler or Demo endpoints are registered |
| `Authentication__Entra__TenantId` | The single approved corporate tenant GUID |
| `Authentication__Entra__Audience` | The API audience (`api://<api-app-id>` or the audience configured on the API app registration) |
| `Authentication__Entra__RequiredScope` | Delegated scope name required in the access token, normally `access_as_user`; app-only tokens are rejected |
| `Authentication__Entra__AuthorityHost` | HTTPS Entra authority host; normally `https://login.microsoftonline.com` |

Use two app registrations unless tenant architecture explicitly approves a combined registration. The SPA registration's client ID is `VITE_ENTRA_CLIENT_ID`; it is a public client and owns the redirect URI. The API registration exposes a delegated scope (for example `api://<api-app-id>/access_as_user`), whose full value is `VITE_ENTRA_API_SCOPE`. The API validates the resulting token against `Authentication__Entra__Audience`, which identifies the API registration—not the SPA client ID. No client secret is needed for API access-token validation.

Provision each portal user explicitly through the existing manager-owned `POST /api/manager/teams/external-identities` operation so a verified `ParticipantExternalIdentity` contains `Provider=Entra`, the approved tenant ID, the token `oid`, and the durable Participant ID. Login never creates or changes this mapping. Valid Entra authentication with `(tenantId, oid)` mapped to an active Participant grants `Quest.Participant` without requiring an Entra `Quest.Participant` app role. The exact `Quest.Manager` app role in the validated token grants Manager capability only after that Participant resolution; Manager also has Participant capability. Missing or unknown app roles never grant Manager access.

These are public SPA settings; never add a client secret to frontend configuration. The redirect URI must also be registered on the Entra SPA application. The API app registration, delegated scope, backend JWT validation, durable tenant/object-ID provisioning, and live tenant values must be activated together before real sign-in can complete.

The frontend treats an authenticated `200` from `/api/auth/me` with `participantId: null` and no roles as unprovisioned. A mapped but inactive/invalid Participant receives `403`. A `403` from any ordinary feature endpoint remains local to that feature and does not clear the signed-in portal profile.

For deterministic local development and automated tests only, set `VITE_APP_ENVIRONMENT=Development` (or `Test`) together with `VITE_DEMO_AUTH_ENABLED=true` to select the existing synthetic demo authentication. The normal Docker Compose build supplies `Development` explicitly. A production deployment must use `VITE_APP_ENVIRONMENT=Production` (the Dockerfile default). Missing or unknown environment values fail closed to Entra mode; a demo flag alone never enables demo authentication in Production. Vite's optimized build mode is not used as the deployment security signal.
