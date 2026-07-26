import type { ChatMessage } from '../model/chatMessage'
import type {
  ChatHistoryMessage,
  ChatSettings,
  ColoredChatProfile,
  ColoredChatSettings,
} from '../model/gameChatManagement'

import * as v from 'valibot'

import {
  chatGetRecentMessagesQuery,
  chatSendGlobalMessageMutation,
  chatSendPrivateMessageMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'

export type LoadRecentChatMessages = (
  limit: number,
  signal?: AbortSignal,
) => Promise<readonly ChatMessage[]>

export interface SendChatInput {
  message: string
  targetCrossplatformId: string | null
}

export type SendChatMessage = (
  authorizationHeader: string,
  input: SendChatInput,
  signal?: AbortSignal,
) => Promise<void>

export interface ChatHistoryGap {
  startedAtUtc: string
  endedAtUtc: string
  droppedMessageCount: number
  reason: string
}

export interface ChatHistoryPage {
  messages: readonly ChatHistoryMessage[]
  nextCursor: string | null
  gaps: readonly ChatHistoryGap[]
}

export interface ColoredChatProfilePage {
  profiles: readonly ColoredChatProfile[]
  nextCursor: string | null
}

const safePositiveInteger = v.pipe(
  v.number(),
  v.integer(),
  v.minValue(1),
  v.maxValue(Number.MAX_SAFE_INTEGER),
)
const safeNonNegativeInteger = v.pipe(
  v.number(),
  v.integer(),
  v.minValue(0),
  v.maxValue(Number.MAX_SAFE_INTEGER),
)
const utcTimestamp = v.pipe(v.string(), v.isoTimestamp())
const chatChannel = v.picklist(['Global', 'Friends', 'Party', 'Whisper', 'Unknown'])
const chatSourceKind = v.picklist(['Player', 'Administrator', 'System'])
const nullableColor = v.nullable(v.pipe(v.string(), v.regex(/^[0-9A-F]{6}$/)))

const chatMessageSchema = v.strictObject({
  sequence: safePositiveInteger,
  occurredAtUtc: utcTimestamp,
  entityId: v.pipe(v.number(), v.integer(), v.minValue(Number.MIN_SAFE_INTEGER), v.maxValue(Number.MAX_SAFE_INTEGER)),
  crossplatformId: v.nullable(v.string()),
  senderName: v.string(),
  channel: chatChannel,
  sourceKind: chatSourceKind,
  message: v.string(),
})
const recentChatMessagesSchema = v.strictObject({ messages: v.array(chatMessageSchema) })
const historyMessageSchema = v.strictObject({
  sequence: safePositiveInteger,
  occurredAtUtc: utcTimestamp,
  entityId: v.pipe(v.number(), v.integer(), v.minValue(Number.MIN_SAFE_INTEGER), v.maxValue(Number.MAX_SAFE_INTEGER)),
  crossplatformId: v.nullable(v.string()),
  senderName: v.nullable(v.string()),
  channel: chatChannel,
  sourceKind: chatSourceKind,
  message: v.string(),
})
const historyGapSchema = v.strictObject({
  startedAtUtc: utcTimestamp,
  endedAtUtc: utcTimestamp,
  droppedMessageCount: safeNonNegativeInteger,
  reason: v.string(),
})
const historyPageSchema = v.strictObject({
  messages: v.array(historyMessageSchema),
  nextCursor: v.nullable(v.string()),
  gaps: v.array(historyGapSchema),
})
const chatSettingsSchema = v.strictObject({
  isEnabled: v.boolean(),
  globalServerName: v.nullable(v.string()),
  whisperServerName: v.nullable(v.string()),
  commandPrefixes: v.array(v.pipe(v.string(), v.length(1))),
  excludeCommandsFromHistory: v.boolean(),
  historyRetentionDays: v.pipe(v.number(), v.integer(), v.minValue(0), v.maxValue(3650)),
})
const coloredChatSettingsSchema = v.strictObject({
  isEnabled: v.boolean(),
  globalDefaultColor: nullableColor,
  whisperDefaultColor: nullableColor,
  friendsDefaultColor: nullableColor,
  partyDefaultColor: nullableColor,
  adminDefaultColor: nullableColor,
  systemDefaultColor: nullableColor,
  playerColorTagPermission: v.picklist(['None', 'AdminOnly', 'All']),
})
const coloredChatProfileSchema = v.strictObject({
  crossplatformId: v.string(),
  customName: v.nullable(v.string()),
  nameColor: nullableColor,
  textColor: nullableColor,
  description: v.nullable(v.string()),
  createdAtUtc: utcTimestamp,
  updatedAtUtc: utcTimestamp,
})
const coloredChatProfilePageSchema = v.strictObject({
  profiles: v.array(coloredChatProfileSchema),
  nextCursor: v.nullable(v.string()),
})

function parseStrict<T>(schema: v.BaseSchema<unknown, T, v.BaseIssue<unknown>>, value: unknown, message: string): T {
  try {
    return v.parse(schema, value)
  }
  catch {
    throw new Error(message)
  }
}

export function parseChatMessage(value: unknown): ChatMessage {
  return Object.freeze(parseStrict(chatMessageSchema, value, 'Invalid chat message'))
}

export function parseRecentChatMessages(value: unknown): readonly ChatMessage[] {
  const parsed = parseStrict(recentChatMessagesSchema, value, 'Invalid recent chat messages response')
  return Object.freeze(parsed.messages.map(item => Object.freeze(item)))
}

export function parseChatHistoryPage(value: unknown): ChatHistoryPage {
  const parsed = parseStrict(historyPageSchema, value, 'Invalid chat history response')
  return Object.freeze({
    messages: Object.freeze(parsed.messages.map(({ channel, ...item }) => Object.freeze({
      ...item,
      chatType: channel,
    }))),
    nextCursor: parsed.nextCursor,
    gaps: Object.freeze(parsed.gaps.map(item => Object.freeze(item))),
  })
}

export function parseChatSettings(value: unknown): ChatSettings {
  const parsed = parseStrict(chatSettingsSchema, value, 'Invalid chat settings response')
  return Object.freeze({
    ...parsed,
    commandPrefixes: Object.freeze([...parsed.commandPrefixes]) as string[],
  })
}

export function parseColoredChatSettings(value: unknown): ColoredChatSettings {
  return Object.freeze(parseStrict(coloredChatSettingsSchema, value, 'Invalid colored chat settings response'))
}

export function parseColoredChatProfile(value: unknown): ColoredChatProfile {
  return Object.freeze(parseStrict(coloredChatProfileSchema, value, 'Invalid colored chat profile response'))
}

export function parseColoredChatProfilePage(value: unknown): ColoredChatProfilePage {
  const parsed = parseStrict(coloredChatProfilePageSchema, value, 'Invalid colored chat profiles response')
  return Object.freeze({
    profiles: Object.freeze(parsed.profiles.map(item => Object.freeze(item))),
    nextCursor: parsed.nextCursor,
  })
}

export const sendChatMessage: SendChatMessage = async (authorization, input, signal) => {
  if (input.targetCrossplatformId === null) {
    const definition = chatSendGlobalMessageMutation({
      headers: { Authorization: authorization },
    })
    await definition.mutation({
      body: { message: input.message },
      signal,
    }, {} as Parameters<typeof definition.mutation>[1])
    return
  }

  const definition = chatSendPrivateMessageMutation({
    headers: { Authorization: authorization },
  })
  await definition.mutation({
    body: {
      message: input.message,
      targetCrossplatformId: input.targetCrossplatformId,
    },
    signal,
  }, {} as Parameters<typeof definition.mutation>[1])
}

export function createRecentChatMessagesLoader(
  authorization: () => string | null,
): LoadRecentChatMessages {
  return async (limit, signal) => {
    const header = authorization()
    if (header === null)
      throw new Error('Authentication is required')
    const definition = chatGetRecentMessagesQuery({
      headers: { Authorization: header },
      query: { limit },
    })
    return parseRecentChatMessages(await definition.query({
      signal,
    } as Parameters<typeof definition.query>[0]))
  }
}
