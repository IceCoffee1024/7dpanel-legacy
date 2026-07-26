import { describe, expect, it, vi } from 'vitest'

import { generatedGameResourcesLoader } from './generatedGameResources'

const gameResourcesGetMock = vi.hoisted(() => vi.fn())

vi.mock('../../../shared/api/generated/sdk.gen', () => ({
  gameResourcesGet: gameResourcesGetMock,
}))

describe('generatedGameResourcesLoader', () => {
  it('forwards the feature query and cancellation signal to the generated SDK', async () => {
    const response = {
      catalogVersion: 'catalog-1',
      gameVersion: null,
      observedAtUtc: '2026-07-26T00:00:00Z',
      total: 0,
      page: 1,
      pageSize: 50,
      warnings: [],
      items: [],
    }
    gameResourcesGetMock.mockResolvedValue(response)
    const signal = new AbortController().signal
    const query = {
      search: 'steel',
      kind: 'item' as const,
      includeHidden: true,
      language: 'en' as const,
      page: 1,
      pageSize: 50,
    }

    await expect(generatedGameResourcesLoader(query, signal)).resolves.toEqual(response)
    expect(gameResourcesGetMock).toHaveBeenCalledWith({ query, signal })
  })
})
