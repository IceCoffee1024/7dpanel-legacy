import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getDiscordHealth, listDiscordDeliveries, parseDiscordConfiguration, parseDiscordHealth } from './discord'

const requestJson = vi.hoisted(() => vi.fn())

vi.mock('../../../shared/api/http', () => ({ requestJson }))

describe('discord API parser', () => {
  beforeEach(() => requestJson.mockReset())

  it('keeps only credential-presence metadata and rejects secret values', () => {
    const configuration = {
      version: 3,
      isEnabled: true,
      mode: 'Bot',
      applicationId: 'app-id',
      guildId: 'guild-id',
      publicChannelId: 'channel-id',
      bridgeGameToDiscord: true,
      bridgeDiscordToGame: false,
      proxy: { isEnabled: false, endpoint: null, hasCredentials: false },
      hasBotToken: true,
      targets: [{
        targetKey: 'public',
        deliveryMode: 'Bot',
        channelId: 'channel-id',
        isEnabled: true,
        hasCredential: false,
      }],
      updatedAtUtc: '2026-07-27T00:00:00+00:00',
    }

    expect(parseDiscordConfiguration(configuration).hasBotToken).toBe(true)
    expect(() => parseDiscordConfiguration({ ...configuration, botToken: 'secret' })).toThrow('Invalid server protocol')
  })

  it('rejects unknown delivery statuses and secret-like delivery fields', async () => {
    const delivery = {
      deliveryId: 'delivery-1',
      businessKey: 'business-1',
      targetKey: 'public',
      status: 'RetryScheduled',
      nextAttemptAtUtc: '2026-07-27T00:01:00+00:00',
      retryCount: 2,
      createdAtUtc: '2026-07-27T00:00:00+00:00',
      completedAtUtc: null,
    }
    requestJson.mockResolvedValueOnce([delivery])
    await expect(listDiscordDeliveries('Bearer owner')).resolves.toMatchObject([{ status: 'RetryScheduled' }])

    requestJson.mockResolvedValueOnce([{ ...delivery, status: 'Invented' }])
    await expect(listDiscordDeliveries('Bearer owner')).rejects.toThrow('Invalid server protocol')

    requestJson.mockResolvedValueOnce([{ ...delivery, contentText: 'secret message' }])
    await expect(listDiscordDeliveries('Bearer owner')).rejects.toThrow('Invalid server protocol')
  })

  it('requests and strictly parses the Discord health contract', async () => {
    const health = {
      gateway: { state: 'Connected', errorCode: null, observedAtUtc: '2026-07-28T08:00:00Z' },
      inbound: { state: 'Healthy', errorCode: null, observedAtUtc: '2026-07-28T08:00:01+00:00' },
    }
    requestJson.mockResolvedValueOnce(health)

    await expect(getDiscordHealth('Bearer owner')).resolves.toEqual(health)
    expect(requestJson).toHaveBeenCalledWith('/api/v1/integrations/discord/health', {
      headers: { Authorization: 'Bearer owner' },
      signal: undefined,
    })
    expect(() => parseDiscordHealth({ ...health, status: 'ok' })).toThrow('Invalid server protocol')
    expect(() => parseDiscordHealth({ ...health, gateway: { ...health.gateway, state: 'Running' } })).toThrow('Invalid server protocol')
  })
})
