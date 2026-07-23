# 7DPanel Admin

The self-hosted administration interface for 7DPanel. The current application
contains the responsive product shell, an Owner login flow, a protected
Overview route, and a protected online players route.

## Development

Run commands from this directory:

```powershell
pnpm install --frozen-lockfile
pnpm dev
```

The development server proxies browser requests under `/api` to
`http://127.0.0.1:18080` by default. Copy `.env.example` to `.env.local` to
point the proxy at another development backend. The browser client keeps using
relative `/api` paths, and production builds do not embed the proxy target.

`pnpm preview` serves the production build for local inspection. Production
hosting is owned by the 7DPanel Mod and does not use the Vite development or
preview server.

## Verification

Run all current application gates from this directory:

```powershell
pnpm lint
pnpm typecheck
pnpm test
pnpm build
```

The real OWIN browser suite is a separate environment gate. Playwright loads
these values from `.env.local`, while variables already present in the process
environment take precedence. It requires a running test deployment and all of
these variables:

- `SEVENDPANEL_ADMIN_URL`
- `PANEL_USERNAME`
- `PANEL_PASSWORD`

`SEVENDPANEL_E2E_BROWSER` selects `msedge`, `chrome`, or `chromium` and defaults
to the system-installed Microsoft Edge on Windows. `chrome` uses the installed
Google Chrome. `chromium` uses Playwright's version-pinned browser and requires
`pnpm exec playwright install chromium` before the suite runs.

Run it only against that controlled environment:

```powershell
pnpm test:e2e
```

When any required variable is absent, the suite reports its real-environment
tests as skipped. A skipped suite is not evidence that the browser smoke passed.
An unsupported browser value fails during Playwright configuration instead of
silently selecting another browser.

## Package Ownership

This application owns its `package.json`, `pnpm-workspace.yaml`,
`pnpm-lock.yaml`, and pinned pnpm version. A root `frontend/` workspace is
introduced only after multiple applications have a demonstrated shared-package
or coordinated-build requirement.

## Template Provenance

The initial application shell was derived from the Nuxt UI Vue Dashboard
template under the MIT license. The unmodified imported baseline is preserved
in repository commit `058da3c`; see [LICENSE](LICENSE).
