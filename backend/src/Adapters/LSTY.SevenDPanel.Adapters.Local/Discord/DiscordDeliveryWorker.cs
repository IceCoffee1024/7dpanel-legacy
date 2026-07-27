using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Discord;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public sealed class DiscordDeliveryWorker : IDisposable
    {
        private readonly IDiscordIntegrationStore store;
        private readonly IDiscordApiClient api;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan pollInterval;
        private readonly Action<string>? log;
        private readonly SemaphoreSlim activeDelivery = new SemaphoreSlim(1, 1);
        private bool disposed;

        public DiscordDeliveryWorker(
            IDiscordIntegrationStore store,
            IDiscordApiClient api,
            Func<DateTimeOffset> utcNow,
            TimeSpan pollInterval,
            Action<string>? log = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (pollInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            this.pollInterval = pollInterval;
            this.log = log;
        }

        public int RecoverInterrupted()
        {
            ThrowIfDisposed();
            var recovered = store.RecoverSendingAsResultUnknown(RequireUtc(utcNow()));
            if (recovered > 0) log?.Invoke("discord_delivery_recovered:" + recovered);
            return recovered;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            ThrowIfDisposed();
            RecoverInterrupted();
            while (!stoppingToken.IsCancellationRequested)
            {
                bool consumed;
                try
                {
                    consumed = await ProcessNextAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    log?.Invoke("discord_delivery_worker_failed");
                    consumed = false;
                }
                if (consumed) continue;
                try
                {
                    await Task.Delay(pollInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested) return false;
            try
            {
                await activeDelivery.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested) return false;
                var claimed = store.TryClaimNextDeliveryAttempt(RequireUtc(utcNow()));
                if (claimed == null) return false;
                await DeliverAsync(claimed, cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                activeDelivery.Release();
            }
        }

        public async Task<int> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drain.CancelAfter(timeout);
            var count = 0;
            while (!drain.IsCancellationRequested &&
                   await ProcessNextAsync(drain.Token).ConfigureAwait(false))
                count++;
            return count;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            activeDelivery.Dispose();
        }

        private async Task DeliverAsync(
            DiscordDeliveryWorkItem work,
            CancellationToken cancellationToken)
        {
            if (!TryCreateRequest(work.Delivery, out var request, out var errorCode))
            {
                Complete(work, DiscordDeliveryStatus.Failed, errorCode, null);
                return;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                Complete(work, DiscordDeliveryStatus.Cancelled, "discord_delivery_cancelled", null);
                return;
            }

            DiscordApiResult result;
            try
            {
                result = await SendWithCancellationBoundaryAsync(request!, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = DiscordApiResult.ResultUnknown();
            }
            catch
            {
                result = DiscordApiResult.ResultUnknown();
            }

            switch (result.Disposition)
            {
                case DiscordApiDeliveryDisposition.Succeeded:
                    Complete(work, DiscordDeliveryStatus.Succeeded, null, null);
                    break;
                case DiscordApiDeliveryDisposition.Retryable:
                    ScheduleRetryOrFail(work, result);
                    break;
                case DiscordApiDeliveryDisposition.Failed:
                    Complete(
                        work,
                        DiscordDeliveryStatus.Failed,
                        result.ErrorCode ?? "discord_delivery_failed",
                        null);
                    break;
                case DiscordApiDeliveryDisposition.ResultUnknown:
                default:
                    Complete(
                        work,
                        DiscordDeliveryStatus.ResultUnknown,
                        "discord_delivery_result_unknown",
                        null);
                    break;
            }
        }

        private async Task<DiscordApiResult> SendWithCancellationBoundaryAsync(
            DiscordApiRequest request,
            CancellationToken cancellationToken)
        {
            var send = api.SendAsync(request, cancellationToken);
            if (!cancellationToken.CanBeCanceled)
                return await send.ConfigureAwait(false);

            var cancelled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                var completed = await Task.WhenAny(send, cancelled.Task).ConfigureAwait(false);
                if (ReferenceEquals(completed, send))
                    return await send.ConfigureAwait(false);
            }

            _ = ObserveCompletionAsync(send);
            return DiscordApiResult.ResultUnknown();
        }

        private static async Task ObserveCompletionAsync(Task<DiscordApiResult> send)
        {
            try { await send.ConfigureAwait(false); }
            catch { }
        }

        private void ScheduleRetryOrFail(DiscordDeliveryWorkItem work, DiscordApiResult result)
        {
            var automaticRetryNumber = work.Delivery.RetryCount + 1;
            if (automaticRetryNumber > DiscordDeliveryPolicy.MaximumAutomaticRetries)
            {
                Complete(work, DiscordDeliveryStatus.Failed, "discord_retry_exhausted", null);
                return;
            }
            var delay = DiscordDeliveryPolicy.RetryDelay(
                automaticRetryNumber,
                result.RetryAfter);
            Complete(
                work,
                DiscordDeliveryStatus.RetryScheduled,
                result.ErrorCode ?? "discord_delivery_retry_scheduled",
                RequireUtc(utcNow()).Add(delay));
        }

        private bool TryCreateRequest(
            DiscordDelivery delivery,
            out DiscordApiRequest? request,
            out string errorCode)
        {
            request = null;
            errorCode = "discord_delivery_configuration_invalid";
            if (delivery.ContentText == null ||
                delivery.ContentText.Length < 1 ||
                delivery.ContentText.Length > 2000)
                return false;
            var target = store.FindTarget(delivery.TargetKey);
            if (target == null || !target.IsEnabled) return false;

            var settings = store.GetSettings();
            var proxy = Proxy(settings);
            if (string.Equals(target.DeliveryMode, "Webhook", StringComparison.Ordinal))
            {
                var secret = store.GetSecret(DiscordSecretKeys.WebhookUrl(target.TargetKey));
                if (secret == null) return false;
                request = DiscordApiRequest.Webhook(secret.SecretValue, delivery.ContentText, proxy);
                return true;
            }
            if (!string.Equals(target.DeliveryMode, "Bot", StringComparison.Ordinal)) return false;
            var token = store.GetSecret(DiscordSecretKeys.BotToken);
            var channelId = string.IsNullOrWhiteSpace(target.ChannelId)
                ? settings?.PublicChannelId
                : target.ChannelId;
            if (token == null || string.IsNullOrWhiteSpace(channelId)) return false;
            request = DiscordApiRequest.Bot(
                channelId!,
                token.SecretValue,
                delivery.ContentText,
                delivery.BusinessKey,
                proxy);
            return true;
        }

        private DiscordProxyConfiguration? Proxy(DiscordIntegrationSettings? settings)
        {
            if (settings == null || !settings.ProxyEnabled ||
                !Uri.TryCreate(settings.ProxyUri, UriKind.Absolute, out var endpoint))
                return null;
            var credentials = store.GetSecret(DiscordSecretKeys.ProxyCredentials)?.SecretValue;
            return new DiscordProxyConfiguration(endpoint, credentials);
        }

        private void Complete(
            DiscordDeliveryWorkItem work,
            DiscordDeliveryStatus status,
            string? errorCode,
            DateTimeOffset? nextAttemptAtUtc)
        {
            store.CompleteDeliveryAttempt(
                work.Delivery.DeliveryId,
                work.AttemptNumber,
                status,
                RequireUtc(utcNow()),
                errorCode,
                nextAttemptAtUtc);
            log?.Invoke(
                "discord_delivery:" + work.Delivery.DeliveryId + ":" + status +
                (errorCode == null ? string.Empty : ":" + errorCode));
        }

        private static DateTimeOffset RequireUtc(DateTimeOffset value) =>
            value.Offset == TimeSpan.Zero
                ? value
                : throw new InvalidOperationException("discord_worker_clock_not_utc");

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiscordDeliveryWorker));
        }
    }
}
