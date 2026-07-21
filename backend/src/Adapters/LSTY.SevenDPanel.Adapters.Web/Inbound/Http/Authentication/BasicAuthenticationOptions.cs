using System;
using Microsoft.Owin.Security;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class BasicAuthenticationOptions : AuthenticationOptions
    {
        public BasicAuthenticationOptions(
            string realm,
            bool allowInsecureHttp,
            Func<string, string, bool> verifier)
            : base("Basic")
        {
            Realm = realm ?? throw new ArgumentNullException(nameof(realm));
            AllowInsecureHttp = allowInsecureHttp;
            Verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            AuthenticationMode = AuthenticationMode.Active;
        }

        public string Realm { get; }
        public bool AllowInsecureHttp { get; }
        public Func<string, string, bool> Verifier { get; }
    }
}
