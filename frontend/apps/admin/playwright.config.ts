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
  const value = process.env[name] ?? localEnv[name]
  if (value === undefined || value === '' || value === 'undefined' || value === 'null')
    delete process.env[name]
  else
    process.env[name] = value
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
const externalAdminUrl = process.env.SEVENDPANEL_ADMIN_URL
const localAdminUrl = 'http://127.0.0.1:4173'
if (externalAdminUrl && !/^https?:\/\//u.test(externalAdminUrl))
  throw new Error('SEVENDPANEL_ADMIN_URL must be an absolute HTTP(S) URL.')
const baseURL = externalAdminUrl || localAdminUrl
const selectedBrowser = {
  ...devices['Desktop Chrome'],
  baseURL,
  channel: selectedBrowserChannel,
}
const mockBrowser = {
  ...selectedBrowser,
  baseURL: localAdminUrl,
}

export default defineConfig({
  outputDir: './node_modules/.cache/playwright/test-results',
  testDir: './tests/e2e',
  use: {
    baseURL,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: `${browserChannels[selectedBrowserChannel]} - real OWIN`,
      use: selectedBrowser,
    },
    {
      name: `${browserChannels[selectedBrowserChannel]} - mock desktop`,
      testDir: './e2e',
      use: {
        ...mockBrowser,
        viewport: { width: 1280, height: 900 },
      },
    },
    {
      name: `${browserChannels[selectedBrowserChannel]} - mock 390x844`,
      testDir: './e2e',
      testIgnore: '**/admin-auth.spec.ts',
      use: {
        ...mockBrowser,
        viewport: { width: 390, height: 844 },
      },
    },
  ],
  webServer: {
    command: 'pnpm dev --host 127.0.0.1 --port 4173',
    url: localAdminUrl,
    reuseExistingServer: !process.env.CI,
  },
})
