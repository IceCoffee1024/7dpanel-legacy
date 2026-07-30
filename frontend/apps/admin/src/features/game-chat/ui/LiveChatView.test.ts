import type { AdminLocaleRuntime } from '../../../app/i18n'
import type { OnlinePlayer } from '../../players/api/onlinePlayers'
import type { ChatMessage } from '../model/chatMessage'

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

import { createAdminI18n } from '../../../app/i18n'
import LiveChatView from './LiveChatView.vue'

const messages: ChatMessage[] = [
  {
    sequence: 1,
    occurredAtUtc: '2026-07-26T08:00:00Z',
    entityId: 7,
    crossplatformId: 'EOS_player-7',
    senderName: 'Alice',
    channel: 'Global',
    sourceKind: 'Player',
    message: '<img src=x onerror="alert(1)">hello',
  },
  {
    sequence: 2,
    occurredAtUtc: '2026-07-26T08:01:00Z',
    entityId: 8,
    crossplatformId: 'EOS_player-8',
    senderName: 'Bob',
    channel: 'Party',
    sourceKind: 'Administrator',
    message: 'party line',
  },
]

function makePlayer(overrides: Partial<OnlinePlayer> = {}): OnlinePlayer {
  return {
    entityId: 7,
    name: 'Alice',
    platformIdentity: { combinedId: 'Steam_7', platform: 'Steam' },
    crossplatformIdentity: { combinedId: 'EOS_player-7', platform: 'EOS' },
    deviceType: 'windows',
    ip: null,
    ping: 42,
    compatibilityVersion: null,
    discordUserId: null,
    permissionLevel: 1000,
    position: { x: 0, y: 0, z: 0 },
    isDead: false,
    health: 100,
    maxHealth: 100,
    level: 1,
    playGroup: null,
    lastLoginUtc: null,
    gameStage: null,
    expToNextLevel: null,
    skillPoints: null,
    bedroll: null,
    score: 0,
    zombieKills: 0,
    playerKills: 0,
    deaths: 0,
    totalTimePlayedMinutes: 1,
    distanceWalkedMeters: 0,
    totalItemsCrafted: 0,
    longestLifeMinutes: 1,
    currentLifeMinutes: 1,
    observedAtUtc: '2026-07-26T08:01:00Z',
    ...overrides,
  }
}

const eligiblePlayer = makePlayer()
const unavailablePlayer = makePlayer({
  entityId: 9,
  name: 'Platform only',
  platformIdentity: { combinedId: 'Steam_9', platform: 'Steam' },
  crossplatformIdentity: null,
})

function localeRuntime(locale: 'en' | 'zh-CN'): AdminLocaleRuntime {
  return createAdminI18n({
    repository: {
      restore: () => locale,
      save: () => true,
      subscribe: () => () => {},
    },
    documentElement: { lang: '' },
  })
}

