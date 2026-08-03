# Cross-System Tests

This directory is for smoke, end-to-end, and release verification across the
admin frontend, backend, and official 7DTD process. Unit and component tests
remain in their owning application directories. `docs/test.md` defines the
complete test scope and release gates.

The backend already has direct tests under `backend/tests/`. No cross-system
tests are runnable from this directory yet; add them here only when a workflow
spans application boundaries or requires an official 7DTD process.

Repository governance checks are intentionally runnable without external
services:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/docs/Test-CapabilityMaturity.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Measure-Complexity.ps1 -RepositoryRoot (Get-Location)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-ComplexityBudget.ps1 -RepositoryRoot (Get-Location)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/complexity/Test-BackendTestTaxonomy.ps1
```

These checks inspect source and documentation only. They do not replace the
cross-system, browser, release-artifact, or real 7DTD gates owned by
`docs/test.md`.

Journey safety harnesses validate preflight and evidence formatting without
touching a server or player:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/journeys/tests/Test-PlayerJourney.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/journeys/tests/Test-RestoreDrill.Tests.ps1
```

`Test-PlayerJourney.ps1` requires an exact stable cross-platform identity and
never guesses by name or entity ID. `Test-RestoreDrill.ps1` requires an
isolated root, expected world name, backup ID or explicit creation policy, and
destructive confirmation; its current preflight lane remains skipped until a
frozen candidate and rollback target are supplied.
