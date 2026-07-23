using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using LSTY.SevenDPanel.Application.ConsoleCommands;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands
{
    internal sealed class ConsoleCommandPatchState
    {
        private int completed;

        public ConsoleCommandPatchState(
            string auditId,
            string rawCommand,
            IReadOnlyList<string> tokens,
            string source,
            string? actorSubject,
            DateTimeOffset startedAtUtc)
        {
            AuditId = auditId;
            RawCommand = rawCommand;
            Tokens = tokens;
            Source = source;
            ActorSubject = actorSubject;
            StartedAtUtc = startedAtUtc;
        }

        public string AuditId { get; }
        public string RawCommand { get; }
        public IReadOnlyList<string> Tokens { get; }
        public string Source { get; }
        public string? ActorSubject { get; }
        public DateTimeOffset StartedAtUtc { get; }

        public bool TryComplete() => Interlocked.Exchange(ref completed, 1) == 0;
    }

    [HarmonyPatch(typeof(SdtdConsole), "executeCommand")]
    public static class ConsoleCommandExecutionPatch
    {
        private static readonly object observerSync = new object();
        private static Action<ConsoleCommandExecutionObservation>? observers;

        internal static IDisposable Subscribe(Action<ConsoleCommandExecutionObservation> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            lock (observerSync) observers += observer;
            return new Subscription(observer);
        }

        [HarmonyPrefix]
        internal static void Prefix(
            SdtdConsole __instance,
            string _command,
            CommandSenderInfo _senderInfo,
            out ConsoleCommandPatchState __state)
        {
            if (__instance == null) throw new ArgumentNullException(nameof(__instance));
            CapturePrefix(
                _command,
                ClassifySource(
                    _senderInfo.IsLocalGame,
                    _senderInfo.RemoteClientInfo != null,
                    _senderInfo.NetworkConnection != null),
                __instance.tokenizeCommand,
                out __state);
        }

        internal static void CapturePrefix(
            string rawCommand,
            string nativeSource,
            Func<string, List<string>?> tokenize,
            out ConsoleCommandPatchState state)
        {
            if (tokenize == null) throw new ArgumentNullException(nameof(tokenize));
            var command = rawCommand ?? string.Empty;
            IReadOnlyList<string> tokens = Array.Empty<string>();
            if (command.Length != 0)
            {
                try
                {
                    var parsed = tokenize(command);
                    if (parsed != null) tokens = parsed.ToArray();
                }
                catch
                {
                }
            }

            var scopedSource = ConsoleCommandSourceContext.Source;
            state = new ConsoleCommandPatchState(
                Guid.NewGuid().ToString("N"),
                command,
                tokens,
                scopedSource ?? nativeSource,
                scopedSource == null ? null : ConsoleCommandSourceContext.ActorSubject,
                DateTimeOffset.UtcNow);
        }

        internal static string ClassifySource(
            bool isLocalGame,
            bool hasRemoteClient,
            bool hasNetworkConnection)
        {
            if (isLocalGame) return "local-game";
            if (hasRemoteClient) return "remote-client";
            if (hasNetworkConnection) return "network";
            return "network";
        }

        [HarmonyPostfix]
        internal static void Postfix(
            List<string>? __result,
            ConsoleCommandPatchState __state)
        {
            if (__state == null || !__state.TryComplete()) return;
            Publish(
                __state,
                __result == null ? Array.Empty<string>() : __result.ToArray(),
                ConsoleCommandCompletionKind.Completed,
                null);
        }

        [HarmonyFinalizer]
        internal static Exception? Finalizer(
            Exception? __exception,
            ConsoleCommandPatchState? __state)
        {
            if (__exception == null || __state == null || !__state.TryComplete())
                return __exception;
            Publish(
                __state,
                Array.Empty<string>(),
                ConsoleCommandCompletionKind.Threw,
                __exception.GetType().FullName ?? __exception.GetType().Name);
            return __exception;
        }

        private static void Publish(
            ConsoleCommandPatchState state,
            IReadOnlyList<string> output,
            ConsoleCommandCompletionKind completionKind,
            string? exceptionType)
        {
            var observation = new ConsoleCommandExecutionObservation(
                state.AuditId,
                state.RawCommand,
                state.Tokens,
                output,
                state.Source,
                state.ActorSubject,
                state.StartedAtUtc,
                DateTimeOffset.UtcNow,
                completionKind,
                exceptionType);
            Action<ConsoleCommandExecutionObservation>? snapshot;
            lock (observerSync) snapshot = observers;
            if (snapshot == null) return;
            foreach (Action<ConsoleCommandExecutionObservation> observer in snapshot.GetInvocationList())
            {
                try
                {
                    observer(observation);
                }
                catch
                {
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action<ConsoleCommandExecutionObservation>? observer;

            public Subscription(Action<ConsoleCommandExecutionObservation> observer)
            {
                this.observer = observer;
            }

            public void Dispose()
            {
                var value = Interlocked.Exchange(ref observer, null);
                if (value == null) return;
                lock (observerSync) observers -= value;
            }
        }
    }
}