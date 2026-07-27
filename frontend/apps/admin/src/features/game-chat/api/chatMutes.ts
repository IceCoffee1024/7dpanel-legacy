import type { InferOutput } from 'valibot'

import * as v from 'valibot'

import {
  createChatMuteMutation,
  listChatMutesQuery,
  releaseChatMuteMutation,
  updateChatMuteMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'

export interface ChatMuteRecord {
  readonly crossplatformId: string
  readonly displayName: string | null
  readonly reason: string
  readonly mutedUntilUtc: string | null
  readonly createdBy: string
  readonly createdAtUtc: string
  readonly updatedBy: string
  readonly updatedAtUtc: string
}

export interface ChatMuteCursor {
  readonly updatedAtUtc: string | null
  readonly crossplatformId: string | null
}

export interface ChatMutePage {
  readonly mutes: readonly ChatMuteRecord[]
  readonly nextCursor: ChatMuteCursor | null
}

export interface ChatMuteWriteInput {
  readonly displayName: string | null
  readonly reason: string
  readonly mutedUntilUtc: string | null
  readonly correlationId: string | null
}

export interface CreateChatMuteInput extends ChatMuteWriteInput {
  readonly crossplatformId: string
}

export type LoadChatMutes = (
  authorizationHeader: string,
  cursor: ChatMuteCursor,
  limit: number,
  signal?: AbortSignal,
) => Promise<ChatMutePage>
export type CreateChatMute = (authorizationHeader: string, input: CreateChatMuteInput, signal?: AbortSignal) => Promise<ChatMuteRecord>
export type UpdateChatMute = (authorizationHeader: string, crossplatformId: string, input: ChatMuteWriteInput, signal?: AbortSignal) => Promise<ChatMuteRecord>
export type ReleaseChatMute = (authorizationHeader: string, crossplatformId: string, correlationId: string | null, signal?: AbortSignal) => Promise<void>

const nonBlankString = v.pipe(v.string(), v.check(value => value.trim() !== ''))
const nullableNonBlankString = v.nullable(nonBlankString)
const utcTimestamp = v.pipe(v.string(), v.check(value =>
  /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
  && Number.isFinite(Date.parse(value)),
))
const muteSchema = v.strictObject({
  crossplatformId: nonBlankString,
  displayName: nullableNonBlankString,
  reason: nonBlankString,
  mutedUntilUtc: v.nullable(utcTimestamp),
  createdBy: nonBlankString,
  createdAtUtc: utcTimestamp,
  updatedBy: nonBlankString,
  updatedAtUtc: utcTimestamp,
})
const mutePageSchema = v.strictObject({
  mutes: v.array(muteSchema),
  nextCursorUpdatedAtUtc: v.nullable(utcTimestamp),
  nextCursorCrossplatformId: nullableNonBlankString,
})
type ParsedMute = InferOutput<typeof muteSchema>
type ParsedMutePage = InferOutput<typeof mutePageSchema>

function freezeMute(value: ParsedMute): ChatMuteRecord {
  return Object.freeze(value)
}

export function parseChatMute(value: unknown): ChatMuteRecord {
  try {
    return freezeMute(v.parse(muteSchema, value))
  }
  catch {
    throw new Error('Invalid chat mute response')
  }
}

export function parseChatMutePage(value: unknown): ChatMutePage {
  let parsed: ParsedMutePage
  try {
    parsed = v.parse(mutePageSchema, value)
    if ((parsed.nextCursorUpdatedAtUtc === null) !== (parsed.nextCursorCrossplatformId === null))
      throw new Error('cursor')
  }
  catch {
    throw new Error('Invalid chat mute page response')
  }
  return Object.freeze({
    mutes: Object.freeze(parsed.mutes.map(freezeMute)),
    nextCursor: parsed.nextCursorUpdatedAtUtc === null
      ? null
      : Object.freeze({
          updatedAtUtc: parsed.nextCursorUpdatedAtUtc,
          crossplatformId: parsed.nextCursorCrossplatformId,
        }),
  })
}

export const loadChatMutes: LoadChatMutes = async (authorizationHeader, cursor, limit, signal) => {
  const definition = listChatMutesQuery({
    headers: { Authorization: authorizationHeader },
    query: {
      limit,
      ...(cursor.updatedAtUtc === null ? {} : { cursorUpdatedAtUtc: cursor.updatedAtUtc }),
      ...(cursor.crossplatformId === null ? {} : { cursorCrossplatformId: cursor.crossplatformId }),
    },
  })
  return parseChatMutePage(await definition.query({ signal } as Parameters<typeof definition.query>[0]))
}

export const createChatMuteRecord: CreateChatMute = async (authorizationHeader, input, signal) => {
  const definition = createChatMuteMutation({ headers: { Authorization: authorizationHeader } })
  return parseChatMute(await definition.mutation({ body: input, signal }, {} as Parameters<typeof definition.mutation>[1]))
}

export const updateChatMuteRecord: UpdateChatMute = async (authorizationHeader, crossplatformId, input, signal) => {
  const definition = updateChatMuteMutation({ headers: { Authorization: authorizationHeader } })
  return parseChatMute(await definition.mutation({
    path: { crossplatformId },
    body: input,
    signal,
  }, {} as Parameters<typeof definition.mutation>[1]))
}

export const releaseChatMuteRecord: ReleaseChatMute = async (
  authorizationHeader,
  crossplatformId,
  correlationId,
  signal,
) => {
  const definition = releaseChatMuteMutation({ headers: { Authorization: authorizationHeader } })
  await definition.mutation({
    path: { crossplatformId },
    query: { correlationId },
    signal,
  }, {} as Parameters<typeof definition.mutation>[1])
}
