using System;

namespace LSTY.SevenDPanel.Application.Discord
{
    public sealed class EnqueueDiscordDeliveryUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> createDeliveryId;

        public EnqueueDiscordDeliveryUseCase(
            IDiscordIntegrationStore store,
            Func<DateTimeOffset> utcNow,
            Func<string> createDeliveryId)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.createDeliveryId = createDeliveryId ?? throw new ArgumentNullException(nameof(createDeliveryId));
        }

        public DiscordDeliverySummary Execute(
            string businessKey,
            string targetKey,
            string contentText)
        {
            RequireContent(contentText);
            if (string.IsNullOrWhiteSpace(businessKey) || string.IsNullOrWhiteSpace(targetKey))
                throw new DiscordDeliveryValidationException();
            var deliveryId = createDeliveryId();
            if (string.IsNullOrWhiteSpace(deliveryId))
                throw new DiscordDeliveryValidationException();
            var now = RequireUtc(utcNow());
            var result = store.EnqueueDelivery(new DiscordDelivery(
                deliveryId,
                businessKey.Trim(),
                targetKey.Trim(),
                DiscordDeliveryStatus.Pending,
                contentText,
                "discord_message:" + contentText.Length,
                null,
                0,
                now,
                null));
            return DiscordDeliverySummary.FromDelivery(result.Delivery);
        }

        internal static void RequireContent(string contentText)
        {
            if (contentText == null || contentText.Length < 1 || contentText.Length > 2000)
                throw new DiscordDeliveryValidationException();
        }

        internal static DateTimeOffset RequireUtc(DateTimeOffset value) =>
            value.Offset == TimeSpan.Zero
                ? value
                : throw new InvalidOperationException("discord_clock_not_utc");
    }

    public sealed class RetryDiscordDeliveryUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly Func<DateTimeOffset> utcNow;

        public RetryDiscordDeliveryUseCase(
            IDiscordIntegrationStore store,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public DiscordDeliverySummary Execute(string deliveryId, string contentText)
        {
            EnqueueDiscordDeliveryUseCase.RequireContent(contentText);
            if (string.IsNullOrWhiteSpace(deliveryId))
                throw new DiscordDeliveryValidationException();
            return DiscordDeliverySummary.FromDelivery(store.ScheduleManualRetry(
                deliveryId.Trim(),
                contentText,
                EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow())));
        }
    }

    public sealed class CancelDiscordDeliveryUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly Func<DateTimeOffset> utcNow;

        public CancelDiscordDeliveryUseCase(
            IDiscordIntegrationStore store,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public bool Execute(string deliveryId)
        {
            if (string.IsNullOrWhiteSpace(deliveryId))
                throw new DiscordDeliveryValidationException();
            return store.CancelDelivery(
                deliveryId.Trim(),
                EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow()));
        }
    }
}
