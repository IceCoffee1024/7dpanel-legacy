import { test } from '@playwright/test'

import {
  runOwnerAdminNavigationScenario,
  runViewerAdminNavigationScenario,
} from '../tests/e2e/support/adminNavigation'

test('owner can navigate across admin pages without Vue render errors', async ({ page }) => {
  await runOwnerAdminNavigationScenario(page, { mockApi: true })
})

test('viewer receives role-filtered fixed entries and local tabs', async ({ page }) => {
  await runViewerAdminNavigationScenario(page, { mockApi: true })
})
