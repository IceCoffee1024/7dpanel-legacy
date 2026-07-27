using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Community
{
    public sealed class CommunityCommandEnvelope
    {
        public CommunityCommandEnvelope(
            string crossplatformId,
            string displayName,
            string commandName,
            IReadOnlyList<string> arguments)
        {
            CrossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            DisplayName = RequireText(displayName, nameof(displayName));
            CommandName = RequireText(commandName, nameof(commandName));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        }

        public string CrossplatformId { get; }
        public string DisplayName { get; }
        public string CommandName { get; }
        public IReadOnlyList<string> Arguments { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }
    }

    public interface ICommunityCommandSource
    {
        IDisposable Subscribe(Action<CommunityCommandEnvelope> callback);
    }

    public interface ICommunityPrivateReplyPort
    {
        void Send(
            string crossplatformId,
            string code,
            IReadOnlyList<string> messages);
    }

    public sealed class CommunityCommandRuntime : IModRuntime, IDisposable
    {
        private readonly object sync = new object();
        private readonly ICommunityCommandSource source;
        private readonly CommunityGameCommandRouter router;
        private readonly ICommunityPrivateReplyPort replies;
        private IDisposable? subscription;
        private bool started;
        private bool disposed;

        public CommunityCommandRuntime(
            ICommunityCommandSource source,
            CommunityGameCommandRouter router,
            ICommunityPrivateReplyPort replies)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            this.replies = replies ?? throw new ArgumentNullException(nameof(replies));
        }

        public void Start()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                started = true;
            }
        }

        public void MarkGameReady()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (!started || subscription != null) return;
                subscription = source.Subscribe(Handle);
            }
        }

        public void Stop()
        {
            IDisposable? current;
            lock (sync)
            {
                current = subscription;
                subscription = null;
                started = false;
            }

            current?.Dispose();
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
            }

            Stop();
        }

        private void Handle(CommunityCommandEnvelope command)
        {
            if (command == null) return;
            CommunityGameCommandResult result;
            try
            {
                result = router.Route(
                    command.CommandName,
                    new CommunityGameCommandContext(
                        command.CrossplatformId,
                        command.DisplayName,
                        command.Arguments));
            }
            catch
            {
                result = new CommunityGameCommandResult(
                    true,
                    "community.command.failed",
                    Array.Empty<string>());
            }

            replies.Send(
                command.CrossplatformId,
                result.Code,
                result.Messages);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CommunityCommandRuntime));
        }
    }
}
