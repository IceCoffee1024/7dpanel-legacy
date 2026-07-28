import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import * as generated from '../../../shared/api/generated/@pinia/colada.gen'
import { useChatHistory } from './useChatHistory'
import { useChatSettings } from './useChatSettings'
import { useColoredChat } from './useColoredChat'

vi.mock('../../../shared/api/generated/@pinia/colada.gen', () => ({
  chatCreateColoredProfileMutation: vi.fn(),
  chatDeleteColoredProfileMutation: vi.fn(),
  chatGetColoredProfilesQuery: vi.fn(),
  chatGetColoredSettingsQuery: vi.fn(),
  chatGetMessagesQuery: vi.fn(),
  chatGetSettingsQuery: vi.fn(),
  chatResetColoredSettingsMutation: vi.fn(),
  chatResetSettingsMutation: vi.fn(),
  chatUpdateColoredProfileMutation: vi.fn(),
  chatUpdateColoredSettingsMutation: vi.fn(),
  chatUpdateSettingsMutation: vi.fn(),
}))

const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
const message = {
  sequence: 1,
  occurredAtUtc: '2026-07-26T08:00:00Z',
  entityId: 1,
  crossplatformId: 'EOS_player',
  senderName: 'Player',
  channel: 'Global',
  sourceKind: 'Player',
  message: 'hello',
}
const settings = {
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
const coloredSettings = {
  isEnabled: true,
  globalDefaultColor: 'FFFFFF',
  whisperDefaultColor: null,
  friendsDefaultColor: null,
  partyDefaultColor: null,
  adminDefaultColor: 'FF0000',
  systemDefaultColor: null,
  playerColorTagPermission: 'AdminOnly' as const,
}
const profile = {
  crossplatformId: 'EOS_player',
  customName: '{playerName}',
  nameColor: '00FF00',
  textColor: null,
  description: null,
  createdAtUtc: '2026-07-26T08:00:00Z',
  updatedAtUtc: '2026-07-26T08:00:00Z',
}

function definition(result: unknown) {
  return { mutation: vi.fn().mockResolvedValue(result), query: vi.fn().mockResolvedValue(result) }
}

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

describe('generated game chat management transport', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(generated.chatGetMessagesQuery).mockReturnValue(definition({ messages: [message], gaps: [], nextCursor: null }) as never)
    vi.mocked(generated.chatGetSettingsQuery).mockReturnValue(definition(settings) as never)
    vi.mocked(generated.chatUpdateSettingsMutation).mockReturnValue(definition(settings) as never)
    vi.mocked(generated.chatResetSettingsMutation).mockReturnValue(definition(settings) as never)
    vi.mocked(generated.chatGetColoredSettingsQuery).mockReturnValue(definition(coloredSettings) as never)
    vi.mocked(generated.chatUpdateColoredSettingsMutation).mockReturnValue(definition(coloredSettings) as never)
    vi.mocked(generated.chatResetColoredSettingsMutation).mockReturnValue(definition(coloredSettings) as never)
    vi.mocked(generated.chatGetColoredProfilesQuery).mockReturnValue(definition({ profiles: [profile], nextCursor: null }) as never)
    vi.mocked(generated.chatCreateColoredProfileMutation).mockReturnValue(definition(profile) as never)
    vi.mocked(generated.chatUpdateColoredProfileMutation).mockReturnValue(definition(profile) as never)
    vi.mocked(generated.chatDeleteColoredProfileMutation).mockReturnValue(definition(undefined) as never)
  })

  it('uses the generated history query with exact filters, auth and signal', async () => {
    const mounted = mountComposable(() => useChatHistory({
      auth,
      route: { query: { senderName: 'Player' } },
      replaceQuery: vi.fn(),
    }))
    await flushPromises()

    expect(generated.chatGetMessagesQuery).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      query: { limit: 100, senderName: 'Player' },
    })
    const query = vi.mocked(generated.chatGetMessagesQuery).mock.results[0]!.value.query
    expect(query.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ signal: expect.any(AbortSignal) }))
    expect(mounted.controller().messages.value).toHaveLength(1)
    mounted.wrapper.unmount()
  })

  it('uses generated settings query and mutations with exact bodies', async () => {
    const mounted = mountComposable(() => useChatSettings({ auth }))
    await flushPromises()
    await mounted.controller().save(settings)
    await mounted.controller().reset()

    expect(generated.chatGetSettingsQuery).toHaveBeenCalledWith({ headers: { Authorization: 'Bearer owner' } })
    const save = vi.mocked(generated.chatUpdateSettingsMutation).mock.results[0]!.value.mutation
    expect(save.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ body: settings, signal: expect.any(AbortSignal) }))
    const reset = vi.mocked(generated.chatResetSettingsMutation).mock.results[0]!.value.mutation
    expect(reset.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ signal: expect.any(AbortSignal) }))
    mounted.wrapper.unmount()
  })

  it('uses every generated colored settings and profile operation without optimistic state', async () => {
    const mounted = mountComposable(() => useColoredChat({ auth }))
    await flushPromises()
    await mounted.controller().saveSettings(coloredSettings)
    await mounted.controller().resetSettings()
    await mounted.controller().createProfile(profile)
    await mounted.controller().updateProfile(profile)
    await mounted.controller().deleteProfile(profile.crossplatformId)

    expect(generated.chatGetColoredSettingsQuery).toHaveBeenCalled()
    expect(generated.chatGetColoredProfilesQuery).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      query: { limit: 50 },
    })
    expect(generated.chatUpdateColoredSettingsMutation).toHaveBeenCalled()
    expect(generated.chatResetColoredSettingsMutation).toHaveBeenCalled()
    expect(generated.chatCreateColoredProfileMutation).toHaveBeenCalled()
    expect(generated.chatUpdateColoredProfileMutation).toHaveBeenCalled()
    expect(generated.chatDeleteColoredProfileMutation).toHaveBeenCalled()
    expect(mounted.controller().profiles.value).toEqual([profile])
    mounted.wrapper.unmount()
  })
})
