using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class ChatMuteUseCases
    {
        private readonly IChatMuteStore store;
        private readonly IChatMuteRuntimeConfiguration runtime;
        private readonly Func<DateTimeOffset> utcNow;

        public ChatMuteUseCases(
            IChatMuteStore store,
            IChatMuteRuntimeConfiguration runtime,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public ChatMuteRecord Create(
            string actorSubject,
            string crossplatformId,
            string? displayName,
            string reason,
            DateTimeOffset? mutedUntilUtc,
            string? correlationId)
        {
            var now = GetUtcNow();
            var record = new ChatMuteRecord(
                crossplatformId,
                displayName,
                reason,
                mutedUntilUtc,
                actorSubject,
                now,
                actorSubject,
                now);
            var operation = Operation(
                ChatMuteOperationKind.Create,
                record,
                actorSubject,
                now,
                correlationId);
            return ExecuteSerialized(() =>
            {
                ReplaceAfterCommit(store.Create(record, operation));
                return record;
            });
        }

        public ChatMuteRecord Update(
            string actorSubject,
            string crossplatformId,
            string? displayName,
            string reason,
            DateTimeOffset? mutedUntilUtc,
            string? correlationId)
        {
            return ExecuteSerialized(() =>
            {
                var current = store.Find(crossplatformId) ?? throw new ChatMuteNotFoundException();
                var now = GetUtcNow();
                var record = new ChatMuteRecord(
                    current.CrossplatformId,
                    displayName,
                    reason,
                    mutedUntilUtc,
                    current.CreatedBy,
                    current.CreatedAtUtc,
                    actorSubject,
                    now);
                var operation = Operation(
                    ChatMuteOperationKind.Update,
                    record,
                    actorSubject,
                    now,
                    correlationId);
                ReplaceAfterCommit(store.Update(record, operation));
                return record;
            });
        }

        public void Release(string actorSubject, string crossplatformId, string? correlationId)
        {
            ExecuteSerialized(() =>
            {
                var current = store.Find(crossplatformId) ?? throw new ChatMuteNotFoundException();
                var now = GetUtcNow();
                var operation = Operation(
                    ChatMuteOperationKind.Release,
                    current,
                    actorSubject,
                    now,
                    correlationId);
                ReplaceAfterCommit(store.Release(current.CrossplatformId, operation));
                return 0;
            });
        }

        public ChatMutePage GetPage(int pageSize, ChatMuteCursor? cursor) => store.GetPage(pageSize, cursor);

        public static T ExecuteSerialized<T>(Func<T> mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            lock (MuteMutationGate) return mutation();
        }

        private static readonly object MuteMutationGate = new object();

        private DateTimeOffset GetUtcNow()
        {
            var now = utcNow();
            ChatMuteRecord.RequireUtc(now, nameof(utcNow));
            return now;
        }

        private static ChatMuteOperation Operation(
            ChatMuteOperationKind kind,
            ChatMuteRecord record,
            string actorSubject,
            DateTimeOffset occurredAtUtc,
            string? correlationId) =>
            new ChatMuteOperation(
                Guid.NewGuid().ToString("D"),
                kind,
                record.CrossplatformId,
                actorSubject,
                occurredAtUtc,
                "Succeeded",
                correlationId,
                record.MutedUntilUtc,
                record.Reason);

        private void ReplaceAfterCommit(IEnumerable<ChatMuteRecord> records)
        {
            var snapshot = new ReadOnlyDictionary<string, ChatMuteRecord>(
                records.ToDictionary(record => record.CrossplatformId, StringComparer.Ordinal));
            runtime.ReplaceMuteSnapshot(snapshot);
        }
    }

    public sealed class ChatMuteNotFoundException : Exception
    {
        public ChatMuteNotFoundException() : base("The chat mute does not exist.") { }
    }
}
