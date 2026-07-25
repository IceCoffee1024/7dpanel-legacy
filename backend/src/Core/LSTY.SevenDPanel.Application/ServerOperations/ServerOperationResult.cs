using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application
{
    public sealed class ServerOperationResult
    {
        public ServerOperationResult(
            string operationId,
            string operationCode,
            string status,
            DateTimeOffset requestedAtUtc,
            DateTimeOffset acceptedAtUtc,
            string auditStatus)
        {
            OperationId = operationId;
            OperationCode = operationCode;
            Status = status;
            RequestedAtUtc = requestedAtUtc;
            AcceptedAtUtc = acceptedAtUtc;
            AuditStatus = auditStatus;
        }

        public string OperationId { get; }

        public string OperationCode { get; }

        public string Status { get; }

        public DateTimeOffset RequestedAtUtc { get; }

        public DateTimeOffset AcceptedAtUtc { get; }

        public string AuditStatus { get; }
    }

    public sealed class ServerOperationAuditIntent
    {
        public ServerOperationAuditIntent(
            string operationId,
            string operationCode,
            string actorSubject,
            DateTimeOffset requestedAtUtc)
        {
            if (!ServerOperationCodeContract.IsOperationCode(operationCode))
                throw new ArgumentException("The server operation code is not approved.", nameof(operationCode));

            OperationId = operationId;
            OperationCode = operationCode;
            ActorSubject = actorSubject;
            RequestedAtUtc = requestedAtUtc;
        }

        public string OperationId { get; }

        public string OperationCode { get; }

        public string ActorSubject { get; }

        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class ServerOperationAuditFailure
    {
        public ServerOperationAuditFailure(
            string operationId,
            DateTimeOffset updatedAtUtc,
            string failureCode)
        {
            if (!ServerOperationCodeContract.IsFailureCode(failureCode))
                throw new ArgumentException("The server operation failure code is not approved.", nameof(failureCode));

            OperationId = operationId;
            UpdatedAtUtc = updatedAtUtc;
            FailureCode = failureCode;
        }

        public string OperationId { get; }

        public DateTimeOffset UpdatedAtUtc { get; }

        public string FailureCode { get; }
    }

    public static class ServerOperationCodeContract
    {
        public const string RestartScript = "restart_script";
        public const string Shutdown = "shutdown";
        public const string RestartScriptNotConfigured = "restart_script_not_configured";
        public const string RestartScriptMissing = "restart_script_missing";
        public const string RestartScriptPlatformUnsupported = "restart_script_platform_unsupported";
        public const string RestartScriptStartFailed = "restart_script_start_failed";
        public const string ShutdownUnavailable = "shutdown_unavailable";
        public const string ShutdownTimeout = "shutdown_timeout";
        public const string ShutdownCancelled = "shutdown_cancelled";
        public const string ShutdownFailed = "shutdown_failed";

        private static readonly IReadOnlyList<string> Operations = Array.AsReadOnly(new[]
        {
            RestartScript,
            Shutdown
        });

        private static readonly IReadOnlyList<string> RestartFailures = Array.AsReadOnly(new[]
        {
            RestartScriptNotConfigured,
            RestartScriptMissing,
            RestartScriptPlatformUnsupported,
            RestartScriptStartFailed
        });

        private static readonly IReadOnlyList<string> ShutdownFailures = Array.AsReadOnly(new[]
        {
            ShutdownUnavailable,
            ShutdownTimeout,
            ShutdownCancelled,
            ShutdownFailed
        });

        public static IReadOnlyList<string> OperationCodes => Operations;

        public static IReadOnlyList<string> RestartFailureCodes => RestartFailures;

        public static IReadOnlyList<string> ShutdownFailureCodes => ShutdownFailures;

        public static bool IsOperationCode(string? value)
        {
            return value == RestartScript || value == Shutdown;
        }

        public static bool IsFailureCode(string? value)
        {
            return Contains(RestartFailures, value) || Contains(ShutdownFailures, value);
        }

        public static bool IsFailure(string? operationCode, string? failureCode)
        {
            if (operationCode == RestartScript)
                return Contains(RestartFailures, failureCode);
            if (operationCode == Shutdown)
                return Contains(ShutdownFailures, failureCode);
            return false;
        }

        public static IReadOnlyList<string> GetFailureCodes(string operationCode)
        {
            switch (operationCode)
            {
                case RestartScript:
                    return RestartFailures;
                case Shutdown:
                    return ShutdownFailures;
                default:
                    return Array.Empty<string>();
            }
        }

        private static bool Contains(IReadOnlyList<string> values, string? value)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
