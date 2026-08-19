# Contributing

## Commit Messages

The 7DPanel product repository uses
[Conventional Commits](https://www.conventionalcommits.org/):

```text
<type>[optional scope][!]: <description>
```

Use one of these types:

- `feat`: a user-visible capability or behavior.
- `fix`: a bug fix.
- `docs`: documentation-only changes.
- `refactor`: internal code changes without behavior changes.
- `perf`: performance improvements.
- `test`: test additions or corrections.
- `build`: build system or dependency changes.
- `ci`: continuous integration changes.
- `chore`: repository maintenance that fits no other type.
- `revert`: reverts an earlier commit.

Scopes are optional. Use a stable component or subsystem name when it adds
clarity, such as `frontend`, `backend`, `marketing`, `docs`, or `repo`. The
external reference repository follows its own contribution guidance.

Write the description in imperative mood, keep it concise, and do not end it
with a period. Add a body when the reason or trade-off is not obvious. Mark a
breaking change with `!` before the colon or a `BREAKING CHANGE:` footer.

Examples:

```text
feat(frontend): add server status panel
fix(backend): stop OWIN host during shutdown
docs: clarify the restore workflow
chore(repo): update repository layout
feat(api)!: replace the player action response
```

## Repository Workflow

- Keep each commit focused on one concern and use the scope that owns the
  change when it improves clarity.
- Treat `7dtd-reference/` as a private reference submodule, not product source.
  Do not mix changes to its files with product changes. When the reference
  repository must change, commit and push that repository first, then update
  the pinned submodule commit in a separate product-repository commit.
- Do not include submodule contents in product release artifacts or generated
  packages.
- Do not commit local machine paths, credentials, generated secrets, or product
  build outputs.
- Write all Markdown under `docs/` in Simplified Chinese, including files in
  `docs/architecture/`, `docs/superpowers/`, and future document
  subdirectories.
- Keep code identifiers, file paths, API routes, commands, protocol names,
  library names, and fenced code blocks in their original form. Keep source
  code comments, file names, identifiers, and commit messages in English.
- Markdown outside `docs/` remains English unless a document explicitly
  defines another audience or language requirement.

## Documentation and Verification

- Update the authoritative document identified in `AGENTS.md` when behavior,
  architecture, tests, or repository commands change.
- Run the checks relevant to the files you changed. Do not claim checks passed
  when the corresponding project or command does not exist.
- Keep changes scoped to one concern where practical. Separate unrelated
  documentation, repository maintenance, and implementation changes.

## Complexity and Cognitive-Budget Review Fields

For a non-trivial change, the review description must state:

- `risk level`: `A`, `B`, or `C`, selected using the definitions in
  `docs/simplification/README.md#风险分级与设计强度`;
- `Golden Path`: `simple query`, `ordinary modification`, or `dangerous
  asynchronous operation`; a fourth path must name the concrete production
  constraint that prevents use of the three standard paths;
- `concepts added/removed`: the named concepts, layers, state records,
  interfaces, routes, or operational steps added and removed, and why the
  resulting path is easier to understand;
- `fact source`: the authoritative document that owns every changed product,
  design, architecture, or test fact, or `none` when the change creates no
  such fact;
- `AI-generated surface`: `none`, or the generated paths, generator/input
  source, and the human-reviewed invariants and failure semantics;
- `primary capability`: the single Operations, Players, Community, Economy,
  Automation, Administration, or Platform owner;
- `production reason for new interface`: the external boundary, second
  production consumer, stable duplication, or dependency direction it protects;
- `background lifecycle`: start, stop, drain, failure, and recovery semantics
  for every new background task;
- `migration/recovery`: schema migration, idempotency, rollback, and restore
  behavior for new persisted state;
- `navigation task`: the user task and existing secondary/context location for
  any new page or route;
- `verification/rollback`: the targeted and aggregate checks, required
  real-boundary evidence, recoverable operator action, and failure handling;
- `real-boundary evidence`: the concrete game, browser, external-service, or
  release artifact evidence required before claiming verification;
- `structural improvement`: at least one `User`, `Developer`, or `Recovery`
  outcome, plus the concrete operational concern before the change and how it
  is improved after the change;
- `production evidence`: the affected `CAP-##` and/or `J#`, and the actual
  browser, game, external-service, release-artifact, or recovery evidence. If
  that evidence was not run, state `Blocked environment` and why.

Local, static, unit, integration, and complexity checks are useful supporting
checks, but must not be labeled production validation or substituted for the
production evidence above.

These review fields describe the proposed change; they do not establish
capability maturity or verification status. `docs/test.md` is the sole
capability-maturity ledger and must be updated only when its authoritative
evidence or gate facts change.
