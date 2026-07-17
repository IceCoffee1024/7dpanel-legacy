# AI Tooling

The MCP servers and skills listed here are development aids. They are not
product runtime or build dependencies, and non-AI contributions do not require
them. When an AI agent is used, it must follow [AGENTS.md](../AGENTS.md) and use
each required tool or skill when its documented trigger applies.

Statuses have the following meanings:

- **Required when applicable:** already required by repository instructions
  when its documented trigger matches the task.
- **Candidate:** worth evaluating, but not part of the repository workflow.
- **Conditional:** activated only when a parent workflow routes to it or the
  relevant technology is adopted.

## MCP Servers

| Tool | Status | Purpose | Official references |
|---|---|---|---|
| CodeGraph | Required when applicable | Index and explore repository symbols and call paths when a `.codegraph/` directory exists. | [GitHub](https://github.com/colbymchenry/codegraph) |
| Context7 | Required when applicable | Retrieve current, version-specific documentation for library, framework, SDK, API, and CLI work. | [GitHub](https://github.com/upstash/context7) |
| Chrome DevTools MCP | Candidate | Let agents inspect and automate a live Chromium browser for frontend debugging and verification. | [GitHub](https://github.com/ChromeDevTools/chrome-devtools-mcp) |
| Nuxt UI MCP | Conditional | Provide component metadata, examples, and setup guidance if the frontend adopts Nuxt UI. | [MCP documentation](https://ui.nuxt.com/docs/getting-started/ai/mcp), [GitHub](https://github.com/nuxt/ui) |
| Inspira UI MCP | Conditional | Provide component guidance if the frontend adopts Inspira UI. | [MCP documentation](https://inspira-ui.com/docs/mcp), [GitHub](https://github.com/unovue/inspira-ui) |

For CodeGraph, follow the official installation instructions, then initialize
the index from the repository root:

```text
codegraph install
codegraph init
```

For Context7, run its interactive setup and select the supported AI client:

```text
ctx7 setup
```

For the other servers, follow their current official documentation. Client
configuration formats change over time, so this repository does not duplicate
them.

## Agent Skills

| Skill or workflow | Source | Status | Trigger and intended use |
|---|---|---|---|
| `managing-project-lifecycle` | [IceCoffee1024/skills](https://github.com/IceCoffee1024/skills) | Required when applicable | Create, update, route, or audit project documentation. |
| `writing-product-prds` | [IceCoffee1024/skills](https://github.com/IceCoffee1024/skills) | Conditional | Used when the lifecycle workflow routes a new or materially revised product contract to PRD work. |
| Dated design and implementation workflow | [obra/superpowers](https://github.com/obra/superpowers) | Conditional | Used when the lifecycle workflow routes a change to `docs/superpowers/specs/` or `docs/superpowers/plans/`. |
| Other superpowers workflows | [obra/superpowers](https://github.com/obra/superpowers) | Candidate | Evaluate discovery, worktree, implementation, debugging, review, and verification workflows individually before adoption. |
| Vue and Vite ecosystem skills | [antfu/skills](https://github.com/antfu/skills) | Conditional | Use the relevant Vue, Vite, Vitest, Pinia, pnpm, or related skill only after the frontend stack adopts that technology. |

Skills are agent instructions, not evidence that a tool or framework has been
selected for the product. Repository instructions in [AGENTS.md](../AGENTS.md)
take precedence over skill defaults.

The project-lifecycle skill remains the routing entry point. When it routes a
change to a dated superpowers design or implementation plan, the configured
superpowers workflow applies to that change. Those dated records must link to
the relevant living product, design, architecture, and test documents; they do
not replace or redefine those authoritative sources.

## Setup Policy

- Prefer user-level MCP and skill configuration unless a shared project file is
  intentionally reviewed and adopted by the team.
- Never commit API keys, access tokens, credentials, generated secrets, or
  machine-specific absolute paths.
- Promote a candidate only when it solves a current need and works for the AI
  clients used by the team. Record required agent behavior and its trigger in
  `AGENTS.md`, then update the status in this guide.
- Pin versions only when reproducibility requires it; otherwise follow the
  current official setup documentation.
