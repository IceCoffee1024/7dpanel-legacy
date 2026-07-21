using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public sealed class PanelUserIdentity
    {
        public PanelUserIdentity(string subject, string username)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("A user subject is required.", nameof(subject));
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A username is required.", nameof(username));

            Subject = subject;
            Username = username;
        }

        public string Subject { get; }
        public string Username { get; }
    }
}
