using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class KickPlayerResult
    {
        public KickPlayerResult(
            string operationId,
            PlayerActionTarget target,
            DateTimeOffset requestedAtUtc,
            DateTimeOffset completedAtUtc)
        {
            OperationId = operationId;
            Status = "succeeded";
            Target = target;
            RequestedAtUtc = requestedAtUtc;
            CompletedAtUtc = completedAtUtc;
        }

        public string OperationId { get; }

        public string Status { get; }

        public PlayerActionTarget Target { get; }

        public DateTimeOffset RequestedAtUtc { get; }

        public DateTimeOffset CompletedAtUtc { get; }
    }
}