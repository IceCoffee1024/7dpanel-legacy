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
      'overview.runtimeMetrics.metrics.gameMemoryBytes',
      'overview.runtimeMetrics.warning.unsupported',
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
      'players.profile.title',
      'players.profile.navigation',
      'players.profile.readOnlyNotice',
      'players.profile.state.staleTitle',
      'players.profile.section.unavailable',
      'players.profile.evidence.confirmed',
      'players.profile.evidence.observedChange',
      'players.profile.evidence.gap',
      'players.profile.inventory.uncomparable',
      'players.profile.skills.unknown',
      'players.profile.skills.notLoaded',
      'players.profile.skills.unsupportedByVersion',
      'players.profile.actions.status.pending',
      'players.profile.actions.status.succeeded',
      'players.profile.actions.status.failed',
      'players.profile.actions.status.resultUnknown',
      'players.profile.actions.grant.title',
      'players.profile.actions.remove.title',
      'players.profile.actions.resetSkills.title',
      'players.profile.actions.resetPartial.title',
      'players.profile.actions.resetFull.title',
      'audit.title',
      'gameEvents.title',
      'chatMutes.title',
      'gameChat.live.composer.keyboardHelp',
      'gameChat.history.filters.apply',
      'gameChat.settings.validation.commandPrefixes',
      'gameChat.colored.dialog.deleteConfirm',
      'gameChat.channels.Global',
      'gameChat.sources.Administrator',
      'gameChat.permissions.AdminOnly',
    ]
    for (const key of requiredKeys) {
      expect(enMessages.has(key), key).toBe(true)
      expect(zhCNMessages.has(key), key).toBe(true)
    }
  })

  it('has real English copy for every game-chat page instead of Chinese fallback', () => {
    const keys = [
      'gameChat.live.title',
      'gameChat.history.title',
      'gameChat.settings.title',
      'gameChat.colored.title',
      'gameChat.feedback.settingsOperationFailed',
    ]
    for (const key of keys) {
      const message = enMessages.get(key) ?? ''
      expect(message, key).not.toMatch(/[\u3400-\u9FFF]/)
      expect(message, key).not.toBe(zhCNMessages.get(key))
    }
  })

  it('has real English copy for player evidence and dangerous action states', () => {
    const keys = [
      'players.profile.title',
      'players.profile.evidence.gap',
      'players.profile.actions.status.resultUnknown',
      'players.profile.actions.resetFull.title',
    ]
    for (const key of keys) {
      const message = enMessages.get(key) ?? ''
      expect(message, key).not.toMatch(/[\u3400-\u9FFF]/)
      expect(message, key).not.toBe(zhCNMessages.get(key))
    }
  })

  it('contains complete automation, Discord, and GeoIP feature messages', () => {
    const requiredKeys = [
      'automation.condition.kind.All',
      'automation.condition.addChild',
      'automation.actions.moveDown',
      'automation.dryRun.traceTitle',
      'automation.execution.actionResults',
      'discord.secrets.keep',
      'discord.secrets.replace',
      'discord.secrets.clearConfirm',
      'discord.health.gateway',
      'discord.health.inbound',
      'discord.delivery.mobileSummary',
      'geoIp.secrets.maxMindLicenseKey',
      'geoIp.secrets.clearConfirm',
      'geoIp.diagnostics.persistentFailure',
      'geoIp.diagnostics.cacheHealth',
      'geoIp.diagnostics.dataVersion',
    ]
    for (const key of requiredKeys) {
      expect(enMessages.has(key), key).toBe(true)
      expect(zhCNMessages.has(key), key).toBe(true)
    }
  })

  it('contains complete backup policy messages', () => {
    const requiredKeys = [
      'backups.policies.title',
      'backups.policies.refresh',
      'backups.policies.state.stale',
      'backups.policies.field.cronExpression',
      'backups.policies.error.conflictDescription',
    ]
    for (const key of requiredKeys) {
      expect(enMessages.has(key), key).toBe(true)
      expect(zhCNMessages.has(key), key).toBe(true)
    }
  })
})
