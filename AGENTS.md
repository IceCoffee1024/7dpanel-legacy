# Repository Instructions

## Authoritative Documents

- [README.md](README.md) - workspace entry point, repository layout, and runnable commands.
- [docs/PRD.md](docs/PRD.md) - product goals, scope, `CAP-##`, `NFR-##`, and acceptance contract.
- [docs/design.md](docs/design.md) - web interface information architecture, flows, states, and visual rules.
- [docs/architecture.md](docs/architecture.md) - system boundaries, runtime, components, dependency matrix, and decisions.
- [docs/test.md](docs/test.md) - requirement traceability, test levels, environments, and release gates.
- [7dtd-reference/README.md](7dtd-reference/README.md) - game reference layout, purpose, and compatibility boundary.

Define each fact only in its authoritative document. Use links and
`CAP-##`/`NFR-##` identifiers for cross-document traceability instead of
maintaining duplicate product, design, architecture, or test facts.

## Repository Workflow

- When `.codegraph/` exists at the repository root, use CodeGraph before `rg`
  or direct file reads to understand or locate code. Prefer
  `codegraph explore "<question or symbols>"`; use
  `codegraph node <symbol-or-file>` for one symbol or file.
- For current framework, library, SDK, API, or CLI usage, resolve the exact
  library with Context7 and query the relevant official documentation. Do not
  rely only on model memory.
- Use `managing-project-lifecycle` to create, update, and audit project
  documentation.
- Commit messages follow Conventional Commits. See
  [CONTRIBUTING.md](CONTRIBUTING.md) for the repository convention and examples.
- Write root `docs/` product and system documents in Chinese. Write all other
  Markdown in English. Do not maintain duplicate bilingual copies.
- Inspect actual code, configuration, and tests before stating implementation
  status. Target documents are not implementation evidence.
- Preserve existing user changes. Do not modify unrelated files or rewrite or
  delete content whose origin is unclear.
- Maintain build, test, and maintenance commands only in `README.md` and
  `docs/test.md`. Do not duplicate them here or add machine-specific paths.

## Documentation Updates

- For changes to product goals, scope, capabilities, or externally observable
  behavior, update `docs/PRD.md` first, then assess design, architecture, and
  test impact.
- For navigation, interaction, page-state, or visual-rule changes, update
  `docs/design.md` and assess browser E2E coverage.
- For component boundaries, lifecycle, data, interfaces, deployment, or
  dependency-matrix changes, update `docs/architecture.md` and `docs/test.md`.
- For test environments, commands, automation, or release-gate changes, update
  `docs/test.md` and synchronize runnable commands with `README.md`.
- For game-version references, runtime evidence, decompiled material, or shared
  asset rules, update `7dtd-reference/README.md` and the architecture
  compatibility matrix when applicable.
- Add only released user-visible or operator-visible changes to `CHANGELOG.md`.
  Create incident records only for actual production incidents.
