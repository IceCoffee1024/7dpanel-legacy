# 7DPanel Admin

The self-hosted administration interface for 7DPanel. The current application
contains the responsive product shell and an Overview route. Backend health,
authentication, and operational features are introduced as verified vertical
slices.

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

Verify the current application with:

```powershell
pnpm lint
pnpm typecheck
pnpm build
```

`pnpm preview` serves the production build for local inspection. Production
hosting is owned by the 7DPanel Mod and does not use the Vite development or
preview server.

## Package Ownership

This application owns its `package.json`, `pnpm-workspace.yaml`,
`pnpm-lock.yaml`, and pinned pnpm version. A root `frontend/` workspace is
introduced only after multiple applications have a demonstrated shared-package
or coordinated-build requirement.

## Template Provenance

The initial application shell was derived from the Nuxt UI Vue Dashboard
template under the MIT license. The unmodified imported baseline is preserved
in repository commit `058da3c`; see [LICENSE](LICENSE).
