import { describe, expect, it, vi } from 'vitest'

import {
  createGameResourcesLoader,
  parseGameResourcePage,
} from './gameResources'

function validPage() {
  return {
    catalogVersion: 'catalog-7',
    gameVersion: 'v3.0.1-b4',
    observedAtUtc: '2026-07-26T08:00:00Z',
    total: 1,
    page: 1,
    pageSize: 50,
    warnings: ['game-resource-localization-partial'],
    items: [{
      resourceId: 'opaque_7',
      numericId: 7,
      internalName: 'resourceConcreteMix',
      localizedName: null,
      kind: 'item',
      visibility: 'public',
      maxStack: null,
      hasQuality: null,
      iconStatus: 'available',
      iconTintHex: null,
    }],
  }
}

describe('parseGameResourcePage', () => {
  it('strictly parses and freezes the page, warnings, items, and nullable fields', () => {
    const page = parseGameResourcePage(validPage())

    expect(page).toEqual(validPage())
    expect(Object.isFrozen(page)).toBe(true)
    expect(Object.isFrozen(page.warnings)).toBe(true)
    expect(Object.isFrozen(page.items)).toBe(true)
    expect(Object.isFrozen(page.items[0])).toBe(true)
  })

  it.each([
    ['kind', 'entity'],
    ['visibility', 'private'],
    ['iconStatus', 'ready'],
    ['resourceId', ''],
    ['numericId', 1.5],
    ['maxStack', 0],
    ['hasQuality', 'yes'],
    ['iconTintHex', 'aabbcc'],
    ['localizedName', ''],
  ])('rejects an invalid %s', (field, value) => {
    const input = validPage()
    Object.assign(input.items[0]!, { [field]: value })

    expect(() => parseGameResourcePage(input)).toThrow('Invalid game resource page response')
  })

  it.each([
    ['observedAtUtc', '2026-07-26'],
    ['total', -1],
    ['page', 0],
    ['pageSize', 101],
    ['gameVersion', ''],
    ['warnings', ['']],
  ])('rejects invalid root metadata %s', (field, value) => {
    const input = validPage()
    Object.assign(input, { [field]: value })

    expect(() => parseGameResourcePage(input)).toThrow('Invalid game resource page response')
  })

  it.each(['iconName', 'path', 'iconPath', 'relativePath', 'absolutePath'])('rejects the known private field %s', (field) => {
    const input = validPage()
    Object.assign(input.items[0]!, { [field]: 'private-value' })

    expect(() => parseGameResourcePage(input)).toThrow('Invalid game resource page response')
  })

  it('rejects unknown root and item fields instead of silently widening the contract', () => {
    expect(() => parseGameResourcePage({ ...validPage(), nextPage: 2 }))
      .toThrow('Invalid game resource page response')

    const input = validPage()
    Object.assign(input.items[0]!, { futureField: true })
    expect(() => parseGameResourcePage(input)).toThrow('Invalid game resource page response')
  })
})

describe('createGameResourcesLoader', () => {
  it('passes the typed query and signal to transport and parses its unknown response', async () => {
    const request = vi.fn().mockResolvedValue(validPage())
    const load = createGameResourcesLoader(request)
    const signal = new AbortController().signal
    const query = {
      search: 'concrete',
      kind: 'item' as const,
      includeHidden: false,
      language: 'zh-CN' as const,
      page: 2,
      pageSize: 50,
    }

    await expect(load(query, signal)).resolves.toEqual(validPage())
    expect(request).toHaveBeenCalledOnce()
    expect(request).toHaveBeenCalledWith(query, signal)
  })
})
