# Contributing

## Commit Messages

All repositories in the 7DPanel workspace use
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
clarity, such as `frontend`, `backend`, `marketing`, `reference`, or `workspace`.

Write the description in imperative mood, keep it concise, and do not end it
with a period. Add a body when the reason or trade-off is not obvious. Mark a
breaking change with `!` before the colon or a `BREAKING CHANGE:` footer.

Examples:

```text
feat(frontend): add server status panel
fix(backend): stop OWIN host during shutdown
docs: clarify the restore workflow
chore(workspace): update backend submodule
feat(api)!: replace the player action response
```

## Repository Workflow

- Commit implementation changes in the component repository that owns them.
- Push the component commit before making the workspace reference it.
- When the workspace adopts a new component revision, commit the updated
  submodule pointer separately in the workspace repository.
- Do not commit local machine paths, credentials, generated secrets, or product
  build outputs.
- Keep root `docs/` product and system documents in Chinese. Keep all other
  Markdown, code comments, identifiers, and commit messages in English.

## Documentation and Verification

- Update the authoritative document identified in `AGENTS.md` when behavior,
  architecture, tests, or repository commands change.
- Run the checks relevant to the files you changed. Do not claim checks passed
  when the corresponding project or command does not exist.
- Keep changes scoped to one concern where practical. Separate unrelated
  documentation, repository maintenance, and implementation changes.

