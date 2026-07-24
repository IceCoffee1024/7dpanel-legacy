# 7DPanel

## What This Is

7DPanel is a self-hosted server administration panel for 7 Days to Die server
owners. Its backend runs as a Mod DLL inside the 7DTD Dedicated Server Mono
process and provides status, player management, logs, backup and restore,
announcement automation, and auditing through a web interface.

The repository defines the product, design, and architecture. The backend has
a buildable and testable `net48` solution with the Mod lifecycle, in-process
OWIN hosting, SQLite-backed bootstrap Owner authentication, persistent opaque
Bearer tokens, authenticated named SSE, and dynamic console commands that run
through the game main thread. The backend also exposes an Owner-only
event-projected 25-field online-player snapshot and typed online-player kick action with
audit records. The Admin application currently provides an Owner login, explicit
tab/browser Bearer session persistence, protected Overview, a compact online-player
list with a read-only details slideover, and API Key routes, with complete
English/Simplified Chinese support for those surfaces. It does not yet consume SSE or the console command API. Full user management, other
state-changing game actions, backups, announcements, and audit-query
experiences are not implemented. The
Marketing application has not been initialized.
Target documents describe approved direction, not completed features.

## Repository Layout

```text
backend/                        Mod DLL, embedded Web API, and backend tests
frontend/apps/admin/            Web administration application
frontend/apps/marketing/        Marketing site application
7dtd-reference/                 Private read-only compatibility reference submodule
docs/                           Product, design, architecture, and test contracts
tests/                          Cross-system smoke, E2E, and release verification
tooling/                        Optional AI-assisted development guidance
```

Product-level requirements and cross-system design, architecture, and test
contracts are maintained only in the root `docs/` directory. Application
directories own their implementation and direct tests; they do not duplicate
the product contract.

The future player application and shared frontend packages are intentionally
not created yet. Add them only after the player storefront is approved or a
real cross-application reuse requirement exists.

`7dtd-reference/` is a private Git submodule pinned to a reviewed commit. It
contains versioned 7DTD runtime, decompiled, and shared reference material; it
is not product source and must not be included in a 7DPanel distribution.
Product-repository work treats the submodule as read-only. Reference changes
are made and pushed in the `7dtd-reference` repository first, then the product
repository records the new submodule commit.

Collaborators who need compatibility evidence must have access to both private
repositories and clone the product with:

```powershell
git clone --recurse-submodules https://github.com/IceCoffee1024/7dpanel.git
```

After a normal clone, initialize the pinned reference explicitly with
`git submodule update --init --recursive`.

## Test and Checks

Initialize the private reference submodule before building the backend. From
the repository root, run:

```powershell
dotnet restore backend/7DPanel.sln
dotnet build backend/7DPanel.sln --configuration Release --no-restore
dotnet test backend/7DPanel.sln --configuration Release --no-build --no-restore
```

Admin verification requires automated tests, lint, type checking, and a
production build. Run the exact commands from the
[Admin application guide](frontend/apps/admin/README.md#verification).

See the [test strategy](docs/test.md) for the required verification levels and
release gates. On Windows, use the `.cmd` wrappers as the default entry points:

```bat
backend\scripts\Publish-Mod.cmd
backend\scripts\Start-Server.cmd
backend\scripts\Stop-Server.cmd
backend\scripts\Test-HealthEndpoint.cmd
```

The wrappers use Windows PowerShell 5.1, which is included with supported
Windows installations; PowerShell 7 is not required. The matching `.ps1` files
expose parameters for automation. See the
[backend script guide](backend/scripts/README.md) for local configuration,
publishing, WinRM, scheduled-task startup, graceful shutdown, and health-check
behavior. Commands for maintaining game reference material belong in the
external reference repository.

## AI-Assisted Development

AI tooling is not required to build or run 7DPanel. When an AI agent is used,
it must follow the applicable repository instructions in [AGENTS.md](AGENTS.md).
See the [AI tooling guide](tooling/README.md) for catalogued MCP servers, agent
skills, adoption status, and setup references.

## Documentation

- [Repository agent instructions](AGENTS.md) - authoritative-document navigation and repository workflow
- [Contributing guide](CONTRIBUTING.md) - commit convention and repository contribution workflow
- [Product requirements](docs/PRD.md) - goals, scope, capabilities, and acceptance contract
- [Product design](docs/design.md) - information architecture, core flows, states, and visual rules
- [System architecture](docs/architecture.md) - boundaries, components, data, dependency matrix, and decisions
- [Backend target architecture blueprint](docs/architecture/backend-target-blueprint.md) - approved future backend flows and production layout, not current implementation evidence
- [Admin frontend target architecture blueprint](docs/architecture/admin-frontend-target-blueprint.md) - approved future Admin SPA boundaries, runtime flows, and release responsibilities
- [Test strategy](docs/test.md) - requirement traceability, test levels, environments, and release gates
- [Backend script guide](backend/scripts/README.md) - publish, server control, and health-check helpers
