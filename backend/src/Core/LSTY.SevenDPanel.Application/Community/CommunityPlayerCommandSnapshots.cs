using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Community
{
    public sealed class CommunityPlayerCommandSnapshot
    {
        public CommunityPlayerCommandSnapshot(
            string displayName,
            TeleportPlayerSnapshot player,
            TimeSpan onlineDuration)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A display name is required.", nameof(displayName));
            if (onlineDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(onlineDuration));
            DisplayName = displayName.Trim();
            Player = player ?? throw new ArgumentNullException(nameof(player));
            OnlineDuration = onlineDuration;
        }

        public string CrossplatformId => Player.CrossplatformId;
        public string DisplayName { get; }
        public TeleportPlayerSnapshot Player { get; }
        public TimeSpan OnlineDuration { get; }
    }

    public interface ICommunityPlayerCommandSnapshotProvider
    {
        CommunityPlayerCommandSnapshot? FindOnlineByCrossplatformId(string crossplatformId);
        CommunityPlayerCommandSnapshot? ResolveOnline(string selector);
        IReadOnlyList<CommunityPlayerCommandSnapshot> CaptureOnline();
    }
}
