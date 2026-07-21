using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class AuthenticationAttemptLimiter
    {
        private readonly object sync = new object();
        private readonly int attemptsPerWindow;
        private readonly int maximumBuckets;
        private readonly TimeSpan window;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);

        public AuthenticationAttemptLimiter()
            : this(20, 1024, TimeSpan.FromMinutes(1), () => DateTimeOffset.UtcNow)
        {
        }

        internal AuthenticationAttemptLimiter(
            int attemptsPerWindow,
            int maximumBuckets,
            TimeSpan window,
            Func<DateTimeOffset> utcNow)
        {
            if (attemptsPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(attemptsPerWindow));
            if (maximumBuckets <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBuckets));
            if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
            this.attemptsPerWindow = attemptsPerWindow;
            this.maximumBuckets = maximumBuckets;
            this.window = window;
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public bool TryAcquire(string key, out TimeSpan retryAfter)
        {
            if (string.IsNullOrEmpty(key)) key = "<unknown>";
            lock (sync)
            {
                var now = utcNow();
                RemoveExpired(now);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    if (buckets.Count >= maximumBuckets)
                    {
                        retryAfter = window;
                        return false;
                    }

                    buckets.Add(key, new Bucket(now, 1));
                    retryAfter = TimeSpan.Zero;
                    return true;
                }

                if (bucket.Count >= attemptsPerWindow)
                {
                    retryAfter = window - (now - bucket.StartedAt);
                    if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
                    return false;
                }

                buckets[key] = new Bucket(bucket.StartedAt, bucket.Count + 1);
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }

        private void RemoveExpired(DateTimeOffset now)
        {
            if (buckets.Count == 0) return;
            var expired = new List<string>();
            foreach (var pair in buckets)
            {
                if (now - pair.Value.StartedAt >= window) expired.Add(pair.Key);
            }
            foreach (var key in expired) buckets.Remove(key);
        }

        private readonly struct Bucket
        {
            public Bucket(DateTimeOffset startedAt, int count)
            {
                StartedAt = startedAt;
                Count = count;
            }

            public DateTimeOffset StartedAt { get; }
            public int Count { get; }
        }
    }
}
