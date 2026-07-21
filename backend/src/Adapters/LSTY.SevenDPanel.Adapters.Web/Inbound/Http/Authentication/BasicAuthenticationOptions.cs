using Microsoft.Owin.Security;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class BasicAuthenticationOptions : AuthenticationOptions
    {
        public BasicAuthenticationOptions(
            string realm,
            bool allowInsecureHttp,
            PanelCredentialVerifier verifier)
            : base("Basic")
        {
            Realm = realm ?? throw new ArgumentNullException(nameof(realm));
            AllowInsecureHttp = allowInsecureHttp;
            Verifier = verifier ?? throw new System.ArgumentNullException(nameof(verifier));
            AuthenticationMode = AuthenticationMode.Active;
        }

        public string Realm { get; }
        public bool AllowInsecureHttp { get; }
        public PanelCredentialVerifier Verifier { get; }
    }
}
