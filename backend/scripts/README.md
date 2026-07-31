# Backend Helper Scripts

These scripts publish the Mod, start or stop a local or remote 7DTD Dedicated
Server, and probe the 7DPanel health endpoint. They are development and smoke
test helpers, not runtime dependencies of the Mod.

## Entry Points

Use the `.cmd` wrappers for the standard Windows workflow. They invoke the
in-box Windows PowerShell 5.1, so PowerShell 7 is not required:

```bat
backend\scripts\Publish-Mod.cmd
backend\scripts\Start-Server.cmd
backend\scripts\Stop-Server.cmd
backend\scripts\Test-HealthEndpoint.cmd
```

The matching `.ps1` files expose PowerShell parameters for automation. Explicit
parameters take precedence over values loaded from the environment file.

`Test-ReleaseArtifact.ps1` is the publish-independent release layout gate. It
validates an existing artifact directory against `release-manifest.json` and
does not build, publish, start, or modify a 7DTD server.

## Local Configuration

Copy `backend/.env.example` to the ignored `backend/.env.local` file and set
only the values needed by the selected environment:

| Variable | Purpose |
|---|---|
| `SEVENDPANEL_PUBLISH_DIR` | Optional publish target, including a local or mounted remote `Mods/7DPanel` directory. |
| `SEVENDPANEL_HEALTH_URL` | Health endpoint probed by `Test-HealthEndpoint`. |
| `SEVENDPANEL_LOCAL_SERVER_ROOT` | Local 7DTD Dedicated Server directory. |
| `SEVENDPANEL_REMOTE_COMPUTER` | WinRM computer name or address for a remote server. |
| `SEVENDPANEL_REMOTE_SERVER_ROOT` | 7DTD Dedicated Server directory on the remote machine. |
| `SEVENDPANEL_TELNET_PORT` | Local Telnet port used for graceful shutdown; defaults to `8081`. |

Runtime listener settings belong to the deployed Mod's `config.json`, not to
`.env.local`. Use `-EnvironmentFile` to select another local environment file.
Authentication credentials belong only in the server-owned `config.json`; they
are never stored in `.env.local` or an alternate environment file.

## Publish

The current publish helper assembles the Windows and Linux x64 SQLite runtime
layout. Local output validation covers both RID directories, but the standard
Batteries combination is verified in a Windows `v3.0.1-b4` process; Linux
still requires a real-process smoke. The target server must retain the game-provided
`Mods/0_TFP_Harmony/0Harmony.dll`; 7DPanel compiles against that assembly and
does not publish its own copy.

Build the Admin application before publishing the Mod:

```powershell
Set-Location frontend/apps/admin
pnpm build
Set-Location ../../..
```

`Publish-Mod` fails when `dist/index.html` or its generated assets are missing.
It runs the standard Release folder profile. With no configured target, output
is written to:

```text
backend/src/Bootstrap/LSTY.SevenDPanel/bin/Release/net48/publish/
```

When `SEVENDPANEL_PUBLISH_DIR` is set, `dotnet publish` writes directly to that
directory. Publishing is incremental: it does not clear the target and does not
overwrite the server-owned `config.json` or `data/`. It replaces only the
published `wwwroot/` directory with `frontend/apps/admin/dist/`, producing this
runtime layout:

```text
<ModDirectory>/
  LSTY.SevenDPanel.dll
  Dapper.dll
  dbup-core.dll
  dbup-sqlite.dll
  LSTY.SevenDPanel.Application.dll
  LSTY.SevenDPanel.Domain.dll
  LSTY.SevenDPanel.Hosting.dll
  LSTY.SevenDPanel.Adapters.Local.dll
  LSTY.SevenDPanel.Adapters.Web.dll
  LSTY.SevenDPanel.Adapters.SevenDays.dll
  LSTY.SevenDPanel.Adapters.Persistence.Sqlite.dll
  Microsoft.Bcl.AsyncInterfaces.dll
  Microsoft.CSharp.dll
  Microsoft.Data.Sqlite.dll
  SQLitePCLRaw.batteries_v2.dll
  SQLitePCLRaw.batteries_v2.dll.config
  SQLitePCLRaw.core.dll
  SQLitePCLRaw.provider.dynamic_cdecl.dll
  System.ComponentModel.DataAnnotations.dll
  System.Dynamic.dll
  System.Reflection.Emit.dll
  System.Runtime.CompilerServices.Unsafe.dll
  System.Runtime.InteropServices.RuntimeInformation.dll
  runtimes/
    win-x64/
      native/
        e_sqlite3.dll
    linux-x64/
      native/
        libe_sqlite3.so
  wwwroot/
    index.html
    assets/
```

