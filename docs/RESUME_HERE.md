# PAS AI Quest — Resume Here

This is a starting point for the next development or hackathon session. For detailed scope and open items, read [Delivery Status](DELIVERY_STATUS.md) and the frozen [Portal Spec](PORTAL_SPEC.md), [Technical Architecture](TECHNICAL_ARCHITECTURE.md), [Decisions](DECISIONS.md), and [Build Playbook](BUILD_PLAYBOOK.md). Verify the current repository and environment before treating an older test result as current acceptance.

## Checkpoint

As of 14 September 2026, the Entra authentication and Azure App Service POC deployment changes are merged into `main` (PRs #9 and #10). The portal, Teams notification integration, and Entra authentication have implementation and local test coverage. Local Docker uses Development/Demo authentication, so a real tenant is not required to resume development or demonstrate the local flows.

The Azure POC automation has been reviewed against the repository, but it has **not** been run against a live Azure subscription. Live Azure deployment, Entra tenant/app registration and user provisioning, Teams tenant/bot activation, and corporate Production deployment remain open. Do not treat the POC hosting or pipeline as the frozen corporate Production design: the POC uses App Service and Azure DevOps; the frozen Production architecture uses Azure Static Web Apps and GitHub Actions. See [Azure POC deployment](../README-AZURE-DEPLOYMENT.md).

## Run locally

Follow [Local Development](LOCAL_DEVELOPMENT.md): copy `.env.example` to `.env`, set a strong local `SQL_SERVER_PASSWORD`, then run `docker compose up --build -d` and open <http://localhost:5173>. Keep `.env` uncommitted. The normal Compose configuration uses Demo authentication with synthetic identities.

## Activate live services only when needed

- **Azure POC:** Start with the [deployment guide](../README-AZURE-DEPLOYMENT.md) and [implementation notes](infra_deploy/AZURE_POC_IMPLEMENTATION.md). Treat the first provisioning and deployment run as live POC validation.
- **Entra:** Follow the [Entra activation settings](LOCAL_DEVELOPMENT.md#entra-activation) for the SPA/API registrations, delegated scope, redirect URI, and verified participant mappings. Live tenant sign-in still needs an end-to-end smoke test.
- **Teams:** Follow [Teams Activation](TEAMS_ACTIVATION.md) for bot registration, app installation, verified destinations and personal conversations, credentials, and live notification checks.

## Authorization model

Valid Entra authentication with `(tenantId, oid)` resolved through a verified `ParticipantExternalIdentity` mapping to an active Participant grants `Quest.Participant`. **No Entra `Quest.Participant` app role is required.** `Quest.Manager` additionally requires the exact `Quest.Manager` app role in the validated Entra token. Manager access never bypasses the Participant mapping.

When resuming, pull the latest `main`, read the linked status and setup documents, run Docker locally, then decide whether the event needs only Demo authentication or live Azure, Entra, and Teams activation. Do not infer that unexecuted live deployment or the remaining items in Delivery Status are complete.
