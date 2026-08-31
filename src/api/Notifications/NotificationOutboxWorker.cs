using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed class NotificationOutboxProcessor(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<NotificationOutboxProcessor> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope(); QuestDbContext db = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
        NotificationOptions options = scope.ServiceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value; if (!options.Enabled) return false;
        Guid leaseId = Guid.NewGuid(); DateTimeOffset now = clock.GetUtcNow(); Guid? id = await ClaimAsync(db, leaseId, now, cancellationToken); if (id is null) return false;
        NotificationOutbox row = await db.NotificationOutbox.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        bool deliveryStarted = false;
        try
        {
            if (!Enum.TryParse(row.EventType, out NotificationEventType type)) { await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Failed, now, null, "MalformedEventType", "Unsupported event type", cancellationToken); return true; }
            try { NotificationRoutingPolicy.Validate(type, new(row.DestinationType, row.DestinationKey, row.RecipientParticipantId)); }
            catch (ArgumentException ex) { await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Failed, now, null, "InvalidDestination", Bounded(ex.Message, 1000), cancellationToken); return true; }
            NotificationFreshness freshness = await scope.ServiceProvider.GetRequiredService<INotificationFreshnessEvaluator>().EvaluateAsync(row, cancellationToken);
            if (!freshness.ShouldDeliver) { await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Suppressed, now, null, null, freshness.SuppressionReason, cancellationToken); return true; }
            if (row.DestinationType == NotificationDestinations.ParticipantPrivate && !options.PrivateDeliveryEnabled) { await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Suppressed, now, null, null, "PrivateDeliveryDisabled", cancellationToken); return true; }
            RenderedNotification rendered;
            try
            {
                NotificationPolicyDecision privacy = scope.ServiceProvider.GetRequiredService<INotificationPrivacyPolicy>().Evaluate(row);
                if (!privacy.Allowed) { await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Suppressed, now, null, null, privacy.SuppressionReason, cancellationToken); return true; }
                rendered = scope.ServiceProvider.GetRequiredService<INotificationRenderer>().Render(type, row.PayloadVersion, row.PayloadJson);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException) { await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Failed, now, null, "MalformedPayload", Bounded(ex.Message, 1000), cancellationToken); return true; }
            if (await MarkDeliveryStartedAsync(db, row.Id, leaseId, cancellationToken) != 1) return true;
            deliveryStarted = true;
            NotificationDeliveryResult result = await scope.ServiceProvider.GetRequiredService<INotificationDeliveryProvider>().DeliverAsync(new(row.EventId, type, row.DestinationType, row.DestinationKey, row.RecipientParticipantId, rendered), cancellationToken);
            await ApplyResultAsync(db, row, leaseId, now, result, cancellationToken); return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (deliveryStarted) await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.DeliveryUnknown, now, null, "DeliveryInterrupted", "Delivery was interrupted after provider invocation began.", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notification delivery attempt failed EventId={EventId} EventType={EventType} DestinationClass={DestinationClass}", row.EventId, row.EventType, row.DestinationType);
            if (deliveryStarted) await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.DeliveryUnknown, now, null, "UnexpectedProviderException", Bounded(ex.Message, 1000), CancellationToken.None);
            else await ApplyResultAsync(db, row, leaseId, now, new(NotificationDeliveryOutcome.RetryableFailure, Code: "UnhandledPreDeliveryFailure", Summary: Bounded(ex.Message, 1000)), CancellationToken.None);
            return true;
        }
    }

    private static async Task<Guid?> ClaimAsync(QuestDbContext db, Guid leaseId, DateTimeOffset now, CancellationToken ct)
    {
        DbConnection connection = db.Database.GetDbConnection(); await connection.OpenAsync(ct); await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (DbCommand recover = connection.CreateCommand())
        {
            recover.Transaction = transaction; recover.CommandText = "UPDATE [NotificationOutbox] SET [Status]='RetryPending',[DeliveryPhase]=NULL,[NextAttemptAt]=DATEADD(minute,1,@now),[LeaseId]=NULL,[LeaseExpiresAt]=NULL,[LastErrorCode]='PreDeliveryLeaseExpired',[LastErrorSummary]='Pre-delivery lease expired before provider invocation.' WHERE [Status]='Processing' AND [DeliveryPhase]='PreDelivery' AND [LeaseExpiresAt] < @now; UPDATE [NotificationOutbox] SET [Status]='DeliveryUnknown',[DeliveryPhase]=NULL,[CompletedAt]=@now,[LeaseId]=NULL,[LeaseExpiresAt]=NULL,[LastErrorCode]='DeliveryStartedLeaseExpired',[LastErrorSummary]='Lease expired after provider invocation started.',[TerminalReason]='DeliveryStartedLeaseExpired' WHERE [Status]='Processing' AND [DeliveryPhase]='DeliveryStarted' AND [LeaseExpiresAt] < @now"; Add(recover, "@now", now); await recover.ExecuteNonQueryAsync(ct);
        }
        Guid? claimed = null;
        await using (DbCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = ";WITH [candidate] AS (SELECT TOP (1) * FROM [NotificationOutbox] WITH (UPDLOCK,READPAST,ROWLOCK) WHERE [Status] IN ('Pending','RetryPending') AND [NextAttemptAt] <= @now ORDER BY [NextAttemptAt],[CreatedAt],[Id]) UPDATE [candidate] SET [Status]='Processing',[DeliveryPhase]='PreDelivery',[AttemptCount]=[AttemptCount]+1,[LastAttemptAt]=@now,[LeaseId]=@leaseId,[LeaseExpiresAt]=@leaseExpires OUTPUT INSERTED.[Id];";
            Add(command, "@now", now); Add(command, "@leaseId", leaseId); Add(command, "@leaseExpires", now.Add(LeaseDuration)); object? value = await command.ExecuteScalarAsync(ct); if (value is Guid id) claimed = id;
        }
        await transaction.CommitAsync(ct); return claimed;
    }

    private static async Task ApplyResultAsync(QuestDbContext db, NotificationOutbox row, Guid leaseId, DateTimeOffset now, NotificationDeliveryResult result, CancellationToken ct)
    {
        switch (result.Outcome)
        {
            case NotificationDeliveryOutcome.Delivered: await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Sent, now, result.ProviderMessageId, null, null, ct); break;
            case NotificationDeliveryOutcome.Captured: await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Captured, now, result.ProviderMessageId, null, null, ct); break;
            case NotificationDeliveryOutcome.PermanentFailure: await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Failed, now, null, result.Code ?? "PermanentFailure", result.Summary, ct); break;
            case NotificationDeliveryOutcome.DeliveryUnknown: await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.DeliveryUnknown, now, null, result.Code ?? "DeliveryUnknown", result.Summary, ct); break;
            case NotificationDeliveryOutcome.RetryableFailure:
                if (row.AttemptCount >= 5) await CompleteAsync(db, row.Id, leaseId, NotificationStatuses.Failed, now, null, result.Code ?? "RetryLimitExceeded", result.Summary ?? "Maximum delivery attempts reached.", ct);
                else await RetryAsync(db, row.Id, leaseId, now + (result.RetryAfter ?? RetryDelay(row.AttemptCount)), result.Code, result.Summary, ct);
                break;
        }
    }
    private static TimeSpan RetryDelay(int attempt) => attempt switch { <= 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), _ => TimeSpan.FromMinutes(60) };
    private static Task<int> MarkDeliveryStartedAsync(QuestDbContext db, Guid id, Guid leaseId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE [NotificationOutbox] SET [DeliveryPhase]='DeliveryStarted' WHERE [Id]={id} AND [Status]='Processing' AND [DeliveryPhase]='PreDelivery' AND [LeaseId]={leaseId}", ct);
    private static Task<int> CompleteAsync(QuestDbContext db, Guid id, Guid leaseId, string status, DateTimeOffset at, string? providerId, string? errorCode, string? reason, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE [NotificationOutbox] SET [Status]={status},[DeliveryPhase]=NULL,[CompletedAt]={at},[ProviderMessageId]={Bounded(providerId, 200)},[LastErrorCode]={Bounded(errorCode, 100)},[LastErrorSummary]={Bounded(reason, 1000)},[TerminalReason]={Bounded(reason, 500)},[LeaseId]=NULL,[LeaseExpiresAt]=NULL WHERE [Id]={id} AND [Status]='Processing' AND [LeaseId]={leaseId}", ct);
    private static Task<int> RetryAsync(QuestDbContext db, Guid id, Guid leaseId, DateTimeOffset next, string? code, string? summary, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE [NotificationOutbox] SET [Status]='RetryPending',[DeliveryPhase]=NULL,[NextAttemptAt]={next},[LastErrorCode]={Bounded(code, 100)},[LastErrorSummary]={Bounded(summary, 1000)},[LeaseId]=NULL,[LeaseExpiresAt]=NULL WHERE [Id]={id} AND [Status]='Processing' AND [LeaseId]={leaseId}", ct);
    private static string? Bounded(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
    private static void Add(DbCommand command, string name, object value) { DbParameter p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
}

public sealed class NotificationOutboxBackgroundService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NotificationOutboxProcessor> processorLogger, ILogger<NotificationOutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = new NotificationOutboxProcessor(scopes, clock, processorLogger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (!await processor.ProcessOnceAsync(stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Notification outbox iteration failed."); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }
}