After assembling `wwwroot/`, publish runs the same manifest-driven validator
available to CI. The manifest owns the exact eight product DLLs, required managed
dependencies and config examples, forbidden game/legacy assemblies and assets,
Windows/Linux x64 SQLite native RID paths, Admin asset minimums, and the
`7dtd-reference` exclusion.

## Release Artifact Validation

Validate an already assembled Mod directory on Windows without invoking
publish:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/Test-ReleaseArtifact.ps1 -ArtifactPath <ModDirectory>
```

Use PowerShell 7 for the same deterministic gate on Linux:

```bash
pwsh -NoProfile -File backend/scripts/Test-ReleaseArtifact.ps1 -ArtifactPath <ModDirectory>
```

Run the self-contained validator fixture suite from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File backend/scripts/tests/Test-ReleaseArtifact.Tests.ps1
```

The fixture suite uses temporary synthetic files only. It covers the valid
layout plus missing product DLLs/native assets, forbidden files and paths,
Admin output, JSON config examples, malformed release manifests, and private
reference-content exclusion.

The script keeps the Windows and Linux native assets under their RID
directories and removes any root `e_sqlite3.dll` because the 7DTD Mod loader
scans root `.dll` files as managed assemblies. `Microsoft.Data.Sqlite` uses the
standard `SQLitePCLRaw.bundle_e_sqlite3` Batteries initialization; the Mod does
not preload native exports, install a provider, or create a ResourceManager
shim. `SQLitePCLRaw.batteries_v2.dll.config` supplies the Linux x64 `libdl.so.2`
mapping. The five Framework64 compatibility assemblies are deliberately placed
in the Mod root so normal managed assembly probing can find them.
Before SQLite composition, 7DPanel uses the game-provided Harmony to restore
`Assembly.Location` only for in-memory-loaded assemblies owned by the current
Mod. Startup fails before opening the database when that compatibility patch
cannot produce a location.

## Panel Restart Scripts

Server owners may deploy a fixed restart script in the panel data directory
and select its normalized path in the server-owned `config.json`. Use a `.cmd`
script on Windows and a `.sh` script on Linux. The panel does not accept a
script path, command, arguments, or environment variables from an HTTP request.

The panel starts the configured script process and immediately releases its
process handle. It does not wait for the script, track its exit status, or
claim that the game server restarted successfully. The script is responsible
for any shutdown, delay, startup, supervision, or remote-host behavior.

Grant the game server account permission to read and execute the script and to
access everything the script needs. Set the configured working directory so
relative paths used by the script resolve predictably. Keep secrets out of the
script and ensure the server owner controls all files reachable from it.

