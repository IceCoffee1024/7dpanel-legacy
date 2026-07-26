import type { ServerConfigurationController } from './useServerConfiguration'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useServerConfiguration } from './useServerConfiguration'

const snapshot = Object.freeze({
  version: 'a'.repeat(64),
  readAtUtc: '2026-07-26T08:00:00Z',
  fields: Object.freeze([]),
})

function mountController(options: Parameters<typeof useServerConfiguration>[0]) {
  let controller!: ServerConfigurationController
  const wrapper = mount(defineComponent({
    setup() {
      controller = useServerConfiguration(options)
      return () => null
    },
  }))
  return { controller, wrapper }
}

describe('useServerConfiguration', () => {
  it('preserves the last snapshot and enters stale after refresh failure', async () => {
    const fetchConfiguration = vi.fn()
      .mockResolvedValueOnce(snapshot)
      .mockRejectedValueOnce(new HttpError('network', 'private detail'))
    const { controller, wrapper } = mountController({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchConfiguration,
    })
    await flushPromises()

    await controller.refresh()

    expect(controller.state.value).toBe('stale')
    expect(controller.snapshot.value?.version).toBe(snapshot.version)
    wrapper.unmount()
  })

  it('maps a version conflict without changing the current snapshot', async () => {
    const updateField = vi.fn().mockRejectedValue(new HttpError('http', 'conflict', {
      status: 409,
      problemCode: 'configuration_version_conflict',
    }))
    const { controller, wrapper } = mountController({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchConfiguration: vi.fn().mockResolvedValue(snapshot),
      updateField,
    })
    await flushPromises()

    await expect(controller.update('ServerName', 'new')).resolves.toBe(false)

    expect(controller.feedback.value).toEqual({ code: 'conflict' })
    expect(controller.snapshot.value).toBe(snapshot)
    wrapper.unmount()
  })
})
