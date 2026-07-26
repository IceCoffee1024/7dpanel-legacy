import { describe, expect, it } from 'vitest'

import {
  gameResourceFiltersToRouteQuery,
  restoreGameResourceFilters,
  toGameResourceRequestQuery,
} from './gameResourceFilters'

describe('game resource URL filters', () => {
  it('restores the documented defaults', () => {
    expect(restoreGameResourceFilters({}, true)).toEqual({
      search: '',
      kind: 'all',
      includeHidden: false,
      page: 1,
      pageSize: 50,
    })
  })

  it('restores valid values and removes includeHidden for non-Owners', () => {
    const query = {
      search: '  steel  ',
      kind: 'block',
      includeHidden: 'true',
      page: '3',
      pageSize: '100',
    }

    expect(restoreGameResourceFilters(query, true)).toEqual({
      search: 'steel',
      kind: 'block',
      includeHidden: true,
      page: 3,
      pageSize: 100,
    })
    expect(restoreGameResourceFilters(query, false).includeHidden).toBe(false)
  })

  it('normalizes invalid URL values without allowing oversized searches', () => {
    expect(restoreGameResourceFilters({
      search: 'x'.repeat(101),
      kind: 'entity',
      includeHidden: '1',
      page: '0',
      pageSize: '500',
    }, true)).toEqual({
      search: '',
      kind: 'all',
      includeHidden: false,
      page: 1,
      pageSize: 50,
    })
  })

  it('writes only non-default filters to the URL and builds the language-aware request query', () => {
    const filters = Object.freeze({
      search: 'steel',
      kind: 'item' as const,
      includeHidden: true,
      page: 4,
      pageSize: 100,
    })

    expect(gameResourceFiltersToRouteQuery(filters)).toEqual({
      search: 'steel',
      kind: 'item',
      includeHidden: 'true',
      page: '4',
      pageSize: '100',
    })
    expect(toGameResourceRequestQuery(filters, 'en')).toEqual({
      search: 'steel',
      kind: 'item',
      includeHidden: true,
      language: 'en',
      page: 4,
      pageSize: 100,
    })
  })
})
