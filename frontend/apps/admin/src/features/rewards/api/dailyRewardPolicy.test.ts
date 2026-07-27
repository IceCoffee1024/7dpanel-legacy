import { expect, it } from 'vitest'

import {
  parseDailyRewardPolicy,
  toDailyRewardPolicyUpdateRequest,
} from './dailyRewardPolicy'

const response = {
  ruleId: 'daily',
  rewardPackageId: 'starter-pack',
  enabled: true,
  createdAtUtc: '2026-07-27T01:02:03.1234567Z',
  updatedAtUtc: '2026-07-27T01:03:04.1234567+00:00',
  rowVersion: '9007199254740993',
}

it('parses only the complete UTC daily reward policy contract', () => {
  expect(parseDailyRewardPolicy(response)).toEqual({
    ...response,
    rowVersion: 9007199254740993n,
  })

  expect(() => parseDailyRewardPolicy({ ...response, unexpected: true })).toThrow('Invalid daily reward policy response')
  expect(() => parseDailyRewardPolicy({ ...response, updatedAtUtc: '2026-07-27T09:03:04+08:00' })).toThrow('Invalid daily reward policy response')
  expect(() => parseDailyRewardPolicy({ ...response, rowVersion: -1 })).toThrow('Invalid daily reward policy response')
})

it('serializes create and update row versions without losing bigint precision', () => {
  expect(toDailyRewardPolicyUpdateRequest({
    rewardPackageId: ' starter-pack ',
    enabled: true,
    expectedRowVersion: null,
  })).toEqual({
    rewardPackageId: 'starter-pack',
    enabled: true,
    expectedRowVersion: null,
  })

  expect(toDailyRewardPolicyUpdateRequest({
    rewardPackageId: 'starter-pack',
    enabled: false,
    expectedRowVersion: 9007199254740993n,
  })).toEqual({
    rewardPackageId: 'starter-pack',
    enabled: false,
    expectedRowVersion: '9007199254740993',
  })
})
