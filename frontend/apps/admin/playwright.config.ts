import process from 'node:process'
import { defineConfig, devices } from '@playwright/test'

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
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
