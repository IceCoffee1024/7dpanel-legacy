import type {
  CommunityGameCommandConfiguration,
  CommunityGameCommandConfigurationInput,
  CommunityGameCommandSetting,
} from './community.types'

import { requestJson } from '../../../shared/api/http'

import {
  collection,
  enumValue,
  headers,
  invalid,
  long,
  record,
  text,
  utc,
  wireInteger,
} from './community.protocol'
import { COMMUNITY_GAME_COMMAND_IDS } from './community.types'

const configurationKeys = ['commands', 'updatedAtUtc', 'rowVersion'] as const
const settingKeys = ['commandId', 'name', 'aliases'] as const

function parseGameCommandSetting(value: unknown): CommunityGameCommandSetting {
  const source = record(value, settingKeys)
  return Object.freeze({
    commandId: enumValue(source.commandId, COMMUNITY_GAME_COMMAND_IDS),
    name: text(source.name),
    aliases: collection(source.aliases, item => text(item)),
  })
}

export function parseGameCommandConfiguration(value: unknown): CommunityGameCommandConfiguration {
  const source = record(value, configurationKeys)
  const commands = collection(source.commands, parseGameCommandSetting)
  if (commands.length !== COMMUNITY_GAME_COMMAND_IDS.length
    || new Set(commands.map(command => command.commandId)).size !== COMMUNITY_GAME_COMMAND_IDS.length) {
    return invalid()
  }
  return Object.freeze({
    commands,
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: long(source.rowVersion),
  })
}

export async function fetchGameCommandConfiguration(
  authorization: string,
  signal?: AbortSignal,
): Promise<CommunityGameCommandConfiguration> {
  return parseGameCommandConfiguration(await requestJson<unknown>('/api/v1/community/game-command-configuration', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }))
}

export async function updateGameCommandConfiguration(
  authorization: string,
  current: CommunityGameCommandConfiguration,
  input: CommunityGameCommandConfigurationInput,
  signal?: AbortSignal,
): Promise<CommunityGameCommandConfiguration> {
  const response = await requestJson<unknown>('/api/v1/community/game-command-configuration', {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      commands: input.commands,
      expectedRowVersion: wireInteger(current.rowVersion),
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseGameCommandConfiguration(response)
  if (authoritative.rowVersion <= current.rowVersion)
    return invalid()
  return authoritative
}
