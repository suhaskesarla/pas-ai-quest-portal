# POC cleanup and cost safety

## Safe default

The recommended SQL configuration is:

```text
useFreeSql = true
freeLimitExhaustionBehavior = AutoPause
```

This prioritises avoiding accidental SQL overage.

The App Service default is:

```text
F1
```

which is for POC/testing.

## Destroy

Queue:

```text
action = destroy
confirmDestroy = DELETE
```

The pipeline deletes the dedicated POC resource group.

Because the POC places all normal Azure resources inside that group, this removes:

- App Service Plan
- both Web Apps
- SQL server/database
- Storage
- Key Vault resource
- Log Analytics
- Application Insights

The cleanup script also attempts to purge the soft-deleted Key Vault.

## Not removed

The following are not resource-group resources and therefore are not deleted:

- Entra app registrations
- Entra service principals
- Entra app-role assignments
- tenant consent grants
- Teams custom-app installation/configuration

Delete those separately only when you are certain they are no longer needed.

## Why there is an explicit `DELETE` confirmation

Resource-group deletion is intentionally destructive.

The pipeline should never delete the POC because someone accidentally selected the wrong action.

Therefore both are required:

```text
action = destroy
confirmDestroy = DELETE
```
