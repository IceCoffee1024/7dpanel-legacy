import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

function readMessages(locale: string): Record<string, unknown> {
  const path = resolve(process.cwd(), `src/app/i18n/locales/${locale}.json`)
  return JSON.parse(readFileSync(path, 'utf8')) as Record<string, unknown>
}

const en = readMessages('en')
const zhCN = readMessages('zh-CN')

function flattenMessages(
  value: Record<string, unknown>,
  prefix = '',
): Map<string, string> {
  const messages = new Map<string, string>()

  for (const [key, child] of Object.entries(value)) {
    const path = prefix === '' ? key : `${prefix}.${key}`
    if (typeof child === 'string') {
      messages.set(path, child)
      continue
    }
    if (typeof child === 'object' && child !== null && !Array.isArray(child)) {
      for (const [childPath, message] of flattenMessages(
        child as Record<string, unknown>,
        path,
      )) {
        messages.set(childPath, message)
      }
      continue
    }
    throw new TypeError(`Invalid message value at ${path}`)
  }

  return messages
}

function interpolationParameters(message: string): string[] {
  return [...message.matchAll(/\{([A-Z]\w*)\}/gi)]
    .map(match => match[1])
    .sort()
}

describe('locale messages', () => {
  const enMessages = flattenMessages(en)
  const zhCNMessages = flattenMessages(zhCN)

  it('has the same non-empty leaf keys in both locales', () => {
    expect([...zhCNMessages.keys()].sort()).toEqual([...enMessages.keys()].sort())
    expect([...enMessages.values()].every(message => message.trim() !== '')).toBe(true)
    expect([...zhCNMessages.values()].every(message => message.trim() !== '')).toBe(true)
  })

  it('uses compatible interpolation parameters', () => {
    for (const [key, enMessage] of enMessages) {
      expect(interpolationParameters(zhCNMessages.get(key) ?? ''), key)
        .toEqual(interpolationParameters(enMessage))
    }
  })

  it('does not contain HTML messages', () => {
    for (const [key, message] of [...enMessages, ...zhCNMessages]) {
      expect(message, key).not.toMatch(/<\/?[A-Z][^>]*>/i)
    }
  })

  it('contains the complete overview dashboard and independent operation messages', () => {
    const requiredKeys = [
      'overview.status.partialTitle',
      'overview.serverInformation.worldName',
      'overview.hostPlatform.deviceId',
      'overview.resources.virtualAddressSpace',
      'overview.resources.swap',
      'overview.activity.empty',
      'overview.restartPolicy.nextRestart',
      'overview.quickActions.restart',
      'overview.restartDialog.accepted',
      'overview.shutdownDialog.accepted',
      'console.title',
      'console.command.placeholder',
      'console.command.feedback.unknown',
      'console.viewport.backToLatest',
      'forbidden.title',
      'shell.playersAndWorld',
      'gameResources.title',
      'gameResources.filters.searchPlaceholder',
      'gameResources.state.partialTitle',
      'gameResources.table.internalName',
      'gameResources.copy.success',
    ]
    for (const key of requiredKeys) {
      expect(enMessages.has(key), key).toBe(true)
      expect(zhCNMessages.has(key), key).toBe(true)
    }
  })
})
