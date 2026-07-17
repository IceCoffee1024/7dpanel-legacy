# 7DPanel

## What This Is

7DPanel is a self-hosted server administration panel for 7 Days to Die server
owners. Its backend runs as a Mod DLL inside the 7DTD Dedicated Server Mono
process and provides status, player management, logs, backup and restore,
announcement automation, and auditing through a web interface.

The repository currently defines the product, design, and architecture. The
application directories have not been initialized with framework code. The
documents describe target contracts, not completed features.

## Repository Layout

```text
backend/                        Mod DLL, embedded Web API, and backend tests
frontend/apps/admin/            Web administration application
frontend/apps/marketing/        Marketing site application
7dtd-reference/                 Private read-only compatibility reference submodule
docs/                           Product, design, architecture, and test contracts
scripts/                        Product repository automation
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

Application build, unit test, and end-to-end commands do not exist yet. See the
[test strategy](docs/test.md) for the required verification levels and release
gates. Commands for maintaining local game reference material belong in the
external reference repository and are not product-repository commands.

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
- [Test strategy](docs/test.md) - requirement traceability, test levels, environments, and release gates
