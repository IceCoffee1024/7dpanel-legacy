using System;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime
{
    public enum MainThreadRequestOutcome
    {
        Succeeded,
        Failed,
        Unavailable,
        Canceled,
        TimedOut,
        Unknown
    }

    public enum MainThreadUnavailableReason
    {
        None,
        NotReady,
        CapacityExceeded,
        Stopping
    }

    public sealed class MainThreadReply<T>
    {
        private MainThreadReply(
            MainThreadRequestOutcome outcome,
            T? value,
            Exception? exception,
            MainThreadUnavailableReason unavailableReason)
        {
            Outcome = outcome;
            Value = value;
            Exception = exception;
            UnavailableReason = unavailableReason;
        }

        public MainThreadRequestOutcome Outcome { get; }
        public T? Value { get; }
        public Exception? Exception { get; }
        public MainThreadUnavailableReason UnavailableReason { get; }

        internal static MainThreadReply<T> Succeeded(T value) =>
            new MainThreadReply<T>(MainThreadRequestOutcome.Succeeded, value, null, MainThreadUnavailableReason.None);

        internal static MainThreadReply<T> Failed(Exception exception) =>
            new MainThreadReply<T>(MainThreadRequestOutcome.Failed, default, exception, MainThreadUnavailableReason.None);

        internal static MainThreadReply<T> Unavailable(MainThreadUnavailableReason reason) =>
            new MainThreadReply<T>(MainThreadRequestOutcome.Unavailable, default, null, reason);

        internal static MainThreadReply<T> Canceled() =>
            new MainThreadReply<T>(MainThreadRequestOutcome.Canceled, default, null, MainThreadUnavailableReason.None);

        internal static MainThreadReply<T> TimedOut() =>
            new MainThreadReply<T>(MainThreadRequestOutcome.TimedOut, default, null, MainThreadUnavailableReason.None);

        internal static MainThreadReply<T> Unknown() =>
            new MainThreadReply<T>(MainThreadRequestOutcome.Unknown, default, null, MainThreadUnavailableReason.None);
    }
}
