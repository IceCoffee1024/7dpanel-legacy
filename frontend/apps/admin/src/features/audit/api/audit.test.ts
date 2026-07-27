import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listAuditEntriesQuery } from '../../../shared/api/generated/@pinia/colada.gen'
import { createEmptyAuditFilters } from '../model/audit'
import { loadAuditEntries, parseAuditPage } from './audit'

vi.mock('../../../shared/api/generated/@pinia/colada.gen', () => ({
  listAuditEntriesQuery: vi.fn(),
}))

function validPage() {
  return {
    entries: [{
      sourceKind: 'playerAction',
      sourceId: 'operation-1',
      actorSubject: 'owner',
      targetRef: 'EOS_target',
      action: 'kick',
      occurredAtUtc: '2026-07-26T08:00:00Z',
      status: 'Succeeded',
      correlationId: null,
      hasDetails: false,
    }],
    nextCursor: 'opaque-audit-cursor',
    sourceGaps: [{
      sourceKind: 'consoleCommand',
      startedAtUtc: '2026-07-26T07:59:00Z',
      endedAtUtc: null,
      affectedCount: 2,
      reason: 'queue-full',
    }],
  }
}

describe('audit generated transport', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses listAuditEntries with the generated query shape and parses the result', async () => {
    const query = vi.fn().mockResolvedValue(validPage())
    vi.mocked(listAuditEntriesQuery).mockReturnValue({ query } as never)
    const signal = new AbortController().signal

    await expect(loadAuditEntries(
      'Bearer owner',
      { ...createEmptyAuditFilters(), actor: 'owner' },
      'cursor-1',
      50,
      signal,
    )).resolves.toEqual(validPage())

    expect(listAuditEntriesQuery).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      query: { actor: 'owner', cursor: 'cursor-1', limit: '50' },
    })
    expect(query).toHaveBeenCalledWith(expect.objectContaining({ signal }))
  })

  it.each([
    ['missing required entry field', () => {
      const page = validPage()
      delete (page.entries[0] as Partial<typeof page.entries[number]>).sourceId
      return page
    }],
    ['unknown source kind', () => {
      const page = validPage()
      page.entries[0]!.sourceKind = 'other'
      return page
    }],
    ['non-UTC timestamp', () => {
      const page = validPage()
      page.entries[0]!.occurredAtUtc = '2026-07-26T16:00:00+08:00'
      return page
    }],
    ['empty cursor', () => ({ ...validPage(), nextCursor: '' })],
  ])('rejects %s', (_label, input) => {
    expect(() => parseAuditPage(input())).toThrow('Invalid audit page response')
  })
})
