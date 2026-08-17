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
configuration-seeded SQLite `Owner`, OAuth password grant, persistent opaque
Header Bearer Access Tokens and API Keys, and the authenticated
`/api/v1/events/stream`. Authenticated `Owner` and `Admin` requests can submit
any non-empty command registered with 7DTD through a bounded capacity-32 FIFO;
each request keeps its own raw command and output while execution remains
serialized on the game thread. A separate Harmony patch observes the final
`SdtdConsole.executeCommand` call for built-in, third-party Mod, and other
standard console callers, then a capacity-256 worker persists best-effort raw
audits and visible gap records to SQLite without changing command behavior.
No structured command SSE is emitted. `GET /api/v1/players/online` provides the
current typed player snapshot. The backend also implements the server-governance
vertical slices: Owner-only `serverconfig.xml` field management with redaction
and optimistic version checks; role-aware native ban/whitelist management;
Owner-only panel-user, 7DTD administrator, and command-permission management;
and read-only-for-Admin/Viewer mod discovery with Owner-only next-start state
changes. Panel roles and native `0..2000` permission levels remain separate.

The embedded host also exposes public runtime API documentation at `/swagger`
and `/swagger/v1/swagger.json`. NSwag reflects the Web API controllers at
runtime; a centralized Web Adapter document processor adds the OWIN-owned
password-grant token operation, and an operation processor describes the
single Bearer scheme, API Key operations, SSE response, and Problem Details errors.
The documentation endpoints intentionally have no access control and do not
invoke the console, player-query, player-action, or audit ports. The Web
Adapter owns `NSwag.AspNet.Owin`; no controller uses `NSwag.Annotations`.

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
The publish gate includes the NSwag, NJsonSchema, and Namotion runtime closure
while continuing to remove `Newtonsoft.Json.dll`; JSON serialization uses the
copy supplied by the game process.

At runtime, `config.example.json` is the versioned template and `config.json` is
the server-owned configuration. The Mod creates a default `config.json` when it
is missing. DbUp creates or upgrades `<ModDirectory>/data/7dpanel.db` before
OWIN starts. The publish project never includes the server-owned file or the
`data/` directory.

`serverConfigurationPath` resolves from the Mod configuration directory and
defaults to `../../serverconfig.xml`. Browser requests cannot supply a file
path. Mod discovery is limited to direct children of the surrounding `Mods`
directory; the current 7DPanel directory is protected from state changes.

Runtime defaults are defined by `PanelHostConfig.CreateDefault()`; an automated
test compares those values with `config.example.json` so the operator template
cannot silently drift from fallback behavior.

Steam OpenID server-side verification uses the optional `steamOpenIdProxy`
HTTP proxy from `config.json`; the generated default is
`http://127.0.0.1:10808`. Set it to `null` to connect directly. Invalid or
credential-bearing proxy URLs are disabled without replacing other settings.

During the current framework-building phase, the `authentication` object is
enabled by default with the known credentials `admin` / `password`, an
8-hour access-token lifetime, and `allowInsecureHttp: true`. These values are
present in both the versioned template and a newly generated `config.json`, so
they are not secrets. On each start they seed the single SQLite user with
`Subject=owner`; password-grant verification then reads that persistent record
with PBKDF2-HMAC-SHA256 at 1,000 iterations. Changing either credential updates
the same owner and revokes its prior Access Tokens. Other credentials and access tokens must not enter command history,
URLs, logs, frontend assets, or version control. Cookie, CSRF-token, and refresh
token authentication are not planned.

`POST /api/v1/auth/token` accepts only the OAuth password grant and issues an
opaque Access Token whose secret is stored only as a SQLite hash. The default
lifetime is 8 hours (`expires_in=28800`); Tokens survive a server restart until
expiration or revocation. `GET /api/v1/events/stream` accepts only
`Authorization: Bearer` Access Tokens or API Keys, rejects Basic, Cookie, and
query-string credentials, and emits `welcome`, `console-log`, `game-ready`, and
`server-stopping` events. The stream revalidates the current user, credential,
and allowed role at most every 15 seconds and closes after invalidation.

`GET /api/v1/api-keys` returns metadata for the authenticated subject's API
Keys. `POST /api/v1/api-keys` and `DELETE /api/v1/api-keys/{keyId}` require a
website Access Token; a Key cannot create or revoke Keys. A created Key is
returned once with `Cache-Control: no-store`; subsequent list results expose
only the safe id prefix, name, timestamps, and status. API Key secrets use a
distinct `7dp_k_` format and SQLite stores only their SHA-256 hash. API Key
authorization rebuilds the creator's current enabled state and role.
Example local checks using environment-held test credentials are:

```powershell
$body = @{
  grant_type = 'password'
  username = $env:PANEL_USERNAME
  password = $env:PANEL_PASSWORD
}
$token = (Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:18080/api/v1/auth/token' -Body $body).access_token
@('no-buffer', 'url = "http://127.0.0.1:18080/api/v1/events/stream"', "header = `"Authorization: Bearer $token`"") |
  curl.exe --config -
```

The root `docs/` directory owns the product contract, system architecture, and
cross-system release gates. Authoritative build and test commands are kept in
the root `README.md` and `docs/test.md`.

## Focused Backend Test Filters

The backend remains one test project. Every test class with a `[Fact]` or
`[Theory]` has exactly one class-level `Trait("Capability", "...")` owner and
at least one `Trait("Boundary", "...")`. The allowed Capability values are
`Platform`, `Operations`, `Players`, `Community`, `Economy`, `Automation`, and
`Administration`; the allowed Boundary values are `Domain`, `Application`,
`Persistence`, `Local`, `SevenDays`, `Web`, `Bootstrap`, and `CrossSystem`.

Use the taxonomy audit before relying on a focused filter:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Capability=Players&Boundary=Application"
dotnet test backend/tests/LSTY.SevenDPanel.Tests/LSTY.SevenDPanel.Tests.csproj --no-restore --filter "Capability=Operations"
```

The filter commands must discover and execute a non-zero set of tests; a zero
test result is not a passing focused gate. `docs/test.md` defines when to run a
Capability/Boundary slice, the aggregate suite, or a real game boundary.
