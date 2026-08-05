import type { FriendshipRecord, FriendshipStatus } from './community.types'

import { requestJson } from '../../../shared/api/http'

import {
  bool,
  collection,
  headers,
  invalid,
  queryPath,
  record,
  text,
  utc,
} from './community.protocol'

const friendshipKeys = ['firstCrossplatformId', 'secondCrossplatformId', 'areFriends'] as const
const friendshipRecordKeys = ['friendshipId', 'memberACrossplatformId', 'memberBCrossplatformId', 'createdByCrossplatformId', 'acceptedAtUtc'] as const

export function parseFriendshipStatus(value: unknown): FriendshipStatus {
  const source = record(value, friendshipKeys)
  return Object.freeze({
    firstCrossplatformId: text(source.firstCrossplatformId),
    secondCrossplatformId: text(source.secondCrossplatformId),
    areFriends: bool(source.areFriends),
  })
}

export function parseFriendshipRecord(value: unknown): FriendshipRecord {
  const source = record(value, friendshipRecordKeys)
  const memberACrossplatformId = text(source.memberACrossplatformId)
  const memberBCrossplatformId = text(source.memberBCrossplatformId)
  if (memberACrossplatformId >= memberBCrossplatformId)
    return invalid()
  return Object.freeze({
    friendshipId: text(source.friendshipId),
    memberACrossplatformId,
    memberBCrossplatformId,
    createdByCrossplatformId: text(source.createdByCrossplatformId),
    acceptedAtUtc: utc(source.acceptedAtUtc),
  })
}

export async function fetchFriendship(
  authorization: string,
  firstCrossplatformId: string,
  secondCrossplatformId: string,
  signal?: AbortSignal,
): Promise<FriendshipStatus> {
  const response = await requestJson<unknown>(queryPath('/api/v1/community/friendships', {
    firstCrossplatformId,
    secondCrossplatformId,
  }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseFriendshipStatus(response)
  if (authoritative.firstCrossplatformId !== firstCrossplatformId
    || authoritative.secondCrossplatformId !== secondCrossplatformId) {
    return invalid()
  }
  return authoritative
}

export async function fetchFriendshipRecords(authorization: string, signal?: AbortSignal): Promise<readonly FriendshipRecord[]> {
  return collection(await requestJson<unknown>('/api/v1/community/friendships/records', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseFriendshipRecord)
}
