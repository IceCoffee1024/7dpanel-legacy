using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using Microsoft.Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [RoutePrefix("api/oauth/steam")]
    public sealed class PlayerAuthenticationController : ApiController
    {
        private const string ChallengeCookie = "7dpanel.player.openid";
        private const string SessionCookie = "7dpanel.player.session";
        private readonly PlayerAuthenticationService authentication;

        public PlayerAuthenticationController(PlayerAuthenticationService authentication)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        }

        [HttpGet]
        [Route("login")]
        public HttpResponseMessage Login(string? redirect = null)
        {
            var requestUri = Request.RequestUri ?? throw new InvalidOperationException("Request URI is unavailable.");
            var origin = new Uri(requestUri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
            PlayerLoginStart start;
            try
            {
                start = authentication.Start(origin, redirect ?? "/player/store");
            }
            catch (ArgumentException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "invalid_player_redirect",
                    "The requested player redirect is not allowed.");
            }

            AppendCookie(
                ChallengeCookie,
                start.State,
                start.ExpiresAtUtc,
                "/api/oauth/steam");
            return RedirectAbsolute(start.LoginUri);
        }

        [HttpGet]
        [Route("return")]
        public async Task<HttpResponseMessage> Return(CancellationToken cancellationToken)
        {
            var completion = await authentication.CompleteAsync(
                    ReadCookie(ChallengeCookie),
                    Request.GetQueryNameValuePairs(),
                    cancellationToken)
                .ConfigureAwait(false);
            DeleteCookie(ChallengeCookie, "/api/oauth/steam");
            if (!completion.IsSuccessful)
            {
                return RedirectRelative(
                    "/player/login?error=" + Uri.EscapeDataString(completion.ErrorCode!));
            }

            AppendCookie(
                SessionCookie,
                completion.SessionId!,
                completion.ExpiresAtUtc!.Value,
                "/");
            return RedirectRelative(completion.Redirect!);
        }

        private string? ReadCookie(string name) =>
            Request.GetOwinContext().Request.Cookies[name];

        private void AppendCookie(
            string name,
            string value,
            DateTimeOffset expiresAtUtc,
            string path)
        {
            Request.GetOwinContext().Response.Cookies.Append(name, value, new CookieOptions
            {
                HttpOnly = true,
                Secure = string.Equals(Request.RequestUri?.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
                SameSite = SameSiteMode.Lax,
                Path = path,
                Expires = expiresAtUtc.UtcDateTime
            });
        }

        private void DeleteCookie(string name, string path)
        {
            Request.GetOwinContext().Response.Cookies.Delete(name, new CookieOptions
            {
                Path = path
            });
        }

        private HttpResponseMessage RedirectRelative(string path)
        {
            var response = Request.CreateResponse(HttpStatusCode.Found);
            response.Headers.Location = new Uri(path, UriKind.Relative);
            return response;
        }

        private HttpResponseMessage RedirectAbsolute(Uri location)
        {
            var response = Request.CreateResponse(HttpStatusCode.Found);
            response.Headers.Location = location;
            return response;
        }
    }

    [ApiExplorerSettings(IgnoreApi = true)]
    [RoutePrefix("api/v1/player")]
    public sealed class PlayerSessionController : ApiController
    {
        private const string SessionCookie = "7dpanel.player.session";
        private readonly PlayerAuthenticationService authentication;

        public PlayerSessionController(PlayerAuthenticationService authentication)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        }

        [HttpGet]
        [Route("me")]
        public HttpResponseMessage GetCurrent()
        {
            var sessionId = Request.GetOwinContext().Request.Cookies[SessionCookie];
            if (!authentication.TryGetSession(sessionId, out var session))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "player_authentication_required",
                    "A valid player session is required.");
            }

            return Request.CreateResponse(
                HttpStatusCode.OK,
                new PlayerSessionResponse(
                    session.Player.SteamId,
                    session.Player.PrimaryId,
                    session.Player.DisplayName));
        }

        [HttpPost]
        [Route("logout")]
        public HttpResponseMessage Logout()
        {
            var sessionId = Request.GetOwinContext().Request.Cookies[SessionCookie];
            authentication.Logout(sessionId);
            Request.GetOwinContext().Response.Cookies.Delete(SessionCookie, new CookieOptions
            {
                Path = "/"
            });
            return Request.CreateResponse(HttpStatusCode.NoContent);
        }
    }

    public sealed class PlayerSessionResponse
    {
        public PlayerSessionResponse(string steamId, string primaryId, string displayName)
        {
            SteamId = steamId;
            PrimaryId = primaryId;
            DisplayName = displayName;
        }

        public string SteamId { get; }
        public string PrimaryId { get; }
        public string DisplayName { get; }
    }
}
