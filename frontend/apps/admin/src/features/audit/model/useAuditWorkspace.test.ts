import type { App } from 'vue'

import type { AuditPage, LoadAuditEntries } from '../api/audit'
import { flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { createApp } from 'vue'
import { createEmptyAuditFilters } from './audit'
import { useAuditWorkspace } from './useAuditWorkspace'

function page(id: string, nextCursor: string | null = null): AuditPage {
  return Object.freeze({
    entries: Object.freeze([Object.freeze({
      sourceKind: 'playerAction' as const,
      sourceId: id,
      actorSubject: 'owner',
      targetRef: null,
      action: 'kick',
      occurredAtUtc: '2026-07-26T08:00:00Z',
      status: 'Succeeded',
      correlationId: null,
      hasDetails: false,
    })]),
    nextCursor,
    sourceGaps: Object.freeze([]),
  })
}

function mountComposable(load: LoadAuditEntries) {
  let result!: ReturnType<typeof useAuditWorkspace>
  const app = createApp({
    setup() {
      result = useAuditWorkspace({
        auth: { authorizationHeader: 'Bearer owner', expireSession: vi.fn() },
        load,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useAuditWorkspace', () => {
  const apps: App[] = []

  afterEach(() => {
    while (apps.length > 0)
      apps.pop()!.unmount()
  })

  it('keeps its own cursor history and clears it when filters change', async () => {
    const load = vi.fn<LoadAuditEntries>()
      .mockResolvedValueOnce(page('first', 'cursor-1'))
      .mockResolvedValueOnce(page('second'))
      .mockResolvedValueOnce(page('filtered'))
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.goToPage(2)
    expect(load.mock.calls[1]?.[2]).toBe('cursor-1')

    await mounted.result.applyFilters({ ...createEmptyAuditFilters(), actor: 'next-owner' })
    expect(mounted.result.pageNumber.value).toBe(1)
    expect(load.mock.calls[2]?.[1].actor).toBe('next-owner')
    expect(load.mock.calls[2]?.[2]).toBeNull()
  })

  it('retains the last successful page as stale after a refresh failure', async () => {
    const load = vi.fn<LoadAuditEntries>()
      .mockResolvedValueOnce(page('retained'))
      .mockRejectedValueOnce(new Error('offline'))
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.refresh()

    expect(mounted.result.state.value).toBe('stale')
    expect(mounted.result.entries.value[0]?.sourceId).toBe('retained')
  })
})
