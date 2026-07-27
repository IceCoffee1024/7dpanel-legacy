import type {
  PlayerEvidenceGapHttpResponse,
  PlayerInventoryDiffHttpResponse,
  PlayerInventoryDiffsPageHttpResponse,
  PlayerInventorySnapshotHttpResponse,
  PlayerInventorySnapshotsPageHttpResponse,
  PlayerProfileHttpResponse,
  PlayerSkillSnapshotHttpResponse,
  PlayerSkillsPageHttpResponse,
} from '../../../shared/api/generated'

import {
  playerEvidenceGetInventoryDiffs,
  playerEvidenceGetInventorySnapshots,
  playerEvidenceGetProfile,
  playerEvidenceGetSkills,
} from '../../../shared/api/generated'

type RequiredContract<T> = T extends readonly (infer TItem)[]
  ? readonly RequiredContract<TItem>[]
  : T extends object
    ? { readonly [TKey in keyof T]-?: RequiredContract<T[TKey]> }
    : T

export type PlayerProfile = RequiredContract<PlayerProfileHttpResponse>
export type PlayerEvidenceGap = RequiredContract<PlayerEvidenceGapHttpResponse>
export type PlayerInventorySnapshot = RequiredContract<PlayerInventorySnapshotHttpResponse>
export type PlayerInventoryDiff = RequiredContract<PlayerInventoryDiffHttpResponse>
export type PlayerSkillSnapshot = RequiredContract<PlayerSkillSnapshotHttpResponse>
export type PlayerInventorySnapshotsPage = RequiredContract<PlayerInventorySnapshotsPageHttpResponse>
export type PlayerInventoryDiffsPage = RequiredContract<PlayerInventoryDiffsPageHttpResponse>
export type PlayerSkillsPage = RequiredContract<PlayerSkillsPageHttpResponse>

export interface PlayerEvidencePageOptions {
  readonly pageSize?: number
  readonly cursor?: string | null
}

export type FetchPlayerProfile = (
  authorizationHeader: string,
  crossplatformId: string,
  signal?: AbortSignal,
) => Promise<PlayerProfile>

export type FetchPlayerInventorySnapshots = (
  authorizationHeader: string,
  crossplatformId: string,
  options?: PlayerEvidencePageOptions,
  signal?: AbortSignal,
) => Promise<PlayerInventorySnapshotsPage>

export type FetchPlayerInventoryDiffs = (
  authorizationHeader: string,
  crossplatformId: string,
  options?: PlayerEvidencePageOptions,
  signal?: AbortSignal,
) => Promise<PlayerInventoryDiffsPage>

export type FetchPlayerSkills = (
  authorizationHeader: string,
  crossplatformId: string,
  options?: PlayerEvidencePageOptions,
  signal?: AbortSignal,
) => Promise<PlayerSkillsPage>

function evidenceQuery(options: PlayerEvidencePageOptions | undefined) {
  return {
    ...(options?.cursor == null ? {} : { cursor: options.cursor }),
    ...(options?.pageSize === undefined ? {} : { pageSize: options.pageSize }),
  }
}

export const fetchPlayerProfile: FetchPlayerProfile = (authorizationHeader, crossplatformId, signal) =>
  playerEvidenceGetProfile({
    headers: { Authorization: authorizationHeader },
    path: { crossplatformId },
    signal,
  }) as Promise<PlayerProfile>

export const fetchPlayerInventorySnapshots: FetchPlayerInventorySnapshots = (
  authorizationHeader,
  crossplatformId,
  options,
  signal,
) => playerEvidenceGetInventorySnapshots({
  headers: { Authorization: authorizationHeader },
  path: { crossplatformId },
  query: evidenceQuery(options),
  signal,
}) as Promise<PlayerInventorySnapshotsPage>

export const fetchPlayerInventoryDiffs: FetchPlayerInventoryDiffs = (
  authorizationHeader,
  crossplatformId,
  options,
  signal,
) => playerEvidenceGetInventoryDiffs({
  headers: { Authorization: authorizationHeader },
  path: { crossplatformId },
  query: evidenceQuery(options),
  signal,
}) as Promise<PlayerInventoryDiffsPage>

export const fetchPlayerSkills: FetchPlayerSkills = (
  authorizationHeader,
  crossplatformId,
  options,
  signal,
) => playerEvidenceGetSkills({
  headers: { Authorization: authorizationHeader },
  path: { crossplatformId },
  query: evidenceQuery(options),
  signal,
}) as Promise<PlayerSkillsPage>
