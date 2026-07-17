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
current validation slice implements `ModMain`, `ModHost`, the 7DTD lifecycle
adapter, Katana self-hosting, and `/health` plus `/api/v1/health`. Persistence,
main-thread game actions, authentication, and product capabilities are not
implemented yet.

Development publish, server-control, and health-check helpers are documented in
the [script guide](scripts/README.md). Machine-specific values belong in the
ignored `.env.local`; the tracked `.env.example` defines the available keys.

At runtime, `config.example.json` is the versioned template and `config.json` is
the server-owned configuration. The Mod creates a default `config.json` when it
is missing. The publish project never includes the server-owned file or the
`data/` directory.

Runtime defaults are defined by `PanelHostConfig.CreateDefault()`; an automated
test compares those values with `config.example.json` so the operator template
cannot silently drift from fallback behavior.

The root `docs/` directory owns the product contract, system architecture, and
cross-system release gates. Authoritative build and test commands are kept in
the root `README.md` and `docs/test.md`.
