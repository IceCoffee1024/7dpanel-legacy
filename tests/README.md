# Cross-System Tests

This directory is for smoke, end-to-end, and release verification across the
admin frontend, backend, and official 7DTD process. Unit and component tests
remain in their owning application directories. `docs/test.md` defines the
complete test scope and release gates.

The backend already has direct tests under `backend/tests/`. No cross-system
tests are runnable from this directory yet; add them here only when a workflow
spans application boundaries or requires an official 7DTD process.
