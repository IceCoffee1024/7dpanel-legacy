import type { VoteConfiguration, VoteConfigurationInput, VoteRound, VoteSettlement } from './community.types'

import { requestJson } from '../../../shared/api/http'

import {
  bool,
  collection,
  enumValue,
  headers,
  integer,
  invalid,
  long,
  nullableText,
  nullableUtc,
  queryPath,
  record,
  text,
  utc,
  wireInteger,
} from './community.protocol'
import { VOTE_KINDS, VOTE_ROUND_STATES } from './community.types'

const voteConfigurationKeys = ['configurationId', 'kind', 'enabled', 'durationMs', 'thresholdPercent', 'minimumParticipants', 'initiatorMinimumOnlineMs', 'participantMinimumOnlineMs', 'initiatorCooldownMs', 'targetCooldownMs', 'globalCooldownMs', 'mutualExclusionScope', 'allowVoteChange', 'updatedAtUtc', 'rowVersion'] as const
const voteRoundKeys = ['roundId', 'configurationId', 'kind', 'state', 'initiatorCrossplatformId', 'targetCrossplatformId', 'scopeKey', 'eligibleCount', 'thresholdPercent', 'minimumParticipants', 'allowVoteChange', 'actionJobId', 'actionOperationId', 'correlationId', 'openedAtUtc', 'expiresAtUtc', 'settledAtUtc', 'actionCompletedAtUtc', 'rowVersion'] as const
const voteSettlementKeys = ['status', 'round', 'participantCount', 'yesCount', 'noCount', 'wasSettled'] as const

export function parseVoteConfiguration(value: unknown): VoteConfiguration {
  const source = record(value, voteConfigurationKeys)
  const thresholdPercent = integer(source.thresholdPercent, 1)
  if (thresholdPercent > 100)
    return invalid()
  return Object.freeze({
    configurationId: text(source.configurationId),
    kind: enumValue(source.kind, VOTE_KINDS),
    enabled: bool(source.enabled),
    durationMs: long(source.durationMs, 1n),
    thresholdPercent,
    minimumParticipants: integer(source.minimumParticipants, 1),
    initiatorMinimumOnlineMs: long(source.initiatorMinimumOnlineMs),
    participantMinimumOnlineMs: long(source.participantMinimumOnlineMs),
    initiatorCooldownMs: long(source.initiatorCooldownMs),
    targetCooldownMs: long(source.targetCooldownMs),
    globalCooldownMs: long(source.globalCooldownMs),
    mutualExclusionScope: text(source.mutualExclusionScope),
    allowVoteChange: bool(source.allowVoteChange),
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: long(source.rowVersion),
  })
}

