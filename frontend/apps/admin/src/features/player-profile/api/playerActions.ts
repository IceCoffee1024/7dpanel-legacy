import type {
  ClearInventoryHttpRequest,
  ClearInventoryHttpResponse,
  GrantItemHttpRequest,
  GrantItemHttpResponse,
  PlayerActionOperationHttpResponse,
  PlayerActionTargetHttpRequest,
  RemoveItemHttpRequest,
  RemoveItemHttpResponse,
  ResetPlayerDataHttpRequest,
  ResetPlayerDataHttpResponse,
  ResetSkillsHttpRequest,
  ResetSkillsHttpResponse,
} from '../../../shared/api/generated'

import {
  playerActionsClearInventory,
  playerActionsGet,
  playerActionsGrantItem,
  playerActionsRemoveItem,
  playerActionsResetPlayerData,
  playerActionsResetSkills,
} from '../../../shared/api/generated'

type RequiredContract<T> = T extends readonly (infer TItem)[]
  ? readonly RequiredContract<TItem>[]
  : T extends object
    ? { readonly [TKey in keyof T]-?: RequiredContract<T[TKey]> }
    : T

type RequiredFields<T, K extends keyof T> = Readonly<Required<Pick<T, K>>>

export type PlayerActionTarget = RequiredFields<
  PlayerActionTargetHttpRequest,
  'crossplatformId' | 'entityId' | 'onlineObservedAtUtc' | 'worldId'
>
export type GrantPlayerItemRequest = RequiredFields<
  GrantItemHttpRequest,
  'target' | 'catalogVersion' | 'resourceId' | 'quantity' | 'quality' | 'hiddenItemConfirmed' | 'clientRequestKey'
> & { readonly target: PlayerActionTarget }
export type RemovePlayerItemRequest = RequiredFields<
  RemoveItemHttpRequest,
  'target' | 'catalogVersion' | 'resourceId' | 'quantity' | 'quality' | 'removalScope' | 'removalMode' | 'clientRequestKey'
> & { readonly target: PlayerActionTarget }
export type ResetPlayerSkillsRequest = RequiredFields<
  ResetSkillsHttpRequest,
  'target' | 'clientRequestKey' | 'dangerConfirmed'
> & { readonly target: PlayerActionTarget, readonly dangerConfirmed: true }
export type ClearPlayerInventoryRequest = RequiredFields<
  ClearInventoryHttpRequest,
  'target' | 'clientRequestKey' | 'dangerConfirmed'
> & { readonly target: PlayerActionTarget, readonly dangerConfirmed: true }
export type ResetPlayerDataRequest = RequiredFields<
  ResetPlayerDataHttpRequest,
  'target' | 'clientRequestKey' | 'dangerConfirmed'
> & { readonly target: PlayerActionTarget, readonly dangerConfirmed: true }

export type GrantPlayerItemResult = RequiredContract<GrantItemHttpResponse>
export type RemovePlayerItemResult = RequiredContract<RemoveItemHttpResponse>
export type ResetPlayerSkillsResult = RequiredContract<ResetSkillsHttpResponse>
export type ClearPlayerInventoryResult = RequiredContract<ClearInventoryHttpResponse>
export type ResetPlayerDataResult = RequiredContract<ResetPlayerDataHttpResponse>
export type PlayerActionOperation = RequiredContract<PlayerActionOperationHttpResponse>
export type PlayerActionSubmission =
  | GrantPlayerItemResult
  | RemovePlayerItemResult
  | ResetPlayerSkillsResult
  | ClearPlayerInventoryResult
  | ResetPlayerDataResult

function targetBody(target: PlayerActionTarget): PlayerActionTargetHttpRequest {
  return {
    crossplatformId: target.crossplatformId,
    entityId: target.entityId,
    onlineObservedAtUtc: target.onlineObservedAtUtc,
    worldId: target.worldId,
  }
}

export async function grantPlayerItem(
  authorizationHeader: string,
  input: GrantPlayerItemRequest,
  signal?: AbortSignal,
): Promise<GrantPlayerItemResult> {
  return playerActionsGrantItem({
    headers: { Authorization: authorizationHeader },
    body: {
      target: targetBody(input.target),
      catalogVersion: input.catalogVersion,
      resourceId: input.resourceId,
      quantity: input.quantity,
      quality: input.quality,
      hiddenItemConfirmed: input.hiddenItemConfirmed,
      clientRequestKey: input.clientRequestKey,
    },
    signal,
  }) as Promise<GrantPlayerItemResult>
}

export async function removePlayerItem(
  authorizationHeader: string,
  input: RemovePlayerItemRequest,
  signal?: AbortSignal,
): Promise<RemovePlayerItemResult> {
  return playerActionsRemoveItem({
    headers: { Authorization: authorizationHeader },
    body: {
      target: targetBody(input.target),
      catalogVersion: input.catalogVersion,
      resourceId: input.resourceId,
      quantity: input.quantity,
      quality: input.quality,
      removalScope: input.removalScope,
      removalMode: input.removalMode,
      clientRequestKey: input.clientRequestKey,
    },
    signal,
  }) as Promise<RemovePlayerItemResult>
}

export async function resetPlayerSkills(
  authorizationHeader: string,
  input: ResetPlayerSkillsRequest,
  signal?: AbortSignal,
): Promise<ResetPlayerSkillsResult> {
  return playerActionsResetSkills({
    headers: { Authorization: authorizationHeader },
    body: {
      target: targetBody(input.target),
      clientRequestKey: input.clientRequestKey,
      dangerConfirmed: true,
    },
    signal,
  }) as Promise<ResetPlayerSkillsResult>
}

export async function clearPlayerInventory(
  authorizationHeader: string,
  input: ClearPlayerInventoryRequest,
  signal?: AbortSignal,
): Promise<ClearPlayerInventoryResult> {
  return playerActionsClearInventory({
    headers: { Authorization: authorizationHeader },
    body: {
      target: targetBody(input.target),
      clientRequestKey: input.clientRequestKey,
      dangerConfirmed: true,
    },
    signal,
  }) as Promise<ClearPlayerInventoryResult>
}

export async function resetPlayerData(
  authorizationHeader: string,
  input: ResetPlayerDataRequest,
  signal?: AbortSignal,
): Promise<ResetPlayerDataResult> {
  return playerActionsResetPlayerData({
    headers: { Authorization: authorizationHeader },
    body: {
      target: targetBody(input.target),
      clientRequestKey: input.clientRequestKey,
      dangerConfirmed: true,
    },
    signal,
  }) as Promise<ResetPlayerDataResult>
}

export async function fetchPlayerActionOperation(
  authorizationHeader: string,
  operationId: string,
  signal?: AbortSignal,
): Promise<PlayerActionOperation> {
  return playerActionsGet({
    headers: { Authorization: authorizationHeader },
    path: { operationId },
    signal,
  }) as Promise<PlayerActionOperation>
}
