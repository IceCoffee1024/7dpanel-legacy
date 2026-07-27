import type { App } from 'vue'
import type * as dailyRewardPolicyApi from '../api/dailyRewardPolicy'

import { flushPromises } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useDailyRewardPolicy } from './useDailyRewardPolicy'

const policy = {
  ruleId: 'daily',
  rewardPackageId: 'starter-pack',
  enabled: true,
  createdAtUtc: '2026-07-27T01:02:03Z',
  updatedAtUtc: '2026-07-27T01:03:04Z',
  rowVersion: 3n,
}

const apps: App[] = []

afterEach(() => {
  while (apps.length > 0)
    apps.pop()!.unmount()
})

function mountComposable(options: {
  fetchPolicy?: typeof dailyRewardPolicyApi.fetchDailyRewardPolicy
  savePolicy?: typeof dailyRewardPolicyApi.saveDailyRewardPolicy
} = {}) {
  let result!: ReturnType<typeof useDailyRewardPolicy>
  const fetchPolicy = options.fetchPolicy ?? vi.fn().mockResolvedValue(policy)
  const savePolicy = options.savePolicy ?? vi.fn().mockResolvedValue(policy)
  const app = createApp({
    setup() {
      result = useDailyRewardPolicy({
        auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
        fetchPolicy,
        savePolicy,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  apps.push(app)
  return { result, fetchPolicy, savePolicy }
}

it('retains an edited draft and marks it stale when the server reports a row-version conflict', async () => {
  const savePolicy = vi.fn().mockRejectedValue(new HttpError('http', 'Conflict', {
    status: 409,
    problemCode: 'daily_reward_policy_concurrency_conflict',
  }))
  const mounted = mountComposable({ savePolicy })
  await flushPromises()

  const draft = { rewardPackageId: 'weekend-pack', enabled: false, expectedRowVersion: 3n }
  mounted.result.updateDraft(draft)

  await expect(mounted.result.save()).resolves.toBe(false)

  expect(mounted.result.draft.value).toEqual(draft)
  expect(mounted.result.state.value).toBe('stale')
  expect(mounted.result.saveError.value).toEqual({ code: 'conflict' })
  expect(savePolicy).toHaveBeenCalledWith('Bearer token', draft, expect.any(AbortSignal))
})

it('creates a missing policy with a null expected row version only after the server confirms it', async () => {
  const savePolicy = vi.fn().mockResolvedValue({ ...policy, rewardPackageId: 'weekend-pack', rowVersion: 0n })
  const mounted = mountComposable({
    fetchPolicy: vi.fn().mockRejectedValue(new HttpError('http', 'Not found', {
      status: 404,
      problemCode: 'daily_reward_policy_not_found',
    })),
    savePolicy,
  })
  await flushPromises()

  expect(mounted.result.state.value).toBe('not-configured')
  expect(mounted.result.draft.value).toEqual({ rewardPackageId: '', enabled: true, expectedRowVersion: null })

  mounted.result.updateDraft({ rewardPackageId: 'weekend-pack', enabled: true, expectedRowVersion: null })

  await expect(mounted.result.save()).resolves.toBe(true)

  expect(savePolicy).toHaveBeenCalledWith('Bearer token', {
    rewardPackageId: 'weekend-pack',
    enabled: true,
    expectedRowVersion: null,
  }, expect.any(AbortSignal))
  expect(mounted.result.policy.value?.rowVersion).toBe(0n)
})
