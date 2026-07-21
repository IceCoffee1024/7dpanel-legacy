using System;
using Microsoft.Owin.Security;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class RejectingAuthenticationTicketFormat :
        ISecureDataFormat<AuthenticationTicket>
    {
        public static readonly RejectingAuthenticationTicketFormat Instance =
            new RejectingAuthenticationTicketFormat();

        private RejectingAuthenticationTicketFormat()
        {
        }

        public string Protect(AuthenticationTicket data) =>
            throw new InvalidOperationException(
                "Self-contained authentication tickets are disabled.");

        public AuthenticationTicket Unprotect(string protectedText) => null!;
    }
}