The script removes and rejects game-provided assemblies plus obsolete
`System.Data.SQLite.dll` and `SQLite.Interop.dll` assets. The managed
`Microsoft.Bcl.AsyncInterfaces.dll` and
`System.Runtime.CompilerServices.Unsafe.dll` versions resolved by the Mod are
published intentionally and are required by the publish validation. Exact
package and assembly versions are owned by the
[current dependency compatibility matrix](../../docs/architecture.md#当前依赖兼容矩阵).

## Start

`Start-Server` uses the fixed `startdedicated.bat` entry point. It selects the
remote configuration when `SEVENDPANEL_REMOTE_COMPUTER` is present; pass
`-Local` to force the local configuration.

Local startup launches the batch file directly. Remote startup uses WinRM to
create or update the on-demand task `7DPanel-Start-7DTD` in the remote Task
Scheduler Library. The task runs as the authenticated remote user through S4U,
stores no password, and keeps the game process independent of the WinRM
session. The script reports `Started` only after it observes the
`7DaysToDieServer` process; the default timeout is 30 seconds.

The startup flow is:

1. Load `.env.local`, then apply explicit PowerShell parameters.
2. Select the local configuration when `-Local` is present; otherwise select
   the remote configuration when `SEVENDPANEL_REMOTE_COMPUTER` is set.
3. Return `AlreadyRunning` when a `7DaysToDieServer` process already exists.
4. Validate the selected server directory and its fixed
   `startdedicated.bat` entry point.
5. Start the batch file directly for a local server. For a remote server, stop
   a stale running launcher task, register or update `7DPanel-Start-7DTD`, and
   start that task.
6. Poll for `7DaysToDieServer` for up to 30 seconds. Return `Started` with the
   process ID, or fail with task diagnostics when the process does not appear.

For an IP-based workgroup connection, add only the target host to TrustedHosts
once from an elevated PowerShell on the development machine:

```powershell
Set-Item WSMan:\localhost\Client\TrustedHosts -Value '192.0.2.10' -Concatenate -Force
```

Replace the documentation address with the actual server address. Do not use a
wildcard TrustedHosts entry unless the development network risk is explicitly
accepted. The remote server must enable WinRM and accept the current or cached
Windows credentials. The `.ps1` entry point also accepts an explicit
`PSCredential`.

## Stop

`Stop-Server` connects to `127.0.0.1:<SEVENDPANEL_TELNET_PORT>` on the selected
machine, waits for the Telnet welcome banner, sends `shutdown`, and requires an
acknowledgement before polling the game process. Remote mode performs the same
localhost-only Telnet operation through WinRM, so Telnet does not need to be
exposed externally. Progress is printed every 5 seconds and the default process
exit timeout is 60 seconds. A timeout fails the command instead of reporting a
pending shutdown. After the game exits, remote mode also stops the scheduled
task launcher so `startdedicated.bat` cannot remain blocked at its final
`pause` command.

The current helper assumes the default passwordless, loopback-only 7DTD Telnet
configuration. It does not store or transmit a Telnet password.

The shutdown flow is:

1. Load `.env.local`, select local or remote mode, and resolve the Telnet port.
2. When no game process exists, stop any stale remote launcher task and return
   `AlreadyStopped`.
3. Connect to the selected machine's loopback Telnet endpoint and consume its
   welcome banner.
4. Send `shutdown` and require an acknowledgement within 5 seconds.
5. Poll the game process for up to 60 seconds and print progress every 5
   seconds. Fail when the process remains alive after the timeout.
6. After a successful remote shutdown, stop the launcher task so its batch file
   cannot remain blocked at `pause`, then return `Stopped`.

## Health Check

`Test-HealthEndpoint` polls the configured URL for up to 30 seconds by default
and prints the HTTP status and response body. It never starts or stops the game
process. Use `-ExpectUnavailable` when verifying that shutdown released the
listener. That mode succeeds only on a transport-level connection failure; any
HTTP response proves that the listener is still reachable and fails the check.

## End-to-End Smoke Workflow

Create the ignored machine-specific configuration before the first run:

```powershell
Copy-Item backend/.env.example backend/.env.local
```

For the default remote workflow, configure the remote computer, remote server
root, publish directory, health URL, and Telnet port in `.env.local`. Then run
from the repository root:

```bat
backend\scripts\Publish-Mod.cmd
backend\scripts\Start-Server.cmd
backend\scripts\Test-HealthEndpoint.cmd -TimeoutSeconds 90
backend\scripts\Stop-Server.cmd
backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable -TimeoutSeconds 5
```

A successful cycle has all of these results:

- Publish completes with Dapper, DbUp, `Microsoft.Data.Sqlite`, SQLitePCLRaw
  Batteries/core/dynamic provider, the five Framework64 compatibility
  assemblies, `Microsoft.Bcl.AsyncInterfaces.dll`,
  `System.Runtime.CompilerServices.Unsafe.dll`,
  `runtimes/win-x64/native/e_sqlite3.dll`, and
  `runtimes/linux-x64/native/libe_sqlite3.so`. No native `e_sqlite3.dll`,
  `0Harmony.dll`, other game-provided assembly, `System.Data.SQLite.dll`, or
  `SQLite.Interop.dll` remains in the Mod root.
- Start returns `Started` or `AlreadyRunning` with the game process ID.
- The running health check returns HTTP 200.
- Stop returns `Stopped` or `AlreadyStopped`; remote mode leaves the scheduled
  task registered in the `Ready` state.
- The final unavailable check succeeds because the health listener is closed.

For a configured local server, force local selection on the lifecycle commands:

```bat
backend\scripts\Start-Server.cmd -Local
backend\scripts\Test-HealthEndpoint.cmd -TimeoutSeconds 90
backend\scripts\Stop-Server.cmd -Local
backend\scripts\Test-HealthEndpoint.cmd -ExpectUnavailable -TimeoutSeconds 5
```

The test strategy defines when these helpers are required and what evidence a
real-process smoke test must retain: [../../docs/test.md](../../docs/test.md).
