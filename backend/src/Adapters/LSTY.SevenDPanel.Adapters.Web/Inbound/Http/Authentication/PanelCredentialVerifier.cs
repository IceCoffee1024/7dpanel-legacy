using System;
using LSTY.SevenDPanel.Hosting.Authentication;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class PanelCredentialVerifier
    {
        private readonly IPanelCredentialStore credentialStore;

        public PanelCredentialVerifier(IPanelCredentialStore credentialStore)
        {
            this.credentialStore = credentialStore ??
                throw new ArgumentNullException(nameof(credentialStore));
        }

        public bool TryVerify(
            string? username,
            string? password,
            out PanelUserIdentity identity)
        {
            identity = null!;
            return !string.IsNullOrEmpty(username) &&
                password != null &&
                credentialStore.TryVerify(username!, password, out identity);
        }
    }
}
