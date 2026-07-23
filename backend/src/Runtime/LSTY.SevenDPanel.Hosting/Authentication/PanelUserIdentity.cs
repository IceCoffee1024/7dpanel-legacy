using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public sealed class PanelUserIdentity
    {
        public const string OwnerRole = "Owner";
        public const string AdminRole = "Admin";
        public const string ViewerRole = "Viewer";

        public PanelUserIdentity(string subject, string username, string role)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("A user subject is required.", nameof(subject));
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A username is required.", nameof(username));
            if (!IsSupportedRole(role))
                throw new ArgumentException("A supported panel user role is required.", nameof(role));

            Subject = subject;
            Username = username;
            Role = role;
        }

        public string Subject { get; }
        public string Username { get; }
        public string Role { get; }

        private static bool IsSupportedRole(string role) =>
            string.Equals(role, OwnerRole, StringComparison.Ordinal) ||
            string.Equals(role, AdminRole, StringComparison.Ordinal) ||
            string.Equals(role, ViewerRole, StringComparison.Ordinal);
    }
}
