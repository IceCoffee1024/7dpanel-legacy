import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import BackupsPage from './backups.vue'

const useBackupsMock = vi.hoisted(() => vi.fn())
const useBackupPoliciesMock = vi.hoisted(() => vi.fn())

vi.mock('../../features/backups/model/useBackups', () => ({ useBackups: useBackupsMock }))
vi.mock('../../features/backups/model/useBackupPolicies', () => ({ useBackupPolicies: useBackupPoliciesMock }))

function createRouterFor(location: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/operations/backups', component: { template: '<main />' } }],
  })
  return router.push(location).then(() => router)
}

function mountPage(router: ReturnType<typeof createRouter>) {
  return mount(BackupsPage, {
    global: {
      plugins: [router],
      stubs: { BackupsView: { template: '<section data-testid="backups-view" />' } },
    },
  })
}

describe('backups page operation recovery', () => {
  beforeEach(() => {
    useBackupsMock.mockReset()
    useBackupPoliciesMock.mockReset()
    useBackupPoliciesMock.mockReturnValue({})
  })

  it('resumes the persisted operation ID from a deep link without restoring again', async () => {
    const resume = vi.fn().mockResolvedValue(undefined)
    useBackupsMock.mockReturnValue({ activeJob: shallowRef(null), resume })
    const router = await createRouterFor('/operations/backups?operationId=restore-42')

    mountPage(router)
    await flushPromises()

    expect(resume).toHaveBeenCalledOnce()
    expect(resume).toHaveBeenCalledWith('restore-42')
  })

  it('records an accepted operation ID in the URL for reload recovery', async () => {
    const activeJob = shallowRef<{ id: string } | null>(null)
    useBackupsMock.mockReturnValue({ activeJob, resume: vi.fn().mockResolvedValue(undefined) })
    const router = await createRouterFor('/operations/backups')

    mountPage(router)
    activeJob.value = { id: 'restore-42' }
    await flushPromises()

    expect(router.currentRoute.value.query.operationId).toBe('restore-42')
  })
})