function mountView(
  overrides: Partial<InstanceType<typeof LiveChatView>['$props']> = {},
  runtime?: AdminLocaleRuntime,
) {
  const panelStub = { template: '<section><slot name="header" /><slot /></section>' }
  const navbarStub = { props: ['title'], template: '<header>{{ title }}<slot name="leading" /><slot name="right" /></header>' }
  const badgeStub = { template: '<span><slot /></span>' }
  const buttonStub = {
    inheritAttrs: false,
    props: ['label', 'disabled', 'loading'],
    emits: ['click'],
    template: '<button v-bind="$attrs" :disabled="disabled" @click="$emit(\'click\')">{{ label }}<slot /></button>',
  }
  const textareaStub = {
    inheritAttrs: false,
    props: ['modelValue', 'disabled', 'placeholder', 'rows'],
    emits: ['update:modelValue'],
    template: '<textarea v-bind="$attrs" :disabled="disabled" :placeholder="placeholder" :rows="rows" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  }
  const slideoverStub = {
    props: ['open', 'title'],
    emits: ['update:open'],
    template: '<aside v-if="open" data-testid="players-slideover"><h2>{{ title }}</h2><slot name="body" /></aside>',
  }

  return mount(LiveChatView, {
    props: {
      messages,
      channelFilter: 'All',
      snapshotLoading: false,
      connectionStatus: 'live',
      hasGap: false,
      unreadCount: 0,
      draft: '',
      selectedTarget: null,
      onlinePlayers: [eligiblePlayer, unavailablePlayer],
      isSubmitting: false,
      sendError: null,
      sendHistory: [],
      ...overrides,
    },
    global: {
      ...(runtime === undefined ? {} : { plugins: [runtime.i18n] }),
      stubs: {
        Badge: badgeStub,
        Button: buttonStub,
        DashboardNavbar: navbarStub,
        DashboardPanel: panelStub,
        DashboardSidebarCollapse: true,
        Slideover: slideoverStub,
        Textarea: textareaStub,
        UDashboardPanel: panelStub,
        UDashboardNavbar: navbarStub,
        UDashboardSidebarCollapse: true,
        UBadge: badgeStub,
        UButton: buttonStub,
        UTextarea: textareaStub,
        USlideover: slideoverStub,
      },
    },
  })
}

describe('liveChatView', () => {
  it('reactively localizes live status, channels, sources and controls', async () => {
    const runtime = localeRuntime('zh-CN')
    const wrapper = mountView({}, runtime)

    expect(wrapper.text()).toContain('实时聊天')
    expect(wrapper.text()).toContain('在线玩家')
    expect(wrapper.text()).toContain('已缓冲 2 条')
    expect(wrapper.text()).toContain('全局')
    expect(wrapper.text()).toContain('管理员')

    runtime.setLocale('en')
    await nextTick()

    expect(wrapper.text()).toContain('Live chat')
    expect(wrapper.text()).toContain('Online players')
    expect(wrapper.text()).toContain('2 buffered')
    expect(wrapper.text()).toContain('Global')
    expect(wrapper.text()).toContain('Administrator')
    expect(wrapper.text()).not.toContain('实时聊天')
    runtime.dispose()
  })

  it('renders message content as escaped plain text and filters channels from the controlled contract', async () => {
    const wrapper = mountView()

    expect(wrapper.get('[data-testid="chat-message-1"]').text()).toContain('<img src=x onerror="alert(1)">hello')
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.findAll('li[data-testid^="chat-message-"]')).toHaveLength(2)

    await wrapper.get('[data-testid="chat-filter-Global"]').trigger('click')
    expect(wrapper.emitted('updateChannelFilter')).toEqual([['Global']])
    await wrapper.setProps({ channelFilter: 'Global' })

    expect(wrapper.findAll('li[data-testid^="chat-message-"]')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('party line')
  })

  it('keeps gap and send failures outside the message log and retains the controlled draft', () => {
    const wrapper = mountView({
      hasGap: true,
      draft: 'retry this message',
      sendError: 'Could not send. Try again.',
    })

    expect(wrapper.get('[data-testid="chat-gap"]').text()).toContain('部分实时消息可能缺失')
    expect(wrapper.get('[data-testid="chat-send-error"]').text()).toContain('Could not send')
    const composer = wrapper.get('[data-testid="chat-composer-input"]')
    expect(composer.element).toHaveProperty('value', 'retry this message')
    expect(composer.attributes()).toMatchObject({
      'aria-label': '聊天消息',
      'id': 'live-chat-message',
      'name': 'live-chat-message',
    })
    expect(wrapper.get('[data-testid="chat-message-viewport"]').text()).not.toContain('Could not send')
    expect(wrapper.get('[data-testid="chat-message-viewport"]').text()).not.toContain('Some live messages may be missing')
  })

  it('selects only players with a stable cross-platform identity in the desktop sidebar and narrow-screen slideover', async () => {
    const wrapper = mountView()
    const unavailableButtons = wrapper.findAll('[data-testid="chat-player-9"]')

    expect(unavailableButtons).toHaveLength(1)
    expect(unavailableButtons[0]?.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('EOS_player-7')

    await wrapper.get('[data-testid="chat-player-7"]').trigger('click')
    expect(wrapper.emitted('selectTarget')).toEqual([[eligiblePlayer]])

    await wrapper.get('[data-testid="open-online-players"]').trigger('click')
    expect(wrapper.get('[data-testid="players-slideover"]').text()).toContain('Alice')
    expect(wrapper.findAll('[data-testid="chat-player-9"]')).toHaveLength(2)
  })

  it('submits on Enter, preserves newline behavior on Shift+Enter, and navigates send history with arrow keys', async () => {
    const wrapper = mountView({ draft: 'hello', sendHistory: ['first', 'second'] })
    const composer = wrapper.get('[data-testid="chat-composer-input"]')

    await composer.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('submit')).toHaveLength(1)

    await composer.trigger('keydown', { key: 'Enter', shiftKey: true })
    expect(wrapper.emitted('submit')).toHaveLength(1)

    await composer.trigger('keydown', { key: 'ArrowUp' })
    await composer.trigger('keydown', { key: 'ArrowDown' })
    expect(wrapper.emitted('navigateHistory')).toEqual([[-1], [1]])

    await composer.setValue('updated')
    expect(wrapper.emitted('updateDraft')).toContainEqual(['updated'])
  })

  it('does not steal scroll while reading older messages and returns to the latest message on request', async () => {
    const wrapper = mountView({ messages: [messages[0]!] })
    const viewport = wrapper.get('[data-testid="chat-message-viewport"]')
    Object.defineProperties(viewport.element, {
      clientHeight: { configurable: true, value: 100 },
      scrollHeight: { configurable: true, value: 500 },
    })
    viewport.element.scrollTop = 100
    await viewport.trigger('scroll')

    const followingEvents = wrapper.emitted('updateFollowingLatest') ?? []
    expect(followingEvents[followingEvents.length - 1]).toEqual([false])
    await wrapper.setProps({ messages, unreadCount: 1 })
    expect(viewport.element.scrollTop).toBe(100)

    await wrapper.get('[data-testid="chat-unread"]').trigger('click')
    expect(viewport.element.scrollTop).toBe(500)
    const updatedFollowingEvents = wrapper.emitted('updateFollowingLatest') ?? []
    expect(updatedFollowingEvents[updatedFollowingEvents.length - 1]).toEqual([true])
  })
})
