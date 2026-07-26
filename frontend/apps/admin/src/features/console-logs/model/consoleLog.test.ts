import { describe, expect, it, vi } from 'vitest'

import { createRecentConsoleLogsLoader, parseRecentConsoleLogs } from '../api/consoleLogs'
import { parseConsoleLogEntry } from './consoleLog'

function rawEntry(sequence = 7) {
  return {
    sequence,
    formattedMessage: '2026-07-26 7 INF hello',
    message: 'hello',
    trace: null,
    logType: 'log',
    timestamp: '2026-07-26T08:00:00Z',
    uptimeMilliseconds: 12_345,
  }
}

describe('console log payload parsing', () => {
  it('parses the shared REST and SSE entry contract', () => {
    expect(parseConsoleLogEntry(rawEntry())).toEqual(rawEntry())
    expect(parseRecentConsoleLogs({ entries: [rawEntry()] })).toEqual([rawEntry()])
  })

  it.each([
    ['non-positive sequence', { ...rawEntry(), sequence: 0 }],
    ['fractional sequence', { ...rawEntry(), sequence: 1.5 }],
    ['invalid timestamp', { ...rawEntry(), timestamp: 'yesterday' }],
    ['negative uptime', { ...rawEntry(), uptimeMilliseconds: -1 }],
    ['missing source field', (({ trace: _trace, ...entry }) => entry)(rawEntry())],
    ['unexpected source field', { ...rawEntry(), isHighPriority: false }],
  ])('rejects %s', (_name, value) => {
    expect(() => parseConsoleLogEntry(value)).toThrow('Invalid console log entry')
  })

  it('rejects an invalid REST envelope or any invalid entry', () => {
    expect(() => parseRecentConsoleLogs([rawEntry()])).toThrow('Invalid recent console logs response')
    expect(() => parseRecentConsoleLogs({ entries: [rawEntry(), { ...rawEntry(8), trace: undefined }] }))
      .toThrow('Invalid recent console logs response')
  })

  it('adapts a future generated request at one explicit boundary', async () => {
    const request = vi.fn().mockResolvedValue({ entries: [rawEntry()] })
    const loadRecent = createRecentConsoleLogsLoader(request)
    const controller = new AbortController()

    await expect(loadRecent(1000, controller.signal)).resolves.toEqual([rawEntry()])
    expect(request).toHaveBeenCalledExactlyOnceWith({
      query: { limit: 1000 },
      signal: controller.signal,
    })
  })
})
