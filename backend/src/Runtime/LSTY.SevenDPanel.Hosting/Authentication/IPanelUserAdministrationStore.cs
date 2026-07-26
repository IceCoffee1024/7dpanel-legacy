using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public enum PanelUserMutationStatus
    {
        Created,
        Updated,
        Deleted,
        NotFound,
        Invalid,
        Conflict,
        LastOwner
    }

    public sealed class PanelUserRecord
    {
        public PanelUserRecord(
            string subject,
            string username,
            string role,
            bool enabled,
            DateTimeOffset updatedAtUtc)
        {
            Subject = subject;
            Username = username;
            Role = role;
            Enabled = enabled;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string Subject { get; }
        public string Username { get; }
        public string Role { get; }
        public bool Enabled { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
    }

    public sealed class PanelUserMutationResult
    {
        private PanelUserMutationResult(
            PanelUserMutationStatus status,
            PanelUserRecord? user = null)
        {
            Status = status;
            User = user;
        }

        public PanelUserMutationStatus Status { get; }
        public PanelUserRecord? User { get; }

        public static PanelUserMutationResult With(
            PanelUserMutationStatus status,
            PanelUserRecord? user = null) => new PanelUserMutationResult(status, user);
    }

    public interface IPanelUserAdministrationStore
    {
        IReadOnlyList<PanelUserRecord> ListUsers();
        PanelUserMutationResult CreateUser(string username, string password, string role, bool enabled);
        PanelUserMutationResult UpdateUser(string subject, string username, string role, bool enabled);
        PanelUserMutationResult ResetPassword(string subject, string password);
        PanelUserMutationResult DeleteUser(string subject);
    }
}
