import { test } from '@playwright/test'

import {
  hasRealOwinNavigationEnvironment,
  missingRealOwinNavigationEnvironmentReason,
  runOwnerAdminNavigationScenario,
  runViewerAdminNavigationScenario,
} from './support/adminNavigation'

test.skip(!hasRealOwinNavigationEnvironment, missingRealOwinNavigationEnvironmentReason)

test('owner can navigate across admin pages without Vue render errors', async ({ page }) => {
  await runOwnerAdminNavigationScenario(page, { mockApi: false })
})

test('viewer receives role-filtered fixed entries and local tabs', async ({ page }) => {
  await runViewerAdminNavigationScenario(page, { mockApi: false })
})
