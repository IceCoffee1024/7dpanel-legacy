using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class InvalidPlayerIdentityException : Exception
    {
    }

    public sealed class PlayerKickConfirmationRequiredException : Exception
    {
    }

    public sealed class InvalidPlayerKickReasonException : Exception
    {
    }

    public sealed class PlayerActionBusyException : Exception
    {
    }

    public sealed class PlayerNotOnlineException : Exception
    {
    }

    public sealed class PlayerIdentityChangedException : Exception
    {
    }

    public sealed class AuditUnavailableException : Exception
    {
        public AuditUnavailableException(Exception innerException)
            : base("The audit trail is unavailable.", innerException)
        {
        }
    }

    public sealed class AuditCompletionUnavailableException : Exception
    {
        public AuditCompletionUnavailableException()
        {
        }

        public AuditCompletionUnavailableException(Exception innerException)
            : base("The audit completion could not be persisted.", innerException)
        {
        }
    }

    public sealed class PlayerKickFailedException : Exception
    {
        public PlayerKickFailedException(Exception innerException)
            : base("The player kick failed.", innerException)
        {
        }
    }
}