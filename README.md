# 7DPanel Workspace

## What This Is

7DPanel is a self-hosted server administration panel for 7 Days to Die server
owners. Its backend runs as a Mod DLL inside the 7DTD Dedicated Server Mono
process and provides status, player management, logs, backup and restore,
announcement automation, and auditing through a web interface.

The workspace is currently defining the product, design, and architecture. The
three product implementation repositories have not been initialized with
application code. The documents describe target contracts, not completed
features.

## Repository Layout

```text
docs/                   Cross-repository product, design, architecture, and test contracts
scripts/                Cross-repository setup, integration, and release automation
tests/                  Cross-repository smoke, E2E, and release verification
7dtd-panel-frontend/    Planned public web administration frontend repository
7dtd-panel-backend/     Planned private Mod DLL and embedded Web API repository
7dtd-marketing/         Planned private marketing site source repository
7dtd-reference/         Planned private game compatibility reference repository
```

The workspace and its four component directories are initialized as local Git
repositories with planned GitHub `origin` URLs. The remote repositories do not
exist yet, so `.gitmodules` has not been created. Formal submodule conversion
must wait until the component repositories have been created and pushed; local
machine paths must not be committed as permanent submodule URLs.

Product-level requirements and cross-system design, architecture, and test
contracts are maintained only in the workspace `docs/` directory. Component
repositories own their implementation, direct tests, and contributor guidance;
they do not duplicate the product contract.

`7dtd-reference/` remains private and is not part of the 7DPanel distribution.
See the [reference guide](7dtd-reference/README.md) for its content and access
boundaries.

## Test and Checks

Application build, unit test, and end-to-end commands do not exist yet. See the
[test strategy](docs/test.md) for the required verification levels and release
gates. Commands for maintaining local game reference material belong in the
[reference guide](7dtd-reference/README.md).

## Documentation

- [Repository agent instructions](AGENTS.md) - authoritative-document navigation and repository workflow
- [Contributing guide](CONTRIBUTING.md) - commit convention and repository contribution workflow
- [Product requirements](docs/PRD.md) - goals, scope, capabilities, and acceptance contract
- [Product design](docs/design.md) - information architecture, core flows, states, and visual rules
- [System architecture](docs/architecture.md) - boundaries, components, data, dependency matrix, and decisions
- [Test strategy](docs/test.md) - requirement traceability, test levels, environments, and release gates
- [Reference guide](7dtd-reference/README.md) - game-version reference layout and maintenance tooling
