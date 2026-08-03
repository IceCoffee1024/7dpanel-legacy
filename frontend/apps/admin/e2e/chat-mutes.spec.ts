import { expect, test } from '@playwright/test'

import { gotoAdmin, mockAdminApi, monitorBrowserErrors, useStoredSession } from './support/admin'

const muteId = 'EOS_browser_mute'
const activeMute = {
  crossplatformId: muteId,
  displayName: 'Browser muted player',
  reason: 'Browser confirmation smoke',
  mutedUntilUtc: null,
  createdBy: 'owner',
  createdAtUtc: '2026-07-31T08:00:00Z',
  updatedBy: 'owner',
  updatedAtUtc: '2026-07-31T08:00:00Z',
}

test('releasing a chat mute requires confirmation and refreshes the list', async ({ page }) => {
  await useStoredSession(page, 'Owner')
  await mockAdminApi(page)
  const browserErrors = monitorBrowserErrors(page)
  let released = false
  let releaseRequests = 0

  await page.route('**/api/v1/chat/mutes?**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        mutes: released ? [] : [activeMute],
        nextCursorUpdatedAtUtc: null,
        nextCursorCrossplatformId: null,
      }),
    })
  })
  await page.route(`**/api/v1/chat/mutes/${muteId}`, async (route) => {
    releaseRequests++
    expect(route.request().method()).toBe('DELETE')
    expect(route.request().headers().authorization).toBe('Bearer 7dp_t_browser-smoke.secret')
    released = true
    await route.fulfill({ status: 204 })
  })

  await gotoAdmin(page, '/community/chat/mutes')
  const releaseButton = page.getByTestId(
    test.info().project.name.includes('390x844')
      ? `release-mute-mobile-${muteId}`
      : `release-mute-desktop-${muteId}`,
  )
  await expect(releaseButton).toBeVisible()
  await releaseButton.click()

  expect(releaseRequests).toBe(0)
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText(muteId)
  await expect(page.getByTestId('confirm-release-mute')).toBeVisible()

  await page.getByTestId('confirm-release-mute').click()

  await expect.poll(() => releaseRequests).toBe(1)
  await expect(dialog).toHaveCount(0)
  await expect(page.getByText('Browser muted player')).toHaveCount(0)
  expect(browserErrors.errors).toEqual([])
  browserErrors.dispose()
})
