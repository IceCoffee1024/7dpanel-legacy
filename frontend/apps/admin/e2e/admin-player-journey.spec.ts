import { expect, test } from '@playwright/test'

import { gotoAdmin, mockAdminApi, useStoredSession } from './support/admin'

const crossplatformId = 'EOS_browser_player'
const player = {
  entityId: 7,
  name: 'Browser Journey Player',
  platformIdentity: { combinedId: 'Steam_browser_player', platform: 'Steam' },
  crossplatformIdentity: { combinedId: crossplatformId, platform: 'EOS' },
  deviceType: 'windows',
  ip: '192.0.2.10',
  ping: 42,
  compatibilityVersion: 'V 3.0.1',
  discordUserId: null,
  permissionLevel: 1000,
  position: { x: 100.5, y: 51, z: 200.25 },
  isDead: false,
  health: 93,
  maxHealth: 100,
  level: 18,
  playGroup: null,
  lastLoginUtc: null,
  gameStage: null,
  expToNextLevel: null,
  skillPoints: null,
  bedroll: null,
  score: 827,
  zombieKills: 317,
  playerKills: 2,
  deaths: 4,
  totalTimePlayedMinutes: 4823.5,
  distanceWalkedMeters: 127540.75,
  totalItemsCrafted: 2360,
  longestLifeMinutes: 920.25,
  currentLifeMinutes: 134.5,
  observedAtUtc: '2026-08-03T08:00:00Z',
}

async function installOwnerJourneyApi(page: Parameters<typeof mockAdminApi>[0]) {
  let kicked = false
  let kickRequests = 0
  await page.route('**/api/v1/players/online', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ players: kicked ? [] : [player] }),
    })
  })
  await page.route(`**/api/v1/players/history/${crossplatformId}/snapshots?**`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ snapshots: [], gaps: [], nextBeforeSnapshotId: null }) })
  })
  await page.route(`**/api/v1/players/history/${crossplatformId}`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        player: { crossplatformId, latestName: player.name, firstObservedAtUtc: player.observedAtUtc, lastObservedAtUtc: player.observedAtUtc, totalObservationCount: 1, retainedSnapshotCount: 1, compactedSnapshotCount: 0, hasGaps: false },
        gapSummary: { gapCount: 0, droppedObservationCount: 0 },
      }),
    })
  })
  await page.route('**/api/v1/audit?**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        entries: [{ sourceKind: 'playerAction', sourceId: '8f742dcfe65a454d8f919e164ace77d7', actorSubject: 'browser-owner', targetRef: 'player:7', action: 'Kick', occurredAtUtc: player.observedAtUtc, status: 'Succeeded', correlationId: null, hasDetails: true }],
        nextCursor: null,
        sourceGaps: [],
      }),
    })
  })
  await page.route('**/api/v1/players/7/kick', async (route) => {
    kickRequests++
    expect(route.request().method()).toBe('POST')
    expect(route.request().headers().authorization).toBe('Bearer 7dp_t_browser-smoke.secret')
    expect(route.request().postDataJSON()).toEqual({
      expectedPlatformIdentity: player.platformIdentity,
      reason: 'Browser journey confirmation',
      confirmed: true,
    })
    kicked = true
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        operationId: '8f742dcfe65a454d8f919e164ace77d7',
        status: 'succeeded',
        target: { entityId: player.entityId, name: player.name, platformIdentity: player.platformIdentity },
        requestedAtUtc: player.observedAtUtc,
        completedAtUtc: '2026-08-03T08:00:01Z',
      }),
    })
  })
  return () => kickRequests
}

test('Owner completes the mock J2 discovery, fixed-target kick, history, map, and audit journey', async ({ page }) => {
  await useStoredSession(page, 'Owner')
  await mockAdminApi(page)
  const kickRequests = await installOwnerJourneyApi(page)

  await gotoAdmin(page, '/players')
  await expect(page.getByText(player.name).filter({ visible: true }).first()).toBeVisible()
  await page.getByRole('button', { name: /查看.*详情|view.*details/iu }).filter({ visible: true }).first().click()
  await expect(page.getByRole('dialog')).toContainText(player.crossplatformIdentity.combinedId)

  const historyLink = page.getByRole('dialog').getByRole('link', { name: /查看历史|view player history/iu }).filter({ visible: true }).first()
  await historyLink.scrollIntoViewIfNeeded()
  await historyLink.focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(new RegExp(`/players/history/${crossplatformId}$`))
  await expect(page.getByText(player.name).filter({ visible: true }).first()).toBeVisible()

  await gotoAdmin(page, '/players')
  await page.getByRole('button', { name: /查看.*详情|view.*details/iu }).filter({ visible: true }).first().click()
  const mapLink = page.getByRole('dialog').getByRole('link', { name: /查看地图|view on map/iu }).filter({ visible: true }).first()
  await mapLink.scrollIntoViewIfNeeded()
  await mapLink.focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(new RegExp(`/players/map\\?player=${crossplatformId}$`))
  await expect(page.getByTestId('player-map-layout')).toBeVisible()

  await gotoAdmin(page, '/players')
  await page.getByRole('button', { name: /踢出.*Browser Journey Player|kick.*Browser Journey Player/iu }).filter({ visible: true }).first().click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText(player.platformIdentity.combinedId)
  await dialog.getByRole('textbox').fill('Browser journey confirmation')
  await page.getByTestId('confirm-kick-player').click()
  await expect.poll(kickRequests).toBe(1)
  await expect(page.getByRole('row').filter({ hasText: player.name })).toHaveCount(0)

  await gotoAdmin(page, '/system/audit')
  await expect(page).toHaveURL(url => url.pathname === '/system/audit')
  await expect(page.getByTestId('audit-entries-panel')).toBeVisible()
  const hasOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(hasOverflow).toBe(false)
})

for (const role of ['Admin', 'Viewer'] as const) {
  test(`${role} cannot access Owner player history, map, or audit actions`, async ({ page }) => {
    await useStoredSession(page, role)
    await mockAdminApi(page)
    await gotoAdmin(page, `/players/history/${crossplatformId}`)
    await expect(page).toHaveURL(url => url.pathname === '/forbidden')
    await gotoAdmin(page, '/players/map')
    await expect(page).toHaveURL(url => url.pathname === '/forbidden')
    await gotoAdmin(page, '/system/audit')
    await expect(page).toHaveURL(url => url.pathname === '/forbidden')
  })
}

test('unauthenticated users are redirected before any player field or action is rendered', async ({ page }) => {
  await mockAdminApi(page)
  await gotoAdmin(page, '/players')
  await expect(page).toHaveURL(url => url.pathname === '/login' && url.searchParams.get('redirect') === '/players')
  await expect(page.getByText(player.name)).toHaveCount(0)
})
