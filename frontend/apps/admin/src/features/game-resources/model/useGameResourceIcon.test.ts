import type { App, ShallowRef } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, nextTick, shallowRef } from 'vue'

import { useGameResourceIcon } from './useGameResourceIcon'

function pngResponse(): Response {
  return new Response(new Blob(['png'], { type: 'image/png' }), {
    headers: { 'Content-Type': 'image/png' },
    status: 200,
  })
}

function mountIcon(options: {
  resourceId?: ShallowRef<string>
  iconStatus?: ShallowRef<'available' | 'missing' | 'invalid'>
  authorizationHeader?: ShallowRef<string | null>
  fetch?: typeof fetch
  createObjectURL?: (blob: Blob) => string
  revokeObjectURL?: (url: string) => void
}) {
  const resourceId = options.resourceId ?? shallowRef('resource/one?')
  const iconStatus = options.iconStatus ?? shallowRef<'available' | 'missing' | 'invalid'>('available')
  const authorizationHeader = options.authorizationHeader ?? shallowRef<string | null>('Bearer secret-token')
  const target = shallowRef<Element | null>(document.createElement('span'))
  let show!: () => void
  const disconnect = vi.fn()
  let result!: ReturnType<typeof useGameResourceIcon>
  const app = createApp({
    setup() {
      result = useGameResourceIcon({
        authorizationHeader,
        createObjectURL: options.createObjectURL,
        createObserver: (onVisible) => {
          show = onVisible
          return { disconnect, observe: vi.fn() }
        },
        fetch: options.fetch,
        iconStatus,
        resourceId,
        revokeObjectURL: options.revokeObjectURL,
        target,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, authorizationHeader, disconnect, iconStatus, resourceId, result, show: () => show() }
}

describe('useGameResourceIcon', () => {
  const apps: App[] = []

  afterEach(() => {
    for (const app of apps.splice(0))
      app.unmount()
  })

  it('waits for visibility and fetches an encoded resourceId with Header Bearer only', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(pngResponse())
    const createObjectURL = vi.fn().mockReturnValue('blob:resource-one')
    const mounted = mountIcon({ fetch: fetchImpl, createObjectURL })
    apps.push(mounted.app)

    expect(fetchImpl).not.toHaveBeenCalled()
    mounted.show()
    await flushPromises()

    expect(fetchImpl).toHaveBeenCalledWith(
      '/api/v1/game-resources/resource%2Fone%3F/icon',
      expect.objectContaining({
        credentials: 'omit',
        headers: { Authorization: 'Bearer secret-token' },
        signal: expect.any(AbortSignal),
      }),
    )
    expect(fetchImpl.mock.calls[0]?.[0]).not.toContain('secret-token')
    expect(mounted.result.src.value).toBe('blob:resource-one')
    expect(mounted.result.failed.value).toBe(false)
  })

  it.each([
    new Response('no', { status: 404 }),
    new Response('no', { status: 403 }),
    new Response('no', { status: 503 }),
    new Response(new Blob(['webp']), { headers: { 'Content-Type': 'image/webp' } }),
  ])('maps HTTP and content-type failures to the same placeholder state', async (response) => {
    const mounted = mountIcon({ fetch: vi.fn<typeof fetch>().mockResolvedValue(response) })
    apps.push(mounted.app)

    mounted.show()
    await flushPromises()

    expect(mounted.result.src.value).toBeNull()
    expect(mounted.result.failed.value).toBe(true)
  })

  it('revokes Blob URLs, aborts replaced resources, and ignores late responses', async () => {
    let resolveFirst!: (response: Response) => void
    const first = new Promise<Response>((resolve) => {
      resolveFirst = resolve
    })
    const fetchImpl = vi.fn<typeof fetch>()
      .mockImplementationOnce(() => first)
      .mockResolvedValueOnce(pngResponse())
    const createObjectURL = vi.fn()
      .mockReturnValueOnce('blob:new-resource')
      .mockReturnValueOnce('blob:late-resource')
    const revokeObjectURL = vi.fn()
    const mounted = mountIcon({ fetch: fetchImpl, createObjectURL, revokeObjectURL })
    apps.push(mounted.app)

    mounted.show()
    await nextTick()
    const firstSignal = (fetchImpl.mock.calls[0]?.[1] as RequestInit).signal as AbortSignal
    mounted.resourceId.value = 'resource-two'
    await flushPromises()

    expect(firstSignal.aborted).toBe(true)
    expect(mounted.result.src.value).toBe('blob:new-resource')

    resolveFirst(pngResponse())
    await flushPromises()
    expect(mounted.result.src.value).toBe('blob:new-resource')
    expect(createObjectURL).toHaveBeenCalledTimes(1)

    mounted.app.unmount()
    apps.splice(0)
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:new-resource')
    expect(mounted.disconnect).toHaveBeenCalledOnce()
  })

  it('does not request missing icons and clears an authenticated Blob when the session disappears', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(pngResponse())
    const revokeObjectURL = vi.fn()
    const mounted = mountIcon({
      fetch: fetchImpl,
      createObjectURL: () => 'blob:authenticated',
      revokeObjectURL,
    })
    apps.push(mounted.app)
    mounted.show()
    await flushPromises()

    mounted.authorizationHeader.value = null
    await nextTick()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:authenticated')
    expect(mounted.result.failed.value).toBe(true)

    mounted.iconStatus.value = 'missing'
    mounted.authorizationHeader.value = 'Bearer renewed'
    await flushPromises()
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })
})
