import type { AreaInvestigationQuery, AreaInvestigationResponse } from './useAreaInvestigation'

import { describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { fetchAreaInvestigation, parseAreaInvestigationResponse } from './areaInvestigationAdapter'
import {
  areaInvestigationPath,
  restoreAreaInvestigationUrlState,
} from './areaInvestigationProjection'
import { createAreaInvestigationController } from './useAreaInvestigation'

vi.mock('../../../shared/api/http', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../shared/api/http')>()
  return { ...actual, requestJson: vi.fn() }
})

const rectangleQuery: AreaInvestigationQuery = {
  geometry: { kind: 'rectangle', minimumX: -10, minimumZ: -20, maximumX: 30, maximumZ: 40 },
  fromUtc: '2026-07-01T00:00:00Z',
  toUtc: '2026-07-26T00:00:00Z',
  limit: 250,
}

function wireResponse(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    hits: [{
      crossplatformId: 'EOS_ada',
      displayName: 'Ada',
      firstHitUtc: '2026-07-03T04:05:06Z',
      lastHitUtc: '2026-07-05T06:07:08Z',
      hitObservationCount: 3,
      lastPosition: { x: 12, y: 34, z: -56 },
    }],
    candidateObservationCount: 7,
    matchingObservationCount: 3,
    candidateObservationLimitReached: false,
    playerResultLimitReached: false,
    ...overrides,
  }
}

describe('area investigation URL and transport', () => {
  it('serializes exactly one rectangle or circle with a bounded UTC range and result limit', () => {
    expect(areaInvestigationPath(rectangleQuery)).toBe(
      '/api/v1/map/players/area?shape=rectangle&minimumX=-10&minimumZ=-20&maximumX=30&maximumZ=40&fromUtc=2026-07-01T00%3A00%3A00Z&toUtc=2026-07-26T00%3A00%3A00Z&limit=250',
    )
    expect(areaInvestigationPath({
      ...rectangleQuery,
      geometry: { kind: 'circle', centerX: 1, centerZ: 2, radius: 50 },
    })).toContain('shape=circle&centerX=1&centerZ=2&radius=50')

    expect(() => areaInvestigationPath({ ...rectangleQuery, toUtc: '2026-08-01T00:00:01Z' })).toThrow('Invalid area investigation query')
    expect(() => areaInvestigationPath({ ...rectangleQuery, limit: 1001 })).toThrow('Invalid area investigation query')
  })

  it('restores only complete serializable URL state and rejects mixed or invalid geometry', () => {
    const restored = restoreAreaInvestigationUrlState(new URLSearchParams(
      'areaShape=rectangle&areaMinimumX=-10&areaMinimumZ=-20&areaMaximumX=30&areaMaximumZ=40&areaFrom=2026-07-01T00%3A00%3A00Z&areaTo=2026-07-26T00%3A00%3A00Z',
    ))
    expect(restored).toEqual({
      geometry: rectangleQuery.geometry,
      fromUtc: rectangleQuery.fromUtc,
      toUtc: rectangleQuery.toUtc,
    })

    const mixed = new URLSearchParams('areaShape=rectangle&areaMinimumX=0&areaMinimumZ=0&areaMaximumX=1&areaMaximumZ=1&areaCenterX=0&areaCenterZ=0&areaRadius=1')
    expect(restoreAreaInvestigationUrlState(mixed).geometry).toBeNull()
    expect(restoreAreaInvestigationUrlState(new URLSearchParams('areaShape=circle&areaCenterX=0&areaCenterZ=0&areaRadius=0')).geometry).toBeNull()
  })

  it('strictly parses grouped matching observations and exposes canonical combined ids', () => {
    const parsed = parseAreaInvestigationResponse(wireResponse())
    expect(parsed).toEqual({
      players: [{
        combinedId: 'EOS_ada',
        displayName: 'Ada',
        firstMatchingObservation: { observedAtUtc: '2026-07-03T04:05:06Z' },
        lastMatchingObservation: {
          observedAtUtc: '2026-07-05T06:07:08Z',
          position: { x: 12, y: 34, z: -56 },
        },
        matchingObservationCount: 3,
      }],
      candidateObservationCount: 7,
      matchingObservationCount: 3,
      truncated: false,
      truncation: { candidateObservations: false, playerResults: false },
    })
    expect(Object.isFrozen(parsed.players[0]?.lastMatchingObservation.position)).toBe(true)
  })

  it.each([
    ['extra dangerous data', wireResponse({ deletePlayers: true })],
    ['continuous-presence data', wireResponse({ hits: [{ ...wireResponse().hits[0], isCurrentlyInside: true }] })],
    ['non-UTC first match', wireResponse({ hits: [{ ...wireResponse().hits[0], firstHitUtc: '2026-07-03T12:05:06+08:00' }] })],
    ['reversed match range', wireResponse({ hits: [{ ...wireResponse().hits[0], firstHitUtc: '2026-07-06T00:00:00Z' }] })],
    ['inconsistent counts', wireResponse({ matchingObservationCount: 2 })],
    ['unbounded player results', wireResponse({ hits: Array.from({ length: 1001 }, (_, index) => ({ ...wireResponse().hits[0], crossplatformId: `EOS_${index}` })) })],
  ])('rejects %s', (_name, value) => {
    expect(() => parseAreaInvestigationResponse(value)).toThrow('Invalid area investigation response')
  })

  it('uses the Owner endpoint with Authorization and AbortSignal then strictly parses it', async () => {
    vi.mocked(requestJson).mockResolvedValueOnce(wireResponse())
    const signal = new AbortController().signal

    const result = await fetchAreaInvestigation('Bearer owner', rectangleQuery, signal)

    expect(requestJson).toHaveBeenCalledWith(areaInvestigationPath(rectangleQuery), {
      headers: { Authorization: 'Bearer owner' },
      signal,
    })
    expect(result.players[0]?.combinedId).toBe('EOS_ada')
  })
})