export function parseVoteRound(value: unknown): VoteRound {
  const source = record(value, voteRoundKeys)
  const openedAtUtc = utc(source.openedAtUtc)
  const expiresAtUtc = utc(source.expiresAtUtc)
  const settledAtUtc = nullableUtc(source.settledAtUtc)
  const actionCompletedAtUtc = nullableUtc(source.actionCompletedAtUtc)
  if (Date.parse(expiresAtUtc) <= Date.parse(openedAtUtc))
    return invalid()
  if (settledAtUtc !== null && Date.parse(settledAtUtc) < Date.parse(openedAtUtc))
    return invalid()
  if (actionCompletedAtUtc !== null && Date.parse(actionCompletedAtUtc) < Date.parse(openedAtUtc))
    return invalid()
  const thresholdPercent = integer(source.thresholdPercent, 1)
  if (thresholdPercent > 100)
    return invalid()
  return Object.freeze({
    roundId: text(source.roundId),
    configurationId: text(source.configurationId),
    kind: enumValue(source.kind, VOTE_KINDS),
    state: enumValue(source.state, VOTE_ROUND_STATES),
    initiatorCrossplatformId: text(source.initiatorCrossplatformId),
    targetCrossplatformId: nullableText(source.targetCrossplatformId),
    scopeKey: text(source.scopeKey),
    eligibleCount: integer(source.eligibleCount, 0),
    thresholdPercent,
    minimumParticipants: integer(source.minimumParticipants, 1),
    allowVoteChange: bool(source.allowVoteChange),
    actionJobId: nullableText(source.actionJobId),
    actionOperationId: nullableText(source.actionOperationId),
    correlationId: nullableText(source.correlationId),
    openedAtUtc,
    expiresAtUtc,
    settledAtUtc,
    actionCompletedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export function parseVoteSettlement(value: unknown): VoteSettlement {
  const source = record(value, voteSettlementKeys)
  const participantCount = integer(source.participantCount, 0)
  const yesCount = integer(source.yesCount, 0)
  const noCount = integer(source.noCount, 0)
  if (yesCount + noCount !== participantCount)
    return invalid()
  return Object.freeze({
    status: enumValue(source.status, ['NotDue', 'Settled', 'AlreadySettled']),
    round: parseVoteRound(source.round),
    participantCount,
    yesCount,
    noCount,
    wasSettled: bool(source.wasSettled),
  })
}

export async function fetchVoteConfigurations(authorization: string, signal?: AbortSignal): Promise<readonly VoteConfiguration[]> {
  return collection(await requestJson<unknown>('/api/v1/community/vote-configurations', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseVoteConfiguration)
}

export async function updateVoteConfiguration(
  authorization: string,
  current: VoteConfiguration,
  input: VoteConfigurationInput,
  signal?: AbortSignal,
): Promise<VoteConfiguration> {
  const response = await requestJson<unknown>(`/api/v1/community/vote-configurations/${current.kind}`, {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      enabled: input.enabled,
      durationMs: wireInteger(input.durationMs),
      thresholdPercent: input.thresholdPercent,
      minimumParticipants: input.minimumParticipants,
      initiatorMinimumOnlineMs: wireInteger(input.initiatorMinimumOnlineMs),
      participantMinimumOnlineMs: wireInteger(input.participantMinimumOnlineMs),
      initiatorCooldownMs: wireInteger(input.initiatorCooldownMs),
      targetCooldownMs: wireInteger(input.targetCooldownMs),
      globalCooldownMs: wireInteger(input.globalCooldownMs),
      mutualExclusionScope: input.mutualExclusionScope,
      allowVoteChange: input.allowVoteChange,
      expectedRowVersion: wireInteger(current.rowVersion),
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseVoteConfiguration(response)
  if (authoritative.kind !== current.kind || authoritative.rowVersion <= current.rowVersion)
    return invalid()
  return authoritative
}

export async function fetchActionQueuedVoteRounds(authorization: string, signal?: AbortSignal): Promise<readonly VoteRound[]> {
  const result = collection(await requestJson<unknown>(queryPath('/api/v1/community/vote-rounds', { actionQueuedOnly: true }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseVoteRound)
  if (result.some(value => value.state !== 'ActionQueued'))
    return invalid()
  return result
}

export async function fetchVoteRounds(authorization: string, signal?: AbortSignal): Promise<readonly VoteRound[]> {
  return collection(await requestJson<unknown>('/api/v1/community/vote-rounds', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseVoteRound)
}

export async function fetchVoteRound(authorization: string, roundId: string, signal?: AbortSignal): Promise<VoteRound> {
  const response = await requestJson<unknown>(`/api/v1/community/vote-rounds/${encodeURIComponent(roundId)}`, {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseVoteRound(response)
  if (authoritative.roundId !== roundId)
    return invalid()
  return authoritative
}

export async function settleVoteRound(authorization: string, roundId: string, signal?: AbortSignal): Promise<VoteSettlement> {
  const response = await requestJson<unknown>(`/api/v1/community/vote-rounds/${encodeURIComponent(roundId)}/settle`, {
    method: 'POST',
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseVoteSettlement(response)
  if (authoritative.round.roundId !== roundId)
    return invalid()
  return authoritative
}
