import type { ChatHistoryPage, ColoredChatProfilePage } from '../api/chat'
import type { ChatHistoryFilters, ChatSettings, ColoredChatProfile, ColoredChatSettings } from './gameChatManagement'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { useChatHistory } from './useChatHistory'
import { useChatSettings } from './useChatSettings'
import { useColoredChat } from './useColoredChat'

const auth = () => ({ authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() })

function mountComposable<T>(create: () => T) {
  let controller!: T
  const Host = defineComponent({
    setup() {
      controller = create()
      return () => null
    },
  })
  const wrapper = mount(Host)
  return { controller: () => controller, wrapper }
}

function historyPage(sequence: number, nextCursor: string | null = null): ChatHistoryPage {
  return {
    messages: [{
      sequence,
      occurredAtUtc: '2026-07-26T08:00:00Z',
      entityId: sequence,
      crossplatformId: `EOS_${sequence}`,
      senderName: `player-${sequence}`,
      chatType: 'Global',
      sourceKind: 'Player',
      message: `message-${sequence}`,
    }],
    nextCursor,
    gaps: [],
  }
}

const chatSettings: ChatSettings = {
  isEnabled: true,
  globalServerName: 'Server',
  whisperServerName: null,
  commandPrefixes: ['/'],
  allowNoPrefix: false,
  commandParameterSeparator: ' ',
  hideRegisteredCommandGlobalMessages: true,
  excludeCommandsFromHistory: true,
  historyRetentionDays: 30,
}

const coloredSettings: ColoredChatSettings = {
  isEnabled: true,
  globalDefaultColor: 'FFFFFF',
  whisperDefaultColor: null,
  friendsDefaultColor: null,
  partyDefaultColor: null,
  adminDefaultColor: 'FF0000',
  systemDefaultColor: null,
  playerColorTagPermission: 'AdminOnly',
}

const profile: ColoredChatProfile = {
  crossplatformId: 'EOS_player',
  customName: '{playerName}',
  nameColor: '00FF00',
  textColor: null,
  description: null,
  createdAtUtc: '2026-07-26T08:00:00Z',
  updatedAtUtc: '2026-07-26T08:00:00Z',
}

describe('game chat management composables', () => {
  it('drives history from URL filters, cancels obsolete requests, uses server cursor and keeps stale data', async () => {
    const signals: AbortSignal[] = []
    const fetchHistory = vi.fn((_header: string, filters: ChatHistoryFilters, cursor: string | null, _limit: number, signal?: AbortSignal) => {
      if (signal)
        signals.push(signal)
      if (filters.senderName === 'new')
        return Promise.resolve(cursor === null ? historyPage(2, 'cursor-2') : historyPage(1))
      if (filters.senderName === 'fail')
        return Promise.reject(new Error('offline'))
      return new Promise<ChatHistoryPage>(() => {})
    })
    const replaceQuery = vi.fn().mockResolvedValue(undefined)
    const mounted = mountComposable(() => useChatHistory({
      auth: auth(),
      route: { query: { senderName: 'old', cursor: 'old-cursor' } },
      replaceQuery,
      fetchHistory,
    }))
    await flushPromises()

    await mounted.controller().applyFilters({
      crossplatformId: '',
      senderName: ' new ',
      chatType: '',
      sourceKind: '',
      startUtc: '',
      endUtc: '',
    })
    expect(signals[0]?.aborted).toBe(true)
    expect(replaceQuery).toHaveBeenCalledWith({ senderName: 'new' })
    expect(mounted.controller().messages.value[0]?.sequence).toBe(2)

    await mounted.controller().loadMore()
    expect(fetchHistory).toHaveBeenLastCalledWith('Bearer owner', expect.objectContaining({ senderName: 'new' }), 'cursor-2', 100, expect.any(AbortSignal))
    expect(mounted.controller().messages.value.map(message => message.sequence)).toEqual([2, 1])

    await mounted.controller().applyFilters({ ...mounted.controller().filters.value, senderName: 'fail' })
    expect(mounted.controller().state.value).toBe('failed')
    mounted.wrapper.unmount()
  })

  it('keeps authoritative chat settings on failed save and accepts complete reset responses', async () => {
    const saveSettings = vi.fn().mockRejectedValue(new Error('not saved'))
    const resetValue = { ...chatSettings, historyRetentionDays: 0, globalServerName: null }
    const invalidateSettings = vi.fn().mockResolvedValue(undefined)
    const mounted = mountComposable(() => useChatSettings({
      auth: auth(),
      fetchSettings: vi.fn().mockResolvedValue(chatSettings),
      saveSettings,
      resetSettings: vi.fn().mockResolvedValue(resetValue),
      invalidateSettings,
    }))
    await flushPromises()
    mounted.controller().setDirty(true)

    await expect(mounted.controller().save({ ...chatSettings, historyRetentionDays: 90 })).resolves.toBe(false)
    expect(mounted.controller().settings.value).toEqual(chatSettings)
    expect(mounted.controller().isDirty.value).toBe(true)
    expect(mounted.controller().state.value).toBe('stale')

    await expect(mounted.controller().reset()).resolves.toBe(true)
    expect(mounted.controller().settings.value).toEqual(resetValue)
    expect(mounted.controller().isDirty.value).toBe(false)
    expect(invalidateSettings).toHaveBeenCalledWith({ exact: true })
    mounted.wrapper.unmount()
  })

  it('exposes leave confirmation only for dirty settings', async () => {
    const mounted = mountComposable(() => useChatSettings({
      auth: auth(),
      fetchSettings: vi.fn().mockResolvedValue(chatSettings),
    }))
    await flushPromises()
    const confirmLeave = vi.fn().mockReturnValue(false)
    expect(mounted.controller().canLeave(confirmLeave)).toBe(true)
    mounted.controller().setDirty(true)
    expect(mounted.controller().canLeave(confirmLeave)).toBe(false)
    expect(confirmLeave).toHaveBeenCalledOnce()
    mounted.wrapper.unmount()
  })

  it('filters and paginates profiles, then reloads authoritative data after CRUD without optimism', async () => {
    const pages: ColoredChatProfilePage[] = [
      { profiles: [], nextCursor: null },
      { profiles: [profile], nextCursor: null },
    ]
    const fetchProfiles = vi.fn().mockImplementation(() => Promise.resolve(pages.shift() ?? { profiles: [profile], nextCursor: null }))
    const createProfile = vi.fn().mockResolvedValue(profile)
    const invalidateProfiles = vi.fn().mockResolvedValue(undefined)
    const mounted = mountComposable(() => useColoredChat({
      auth: auth(),
      fetchSettings: vi.fn().mockResolvedValue(coloredSettings),
      fetchProfiles,
      createProfile,
      invalidateProfiles,
    }))
    await flushPromises()
    expect(mounted.controller().profiles.value).toEqual([])

    await expect(mounted.controller().createProfile(profile)).resolves.toBe(true)
    expect(mounted.controller().profiles.value).toEqual([profile])
    expect(invalidateProfiles).toHaveBeenCalledWith({ exact: true })
    expect(fetchProfiles).toHaveBeenLastCalledWith('Bearer owner', '', null, 50, expect.any(AbortSignal))

    await mounted.controller().filterProfiles(' EOS ')
    expect(mounted.controller().profileFilter.value).toBe('EOS')
    expect(fetchProfiles).toHaveBeenLastCalledWith('Bearer owner', 'EOS', null, 50, expect.any(AbortSignal))
    mounted.wrapper.unmount()
  })
})
