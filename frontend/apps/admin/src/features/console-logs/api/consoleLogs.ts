import type { ConsoleLogEntry } from '../model/consoleLog'

import * as v from 'valibot'

import { parseConsoleLogEntry } from '../model/consoleLog'

export type LoadRecentConsoleLogs = (
  limit: number,
  signal?: AbortSignal,
) => Promise<readonly ConsoleLogEntry[]>

export interface GeneratedRecentConsoleLogsRequestOptions {
  query: { limit: number }
  signal?: AbortSignal
}

export type GeneratedRecentConsoleLogsRequest = (
  options: GeneratedRecentConsoleLogsRequestOptions,
) => Promise<unknown>

const recentConsoleLogsSchema = v.strictObject({
  entries: v.array(v.unknown()),
})

export function parseRecentConsoleLogs(value: unknown): readonly ConsoleLogEntry[] {
  try {
    const response = v.parse(recentConsoleLogsSchema, value)
    return Object.freeze(response.entries.map(parseConsoleLogEntry))
  }
  catch {
    throw new Error('Invalid recent console logs response')
  }
}

export function createRecentConsoleLogsLoader(
  request: GeneratedRecentConsoleLogsRequest,
): LoadRecentConsoleLogs {
  return async (limit, signal) => parseRecentConsoleLogs(await request({
    query: { limit },
    signal,
  }))
}