describe('area investigation controller', () => {
  it('keeps rectangle and circle mutually exclusive and syncs future Draw/Modify state to the URL', () => {
    const replaceQuery = vi.fn()
    const controller = createAreaInvestigationController({
      authorizationHeader: () => 'Bearer owner',
      replaceQuery,
    })

    controller.setRectangle(-10, -20, 30, 40)
    expect(controller.geometry.value).toEqual(rectangleQuery.geometry)
    controller.setCircle(1, 2, 50)
    expect(controller.geometry.value).toEqual({ kind: 'circle', centerX: 1, centerZ: 2, radius: 50 })
    expect(controller.geometry.value).not.toHaveProperty('minimumX')

    controller.setTimeRange(rectangleQuery.fromUtc, rectangleQuery.toUtc)
    const synced = replaceQuery.mock.calls[replaceQuery.mock.calls.length - 1]?.[0] as URLSearchParams
    expect(synced.get('areaShape')).toBe('circle')
    expect(synced.get('areaRadius')).toBe('50')
    expect(synced.get('areaMinimumX')).toBeNull()
    expect(synced.get('areaFrom')).toBe(rectangleQuery.fromUtc)
  })

  it('cancels obsolete requests, ignores stale responses and selects only returned players', async () => {
    const pending: Array<{
      signal: AbortSignal
      resolve: (response: AreaInvestigationResponse) => void
    }> = []
    const request = vi.fn((_authorization: string, _query: AreaInvestigationQuery, signal: AbortSignal) =>
      new Promise<AreaInvestigationResponse>(resolve => pending.push({ signal, resolve })))
    const controller = createAreaInvestigationController({ authorizationHeader: () => 'Bearer owner', request })
    controller.setRectangle(-10, -20, 30, 40)
    controller.setTimeRange(rectangleQuery.fromUtc, rectangleQuery.toUtc)

    const obsolete = controller.search()
    const current = controller.search()
    expect(pending[0]?.signal.aborted).toBe(true)

    pending[0]?.resolve(parseAreaInvestigationResponse(wireResponse({ hits: [{ ...wireResponse().hits[0], crossplatformId: 'EOS_stale' }] })))
    await obsolete
    expect(controller.players.value).toEqual([])

    pending[1]?.resolve(parseAreaInvestigationResponse(wireResponse()))
    await current
    expect(controller.state.value).toBe('ready')
    expect(controller.players.value[0]?.combinedId).toBe('EOS_ada')
    controller.selectResult('EOS_missing')
    expect(controller.selectedCombinedId.value).toBeNull()
    controller.selectResult('EOS_ada')
    expect(controller.selectedCombinedId.value).toBe('EOS_ada')
  })

  it('exposes truncation, clear and cancel without implying current location or continuous presence', async () => {
    let resolvePending: ((response: AreaInvestigationResponse) => void) | undefined
    const request = vi.fn(() => new Promise<AreaInvestigationResponse>((resolve) => {
      resolvePending = resolve
    }))
    const controller = createAreaInvestigationController({ authorizationHeader: () => 'Bearer owner', request })
    controller.setCircle(1, 2, 50)
    controller.setTimeRange(rectangleQuery.fromUtc, rectangleQuery.toUtc)

    const pending = controller.search()
    controller.cancel()
    expect(controller.state.value).toBe('idle')
    resolvePending?.(parseAreaInvestigationResponse(wireResponse()))
    await pending
    expect(controller.players.value).toEqual([])

    const second = controller.search()
    resolvePending?.(parseAreaInvestigationResponse(wireResponse({ playerResultLimitReached: true })))
    await second
    expect(controller.state.value).toBe('truncated')
    expect(controller.truncated.value).toBe(true)

    controller.clear()
    expect(controller.geometry.value).toBeNull()
    expect(controller.players.value).toEqual([])
    expect(controller.selectedCombinedId.value).toBeNull()
    expect(controller.state.value).toBe('idle')
  })
})
