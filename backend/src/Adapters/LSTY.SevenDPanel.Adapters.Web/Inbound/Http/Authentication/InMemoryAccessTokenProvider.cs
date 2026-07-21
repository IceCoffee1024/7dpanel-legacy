using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class InMemoryAccessTokenProvider : AuthenticationTokenProvider, IDisposable
    {
        private const int DefaultCapacity = 128;
        private readonly object sync = new object();
        private readonly int capacity;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> createToken;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Queue<string> issueOrder = new Queue<string>();
        private bool disposed;

        public InMemoryAccessTokenProvider()
            : this(DefaultCapacity, () => DateTimeOffset.UtcNow, CreateRandomToken)
        {
        }

        internal InMemoryAccessTokenProvider(
            int capacity,
            Func<DateTimeOffset> utcNow,
            Func<string> createToken)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.createToken = createToken ?? throw new ArgumentNullException(nameof(createToken));
        }

        internal int Count
        {
            get { lock (sync) return entries.Count; }
        }

        public override System.Threading.Tasks.Task CreateAsync(AuthenticationTokenCreateContext context)
        {
            context.SetToken(Issue(context.Ticket));
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public override System.Threading.Tasks.Task ReceiveAsync(AuthenticationTokenReceiveContext context)
        {
            if (TryReceive(context.Token, out var ticket)) context.SetTicket(ticket);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        internal string Issue(AuthenticationTicket ticket)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            if (!ticket.Properties.ExpiresUtc.HasValue)
                throw new InvalidOperationException("Access tokens require an expiration time.");

            lock (sync)
            {
                ThrowIfDisposed();
                RemoveExpired(utcNow());
                while (entries.Count >= capacity && issueOrder.Count > 0)
                {
                    entries.Remove(issueOrder.Dequeue());
                }

                string token;
                do { token = createToken(); }
                while (string.IsNullOrEmpty(token) || entries.ContainsKey(token));
                entries.Add(token, new Entry(ticket, ticket.Properties.ExpiresUtc.Value));
                issueOrder.Enqueue(token);
                return token;
            }
        }

        internal bool TryReceive(string? token, out AuthenticationTicket ticket)
        {
            lock (sync)
            {
                ticket = null!;
                if (disposed || string.IsNullOrEmpty(token)) return false;
                var now = utcNow();
                RemoveExpired(now);
                if (!entries.TryGetValue(token!, out var entry) || entry.ExpiresUtc <= now)
                    return false;
                ticket = entry.Ticket;
                return true;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                entries.Clear();
                issueOrder.Clear();
            }
        }

        private void RemoveExpired(DateTimeOffset now)
        {
            if (entries.Count == 0) return;
            var expired = new List<string>();
            foreach (var pair in entries)
            {
                if (pair.Value.ExpiresUtc <= now) expired.Add(pair.Key);
            }
            foreach (var token in expired) entries.Remove(token);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(InMemoryAccessTokenProvider));
        }

        private static string CreateRandomToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private readonly struct Entry
        {
            public Entry(AuthenticationTicket ticket, DateTimeOffset expiresUtc)
            {
                Ticket = ticket;
                ExpiresUtc = expiresUtc;
            }

            public AuthenticationTicket Ticket { get; }
            public DateTimeOffset ExpiresUtc { get; }
        }
    }
}
