using System;
using System.Globalization;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class KickPlayerRequestBody
    {
        public KickPlayerPlatformIdentityBody? ExpectedPlatformIdentity { get; set; }

        public string? Reason { get; set; }

        public bool Confirmed { get; set; }
    }

    public sealed class KickPlayerPlatformIdentityBody
    {
        public string? CombinedId { get; set; }

        public string? Platform { get; set; }
    }

    public sealed class KickPlayerResponse
    {
        public KickPlayerResponse(KickPlayerResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            OperationId = result.OperationId;
            Status = result.Status;
            Target = new KickPlayerTargetResponse(result.Target);
            RequestedAtUtc = result.RequestedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            CompletedAtUtc = result.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        public string OperationId { get; }

        public string Status { get; }

        public KickPlayerTargetResponse Target { get; }

        public string RequestedAtUtc { get; }

        public string CompletedAtUtc { get; }
    }

    public sealed class KickPlayerTargetResponse
    {
        public KickPlayerTargetResponse(PlayerActionTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            EntityId = target.EntityId;
            Name = target.Name;
            PlatformIdentity = new KickPlayerPlatformIdentityResponse(
                target.PlatformIdentity.CombinedId,
                target.PlatformIdentity.Platform);
        }

        public int EntityId { get; }

        public string Name { get; }

        public KickPlayerPlatformIdentityResponse PlatformIdentity { get; }
    }

    public sealed class KickPlayerPlatformIdentityResponse
    {
        public KickPlayerPlatformIdentityResponse(string combinedId, string platform)
        {
            CombinedId = combinedId;
            Platform = platform;
        }

        public string CombinedId { get; }

        public string Platform { get; }
    }
}