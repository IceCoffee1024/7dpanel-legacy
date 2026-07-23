import process from 'node:process'
import { defineConfig, devices } from '@playwright/test'
import { loadEnv } from 'vite'

const localEnv = loadEnv('development', process.cwd(), '')
const smokeEnvironmentVariables = [
  'SEVENDPANEL_ADMIN_URL',
  'PANEL_USERNAME',
  'PANEL_PASSWORD',
] as const

for (const name of smokeEnvironmentVariables) {
  process.env[name] ||= localEnv[name]
}

const browserChannels = {
  chrome: 'Google Chrome',
  chromium: 'Playwright Chromium',
  msedge: 'Microsoft Edge',
} as const
const browserChannel = process.env.SEVENDPANEL_E2E_BROWSER
  || localEnv.SEVENDPANEL_E2E_BROWSER
  || 'msedge'

if (!(browserChannel in browserChannels)) {
  throw new Error(
    `Unsupported SEVENDPANEL_E2E_BROWSER "${browserChannel}". Expected chrome, msedge, or chromium.`,
  )
}

const selectedBrowserChannel = browserChannel as keyof typeof browserChannels

export default defineConfig({
  outputDir: './node_modules/.cache/playwright/test-results',
  testDir: './tests/e2e',
  use: {
    baseURL: process.env.SEVENDPANEL_ADMIN_URL || 'http://127.0.0.1:18080',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: browserChannels[selectedBrowserChannel],
      use: {
        ...devices['Desktop Chrome'],
        channel: selectedBrowserChannel,
      },
    },
  ],
})
