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
- Write Markdown files directly under `docs/` (`docs/*.md`) in Chinese. These
  are the living product, design, architecture, test, and operations documents.
- Write Markdown outside `docs/` and in every `docs/` subdirectory in English.
  This includes dated change records under `docs/superpowers/specs/` and
  `docs/superpowers/plans/`, and incident records under `docs/incidents/`.
  Keep file names, code comments, identifiers, and commit messages in English.

## Documentation and Verification

- Update the authoritative document identified in `AGENTS.md` when behavior,
  architecture, tests, or repository commands change.
- Run the checks relevant to the files you changed. Do not claim checks passed
  when the corresponding project or command does not exist.
- Keep changes scoped to one concern where practical. Separate unrelated
  documentation, repository maintenance, and implementation changes.
