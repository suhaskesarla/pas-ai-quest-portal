# Teams notification activation

## Code complete

The portal contains the transactional outbox, seven BA-017 integrations, Capture provider, TeamsBot transport, trusted Bot Connector URL enforcement, single-tenant routing, authenticated `/api/teams/messages` activity capture, private conversation references, deep links, and manager leaderboard command.

## Values the tenant administrator must provide

- PAS tenant ID and single-tenant bot application/client ID.
- Bot client credential supplied through the deployment secret store as `Notifications__TeamsBot__ClientSecret`.
- Public HTTPS portal URL and bot messaging endpoint URL (`https://<api-host>/api/teams/messages`).
- General and Manager Bot Connector `serviceUrl` and `conversationId` values established by authenticated Teams installation activity.
- Teams app catalog permissions and final color/outline PNG icons.
- Verified participant mappings using PAS tenant ID plus Entra `oid`; never email or display name.

## Tenant activation required

1. Register the single-tenant Entra/Bot application and record its application ID and PAS tenant ID.
2. Configure the Azure Bot messaging endpoint as `https://<api-host>/api/teams/messages`.
3. Configure `Notifications:TeamsBot:MicrosoftAppId`, `Notifications:TeamsBot:TenantId`, and the bot credential through the deployment secret store. The activity recipient must be the real Teams channel-account form `28:<MicrosoftAppId>`.
4. Replace manifest tokens, add the required PNG icons, package `teams-app`, and upload or publish it in the tenant Teams catalog.
5. Install/add the bot in the intended General team/channel.
6. Let the authenticated Teams installation activity reach `POST /api/teams/messages`; the portal validates Bot authentication, tenant, `28:<MicrosoftAppId>`, and the trusted Bot Connector service URL, then stores a verified channel candidate.
7. As a `Quest.Manager`, list verified values with `GET /api/manager/teams/destination-candidates`.
8. Assign the selected candidate with `POST /api/manager/teams/destinations/QUEST_GENERAL_AUDIENCE` and body `{ "candidateId": "<verified-candidate-guid>" }`. Copy only the returned verified routing values into secure deployment configuration when enabling the provider.
9. Repeat steps 5–8 for `QUEST_MANAGER_AUDIENCE`; never manually invent a service URL or conversation ID.
10. Provision the verified participant identity as a `Quest.Manager` with `POST /api/manager/teams/external-identities` and body `{ "participantId": "<durable-participant-guid>", "tenantId": "<approved-tenant-guid>", "oid": "<entra-object-guid>" }`. Exact replay is safe; conflicting mappings fail.
11. Install/open the bot in that user's personal scope.
12. Let the authenticated personal activity reach `POST /api/teams/messages`; it creates or updates the conversation reference only when the verified `(tenantId, oid)` mapping exists.
13. Verify the `Participant -> ParticipantExternalIdentity -> TeamsConversationReference` chain through the supported manager operation and sanitized operational logs; do not directly insert conversation references.
14. Set `Notifications:Enabled=true` and `Notifications:Provider=TeamsBot`. Startup fails if credentials, tenant, or either audience destination is incomplete, cross-tenant, or untrusted.
15. Publish a synthetic challenge and verify the General notification reaches `Sent` with a provider message ID.
16. Submit synthetic work and verify the Manager notification reaches `Sent`.
17. Keep `Notifications:PrivateDeliveryEnabled=false` until participant mappings and personal references have been verified.
18. Test one private synthetic participant notification, then enable private delivery.
19. Keep `Notifications:RealUserLeaderboardEnabled=false` until display-name and XP privacy approval is recorded.
20. Run the publish, submit, NeedsEvidence, resubmit, approve, reject, and privacy-approved leaderboard smoke sequence and verify each outbox terminal state and HTTPS deep link.

## Operator verification

- Identity mapping: confirm exactly one verified `ParticipantExternalIdentity` for `(Provider=Entra, tenantId, oid)` and the intended durable participant.
- Conversation reference: confirm one active reference for that identity, matching the configured tenant; never copy arbitrary service URLs from an untrusted source.
- Audience destinations: confirm General and Manager tenant IDs match the configured bot tenant and their service URL host is exactly `smba.trafficmanager.net`.
- Capture: in Development/Test only, use `/api/dev/notifications/captured`; it exposes rendered notification diagnostics, not conversation references.
- Live provider: confirm the outbox row is `Sent` and has a provider message ID. `DeliveryUnknown` requires operator investigation and must not be blindly replayed.

Entra/MSAL portal login and Teams tab SSO remain separate work; the manifest does not claim SSO is active. Do not insert arbitrary conversation references directly in SQL as a normal activation procedure.
