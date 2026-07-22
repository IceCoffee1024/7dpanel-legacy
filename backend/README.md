# 7DPanel Backend

The private core backend for 7DPanel. It will run as a Mod DLL inside the 7DTD
Dedicated Server Mono process.

```text
src/      Mod DLL and embedded Web API implementation
tests/    Backend unit and integration tests
```

The directory contains an SDK-style `.NET Framework 4.8` solution. The main
project validates compile-time references against the pinned 7DTD game version
and prevents game-provided assemblies from being copied to build output. The
current validation slice implements `ModMain`, `ModHost`, the 7DTD lifecycle,
a consolidated bounded in-process server-event stream, Katana self-hosting,
`/health` plus `/api/v1/health`, unified API Problem Details, a
configuration-seeded SQLite `Owner`, SQLite-backed Basic authentication,
persistent opaque Bearer tokens, and the authenticated
`/api/v1/events/stream`. Authenticated `Owner` and `Admin` requests can submit
any non-empty command registered with 7DTD through a bounded capacity-32 FIFO;
each request keeps its own raw command and output while execution remains
serialized on the game thread. A separate Harmony patch observes the final
`SdtdConsole.executeCommand` call for built-in, third-party Mod, and other
standard console callers, then a capacity-256 worker persists best-effort raw
audits and visible gap records to SQLite without changing command behavior.
No structured command SSE is emitted. `GET /api/v1/players/online`
adds an Owner-only snapshot with entity id, name, opaque platform identities,
ping, level, health, and capture time; it excludes IP, location, ban state,
combat statistics, and offline history. User/role management,
additional player actions, audit-query APIs, and other product capabilities
are not implemented yet.

Bootstrap compiles against the game-provided `0_TFP_Harmony/0Harmony.dll` and
applies a scoped `Assembly.Location` compatibility patch before runtime
composition. After SQLite migration, it installs the separately owned command
observation patch; normal shutdown unpatches only that Harmony id after the
HTTP command and audit workers stop. This supports assemblies loaded from
memory without publishing a second Harmony copy inside the 7DPanel Mod.

Bootstrap is the only Microsoft.Extensions.DependencyInjection composition
root. It owns one validated root provider for the Mod lifetime; the OWIN
middleware owns one scope per request, and Web API resolves controllers from
that same scope. The production SSE writer is the first scoped runtime
service. The root provider is disposed only after the inner runtime and OWIN
host stop. The same root owns the SQLite connection factory; shutdown clears
its connection pools after OWIN stops.

Development publish, server-control, and health-check helpers are documented in
the [script guide](scripts/README.md). Machine-specific values belong in the
ignored `.env.local`; the tracked `.env.example` defines the available keys.

At runtime, `config.example.json` is the versioned template and `config.json` is
the server-owned configuration. The Mod creates a default `config.json` when it
is missing. DbUp creates or upgrades `<ModDirectory>/data/7dpanel.db` before
OWIN starts. The publish project never includes the server-owned file or the
`data/` directory.

Runtime defaults are defined by `PanelHostConfig.CreateDefault()`; an automated
test compares those values with `config.example.json` so the operator template
cannot silently drift from fallback behavior.

During the current framework-building phase, the `authentication` object is
enabled by default with the known credentials `admin` / `password`, a
30-minute access-token lifetime, and `allowInsecureHttp: true`. These values are
present in both the versioned template and a newly generated `config.json`, so
they are not secrets. On each start they seed the single SQLite user with
`Subject=owner`; Basic and password-grant verification then read that persistent
record. Changing either credential updates the same owner and revokes its prior
tokens. Other credentials and access tokens must not enter command history,
URLs, logs, frontend assets, or version control. Cookie, CSRF-token, and refresh
token authentication are not planned.

`POST /api/v1/auth/token` accepts only the OAuth password grant and issues a
short-lived opaque Bearer token whose secret is stored only as a SQLite hash.
Tokens survive a server restart until expiration or revocation.
`GET /api/v1/events/stream` requires either Basic or Bearer authentication,
rejects tokens in the query string, and emits `welcome`, `console-log`,
`game-ready`, and `server-stopping` events. The stream revalidates the current
user and Bearer token at most every 15 seconds and closes after invalidation.
Example local checks using environment-held test credentials are:

```powershell
$body = @{
  grant_type = 'password'
  username = $env:PANEL_USERNAME
  password = $env:PANEL_PASSWORD
}
$token = (Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:18080/api/v1/auth/token' -Body $body).access_token
$basic = [Convert]::ToBase64String(
  [Text.Encoding]::UTF8.GetBytes("$($env:PANEL_USERNAME):$($env:PANEL_PASSWORD)"))
@('no-buffer', 'url = "http://127.0.0.1:18080/api/v1/events/stream"', "header = `"Authorization: Basic $basic`"") |
  curl.exe --config -
@('no-buffer', 'url = "http://127.0.0.1:18080/api/v1/events/stream"', "header = `"Authorization: Bearer $token`"") |
  curl.exe --config -
```

The root `docs/` directory owns the product contract, system architecture, and
cross-system release gates. Authoritative build and test commands are kept in
the root `README.md` and `docs/test.md`.
