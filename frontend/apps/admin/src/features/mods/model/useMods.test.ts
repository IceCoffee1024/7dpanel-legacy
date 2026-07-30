import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useMods } from './useMods'

const mod = {
  directoryId: 'Example',
  name: 'Example',
  displayName: 'Example',
  author: 'Author',
  version: '1',
  website: null,
  description: null,
  isLoadedNow: true,
  isEnabledNextStart: false,
  isProtected: false,
}

describe('useMods', () => {
  function mountMods(options: Parameters<typeof useMods>[0]) {
    let controller!: ReturnType<typeof useMods>
    const Host = defineComponent({
      setup() {
        controller = useMods(options)
        return () => null
      },
    })
    return { controller: () => controller, wrapper: mount(Host) }
  }

  it('loads mods and preserves separate runtime and next-start state', async () => {
    const mounted = mountMods({
      auth: { authorizationHeader: 'Bearer owner', role: 'Owner', expireSession: vi.fn() },
      fetchMods: vi.fn().mockResolvedValue([mod]),
    })
    await mounted.controller().refresh()
    expect(mounted.controller().mods.value).toEqual([mod])
    expect(mounted.controller().mods.value[0]?.isLoadedNow).toBe(true)
    expect(mounted.controller().mods.value[0]?.isEnabledNextStart).toBe(false)
    mounted.wrapper.unmount()
  })

  it('allows only Owner mutation and refreshes after a conflict', async () => {
    const fetchMods = vi.fn().mockResolvedValue([mod])
    const change = vi.fn().mockRejectedValue(new HttpError('http', 'conflict', { status: 409 }))
    const mounted = mountMods({
      auth: { authorizationHeader: 'Bearer owner', role: 'Owner', expireSession: vi.fn() },
      fetchMods,
      setModEnabled: change,
    })
    await mounted.controller().refresh()
    await expect(mounted.controller().changeNextStart(mod, true)).resolves.toBe(false)
    expect(fetchMods).toHaveBeenCalledTimes(2)
    expect(mounted.controller().feedback.value).toEqual({ code: 'conflict' })
    mounted.wrapper.unmount()
  })

  it('keeps Admin read-only without sending a mutation', async () => {
    const change = vi.fn()
    const mounted = mountMods({
      auth: { authorizationHeader: 'Bearer admin', role: 'Admin', expireSession: vi.fn() },
      fetchMods: vi.fn().mockResolvedValue([mod]),
      setModEnabled: change,
    })
    await expect(mounted.controller().changeNextStart(mod, true)).resolves.toBe(false)
    expect(change).not.toHaveBeenCalled()
    expect(mounted.controller().canMutate.value).toBe(false)
    mounted.wrapper.unmount()
  })
})
