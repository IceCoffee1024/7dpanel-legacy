using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Community
{
    public interface ICommunityGameGateway
    {
        Task<TeleportActionResult> TeleportAsync(
            TeleportActionCommand command,
            CancellationToken cancellationToken);
    }

    public sealed class TeleportActionCommand
    {
        public TeleportActionCommand(
            string operationId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            WorldPosition destination,
            bool denyDuringBloodMoon)
        {
            OperationId = CommunityModelValidation.RequireText(operationId, nameof(operationId));
            CrossplatformId = CommunityModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = CommunityModelValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            DenyDuringBloodMoon = denyDuringBloodMoon;
        }

        public string OperationId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public WorldPosition Destination { get; }
        public bool DenyDuringBloodMoon { get; }
    }

    public sealed class TeleportActionResult
    {
        private TeleportActionResult(
            TeleportActionStatus status,
            string? failureCode,
            WorldPosition? origin)
        {
            CommunityModelValidation.RequireDefined(status, nameof(status));
            if (status == TeleportActionStatus.Succeeded && origin == null)
                throw new ArgumentNullException(nameof(origin));
            if (status != TeleportActionStatus.Succeeded && origin != null)
                throw new ArgumentException("Only a confirmed teleport can include an origin.", nameof(origin));
            Status = status;
            FailureCode = CommunityModelValidation.OptionalText(failureCode);
            Origin = origin;
        }

        public TeleportActionStatus Status { get; }
        public string? FailureCode { get; }
        public WorldPosition? Origin { get; }

        public static TeleportActionResult Succeeded(WorldPosition origin) =>
            new TeleportActionResult(TeleportActionStatus.Succeeded, null, origin);

        public static TeleportActionResult Rejected(string failureCode) =>
            new TeleportActionResult(TeleportActionStatus.Rejected, failureCode, null);

        public static TeleportActionResult Failed(string failureCode) =>
            new TeleportActionResult(TeleportActionStatus.Failed, failureCode, null);

        public static TeleportActionResult Cancelled() =>
            new TeleportActionResult(
                TeleportActionStatus.Cancelled,
                TeleportFailureCodes.Cancelled,
                null);

        public static TeleportActionResult ResultUnknown() =>
            new TeleportActionResult(
                TeleportActionStatus.ResultUnknown,
                TeleportFailureCodes.ResultUnknown,
                null);
    }
}
