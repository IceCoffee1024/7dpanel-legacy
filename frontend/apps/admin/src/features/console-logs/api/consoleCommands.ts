import {
  consoleCommandsGetCatalog,
  consoleCommandsPost,
} from '../../../shared/api/generated/sdk.gen'
import { HttpError } from '../../../shared/api/http'

export interface ConsoleCommandCatalogEntry {
  name: string
  aliases: readonly string[]
  description: string | null
  help: string | null
  permissionLevel: number | null
}

export interface ConsoleCommandCatalog {
  capturedAtUtc: string
  commands: readonly ConsoleCommandCatalogEntry[]
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function optionalText(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string')
    throw new HttpError('invalid', 'Invalid console command catalog response')
  return value
}

function parseEntry(value: unknown): ConsoleCommandCatalogEntry {
  if (!isRecord(value)
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || !Array.isArray(value.aliases)
    || value.aliases.some(alias => typeof alias !== 'string' || alias.trim() === '')
    || (value.permissionLevel !== null
      && (!Number.isSafeInteger(value.permissionLevel) || typeof value.permissionLevel !== 'number'))) {
    throw new HttpError('invalid', 'Invalid console command catalog response')
  }

  return Object.freeze({
    name: value.name,
    aliases: Object.freeze([...value.aliases] as string[]),
    description: optionalText(value.description),
    help: optionalText(value.help),
    permissionLevel: value.permissionLevel as number | null,
  })
}

export function parseConsoleCommandCatalog(value: unknown): ConsoleCommandCatalog {
  if (!isRecord(value)
    || typeof value.capturedAtUtc !== 'string'
    || value.capturedAtUtc.trim() === ''
    || !Array.isArray(value.commands)) {
    throw new HttpError('invalid', 'Invalid console command catalog response')
  }

  return Object.freeze({
    capturedAtUtc: value.capturedAtUtc,
    commands: Object.freeze(value.commands.map(parseEntry)),
  })
}

export async function fetchConsoleCommandCatalog(
  _authorizationHeader: string,
  signal?: AbortSignal,
): Promise<ConsoleCommandCatalog> {
  const response = await consoleCommandsGetCatalog({ signal })
  return parseConsoleCommandCatalog(response)
}

export async function executeConsoleCommand(command: string, signal?: AbortSignal): Promise<void> {
  const response = await consoleCommandsPost({
    body: { command },
    signal,
  })
  if (typeof response !== 'object' || response === null || !Array.isArray(response.output))
    throw new HttpError('invalid', 'Invalid console command response')
  if (response.output.some(line => typeof line !== 'string'))
    throw new HttpError('invalid', 'Invalid console command response')
  // The independent output is validated deliberately but never returned to the log UI.
}
