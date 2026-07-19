# Repository Instructions

## Authoritative Documents

- [README.md](README.md) - product entry point, repository layout, and runnable commands.
- [docs/PRD.md](docs/PRD.md) - product goals, scope, `CAP-##`, `NFR-##`, and acceptance contract.
- [docs/design.md](docs/design.md) - web interface information architecture, flows, states, and visual rules.
- [docs/architecture.md](docs/architecture.md) - system boundaries, runtime, components, dependency matrix, and decisions.
- [docs/test.md](docs/test.md) - requirement traceability, test levels, environments, and release gates.
- [tooling/README.md](tooling/README.md) - AI tooling status, triggers, and setup references.

## Supporting Design

- [docs/architecture/backend-target-blueprint.md](docs/architecture/backend-target-blueprint.md) - approved target backend flows and production layout. It is not current implementation evidence; promote implemented and verified facts to `docs/architecture.md`.
- [docs/architecture/admin-frontend-target-blueprint.md](docs/architecture/admin-frontend-target-blueprint.md) - approved target Admin SPA boundaries, state ownership, runtime flows, and release responsibilities. It is not current implementation evidence.

Define each fact only in its authoritative document. Use links and
`CAP-##`/`NFR-##` identifiers for cross-document traceability instead of
maintaining duplicate product, design, architecture, or test facts.

## Repository Workflow

- Model essential domain complexity through small, traceable, feedback-driven
  changes. Minimize accidental complexity with clear ownership, explicit
  boundaries, simple verifiable designs, and recoverable operations so the
  team can keep changing the system with confidence.
- When `.codegraph/` exists at the repository root, use CodeGraph before `rg`
  or direct file reads to understand or locate code. Prefer
  `codegraph explore "<question or symbols>"`; use
  `codegraph node <symbol-or-file>` for one symbol or file.
- For current framework, library, SDK, API, or CLI usage, resolve the exact
  library with Context7 and query the relevant official documentation. Do not
  rely only on model memory.
- Use `managing-project-lifecycle` to create, update, and audit project
  documentation.
- Repository instructions and established local conventions override skill
  templates and defaults. Skills do not authorize creating files or performing
  Git operations beyond the user's requested scope.
- Follow [CONTRIBUTING.md](CONTRIBUTING.md) for Conventional Commit messages
  and the documentation language policy.
- All Markdown under `docs/` is Simplified Chinese, including future
  subdirectories. Preserve code identifiers, paths, commands, API routes,
  protocol names, library names, and fenced code blocks exactly; source code
  comments and repository-level Markdown remain English.
- Inspect actual code, configuration, and tests before stating implementation
  status. Target documents are not implementation evidence.
- Preserve existing user changes. Do not modify unrelated files or rewrite or
  delete content whose origin is unclear.
- Do not run `git commit`, `git push`, `git reset`, or `git revert` unless the
  user explicitly requests that class of Git operation.
- Treat `7dtd-reference/` as a private, read-only Git submodule containing
  compatibility evidence, not product source or release content. Modify it only
  when a task explicitly includes that repository; follow its instructions,
  commit and push there first, then update the product gitlink separately. A
  future build-time dependency on its assemblies must be recorded in
  `docs/architecture.md` and `docs/test.md`.
- Keep repository-wide aggregate build and test entry points in `README.md`.
  Let `docs/test.md` own verification levels, environments, and release gates.
  Keep exact app, module, and script commands in the closest owning README,
  and link to them from higher-level documents instead of duplicating command
  blocks. Repeat only short, stable aggregate commands needed for repository
  quick start; do not add machine-specific paths.

## Documentation Updates

- When `managing-project-lifecycle` routes a multi-step change to
  `docs/superpowers/`, create and obtain approval for
  `specs/YYYY-MM-DD-<feature>-design.md` before creating
  `plans/YYYY-MM-DD-<feature>.md`. Each plan must link exactly one primary
  dated design spec. A request for an inline plan does not authorize creating
  either file.

- For changes to product goals, scope, capabilities, or externally observable
  behavior, update `docs/PRD.md` first, then assess design, architecture, and
  test impact.
- For navigation, interaction, page-state, or visual-rule changes, update
  `docs/design.md` and assess browser E2E coverage.
- For component boundaries, lifecycle, data, interfaces, deployment, or
  dependency-matrix changes, update `docs/architecture.md` and assess test
  strategy impact before changing `docs/test.md`.
- Update `docs/architecture/backend-target-blueprint.md` only when the approved
  future backend flows, project boundaries, or production file responsibilities
  change. Never use target entries as evidence that code exists.
- Update `docs/architecture/admin-frontend-target-blueprint.md` only when the
  approved future Admin application boundaries, state ownership, runtime flows,
  dependency direction, or production asset responsibilities change.
- For test environments, automation, or release-gate changes, update
  `docs/test.md`. For runnable-command changes, update the closest owning
  README and synchronize repository-wide aggregate entry points or links in
  `README.md`.
- For game-version compatibility changes, update `docs/architecture.md` and
  `docs/test.md` from verified external reference evidence. Do not update the
  external reference repository as part of product-repository documentation
  work.
- Add only released user-visible or operator-visible changes to `CHANGELOG.md`.
  Create incident records only for actual production incidents.
