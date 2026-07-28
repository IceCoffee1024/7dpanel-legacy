import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import { createAdminI18n } from '../../../app/i18n'
import {
  chatSourceOptions,
  chatTypeOptions,
  createEmptyHistoryFilters,
  normalizeChatColor,
  playerColorTagPermissionOptions,
  renderColoredChatName,
} from '../model/gameChatManagement'
import ChatHistoryView from './ChatHistoryView.vue'
import ChatSettingsView from './ChatSettingsView.vue'
import ColoredChatPreview from './ColoredChatPreview.vue'
import ColoredChatProfileDialog from './ColoredChatProfileDialog.vue'
import ColoredChatView from './ColoredChatView.vue'

const formStub = {
  props: ['state'],
  emits: ['submit'],
  template: '<form @submit.prevent="$emit(\'submit\', { data: state })"><slot /></form>',
}

const formFieldStub = {
  props: ['label', 'name', 'description', 'hint'],
  template: '<label><span>{{ label }}</span><slot /><small>{{ description }}</small><small>{{ hint }}</small></label>',
}

const inputStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled', 'type', 'min', 'max'],
  emits: ['update:modelValue'],
  template: '<input v-bind="$attrs" :type="type || \'text\'" :value="modelValue" :disabled="disabled" :min="min" :max="max" @input="$emit(\'update:modelValue\', type === \'number\' ? Number($event.target.value) : $event.target.value)">',
}

const inputNumberStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled', 'min', 'max'],
  emits: ['update:modelValue'],
  template: '<input v-bind="$attrs" type="number" :value="modelValue" :disabled="disabled" :min="min" :max="max" @input="$emit(\'update:modelValue\', Number($event.target.value))">',
}

const textareaStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template: '<textarea v-bind="$attrs" :value="modelValue" :disabled="disabled" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const selectStub = {
  inheritAttrs: false,
  props: ['modelValue', 'items', 'disabled'],
  emits: ['update:modelValue'],
  template: '<select v-bind="$attrs" :value="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', $event.target.value)"><option v-for="item in items" :key="item.value ?? item" :value="item.value ?? item">{{ item.label ?? item }}</option></select>',
}

const inputTagsStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template: '<input v-bind="$attrs" :value="modelValue.join(\',\')" :disabled="disabled" @input="$emit(\'update:modelValue\', $event.target.value.split(\',\').filter(Boolean))">',
}

const checkboxStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled', 'label'],
  emits: ['update:modelValue'],
  template: '<label><input v-bind="$attrs" type="checkbox" :checked="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', $event.target.checked)">{{ label }}</label>',
}

const buttonStub = {
  inheritAttrs: false,
  props: ['label', 'disabled', 'type'],
  emits: ['click'],
  template: '<button v-bind="$attrs" :type="type || \'button\'" :disabled="disabled" @click="$emit(\'click\')">{{ label }}<slot /></button>',
}

const modalStub = {
  props: ['open', 'title', 'description'],
  emits: ['update:open'],
  template: '<section v-if="open" role="dialog"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="body" /><slot name="footer" /></section>',
}

const tabsStub = {
  props: ['items', 'modelValue'],
  emits: ['update:modelValue'],
  template: '<section><nav><button v-for="item in items" :key="item.value" :data-tab="item.value" @click="$emit(\'update:modelValue\', item.value)">{{ item.label }}</button></nav><slot name="profiles" /><slot name="defaults" /></section>',
}

const colorPickerStub = {
  inheritAttrs: false,
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template: '<input v-bind="$attrs" data-color-picker type="color" :value="modelValue || \'#FFFFFF\'" :disabled="disabled" @input="$emit(\'update:modelValue\', $event.target.value)">',
}

const commonStubs = {
  Alert: { props: ['title', 'description'], template: '<div role="alert"><strong>{{ title }}</strong>{{ description }}<slot /></div>' },
  Badge: { template: '<span><slot /></span>' },
  Button: buttonStub,
  Checkbox: checkboxStub,
  ColorPicker: colorPickerStub,
  Form: formStub,
  FormField: formFieldStub,
  Input: inputStub,
  InputNumber: inputNumberStub,
  InputTags: inputTagsStub,
  Modal: modalStub,
  Select: selectStub,
  Skeleton: { template: '<div />' },
  Switch: checkboxStub,
  Table: { template: '<div data-testid="desktop-history-table"><slot /></div>' },
  Tabs: tabsStub,
  Textarea: textareaStub,
  UAlert: { props: ['title', 'description'], template: '<div role="alert"><strong>{{ title }}</strong>{{ description }}<slot /></div>' },
  UBadge: { template: '<span><slot /></span>' },
  UButton: buttonStub,
  UCheckbox: checkboxStub,
  UColorPicker: colorPickerStub,
  UForm: formStub,
  UFormField: formFieldStub,
  UInput: inputStub,
  UInputNumber: inputNumberStub,
  UInputTags: inputTagsStub,
  UModal: modalStub,
  USelect: selectStub,
  USkeleton: { template: '<div />' },
  USwitch: checkboxStub,
  UTable: { template: '<div data-testid="desktop-history-table"><slot /></div>' },
  UTabs: tabsStub,
  UTextarea: textareaStub,
}

const historyMessage = {
  sequence: 12,
  occurredAtUtc: '2026-07-26T08:30:00Z',
  entityId: 42,
  crossplatformId: 'EOS_abc123',
  senderName: 'Ada',
  chatType: 'Global' as const,
  sourceKind: 'Player' as const,
  message: '<img src=x onerror=alert(1)> hello',
}

const chatSettings = {
  isEnabled: true,
  globalServerName: 'Server',
  whisperServerName: 'Operator',
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
  whisperDefaultColor: 'B9D7FF',
  friendsDefaultColor: null,
  partyDefaultColor: '88CC88',
  adminDefaultColor: 'FFAA00',
  systemDefaultColor: 'AAAAAA',
  playerColorTagPermission: 'AdminOnly' as const,
}

const profile = {
  crossplatformId: 'EOS_player_1',
  customName: '[{chatType}] {playerName}',
  nameColor: '00FF00',
  textColor: 'FFFFFF',
  description: 'VIP player',
  createdAtUtc: '2026-07-25T08:00:00Z',
  updatedAtUtc: '2026-07-26T08:00:00Z',
}

describe('game chat management model', () => {
  it('keeps translatable option labels out of the domain model', () => {
    expect(chatTypeOptions).toEqual(['Global', 'Friends', 'Party', 'Whisper', 'Unknown'])
    expect(chatSourceOptions).toEqual(['Player', 'Administrator', 'System'])
    expect(playerColorTagPermissionOptions).toEqual(['None', 'AdminOnly', 'All'])
  })

  it('normalizes optional six-digit RGB values and rejects other input', () => {
    expect(normalizeChatColor(' #a0B1c2 ')).toBe('A0B1C2')
    expect(normalizeChatColor('')).toBeNull()
    expect(normalizeChatColor('#12345')).toBeUndefined()
    expect(normalizeChatColor('url(red)')).toBeUndefined()
  })

  it('renders only the four approved variables case-insensitively', () => {
    expect(renderColoredChatName(
      '{PLAYERNAME}/{playerId}/{entityId}/{chatType}/{unknown}',
      { playerName: 'Ada', playerId: 'EOS_1', entityId: 42, chatType: 'Global' },
    )).toBe('Ada/EOS_1/42/Global/{unknown}')
  })
})

describe('ChatHistoryView', () => {
  it('renders every approved field in desktop/mobile content as plain text', () => {
    const wrapper = mount(ChatHistoryView, {
      props: {
        state: 'ready',
        messages: [historyMessage],
        filters: createEmptyHistoryFilters(),
        nextCursor: 'next',
        isLoadingMore: false,
      },
      global: { stubs: commonStubs },
    })

    expect(wrapper.text()).toContain('Ada')
    expect(wrapper.text()).toContain('EOS_abc123')
    expect(wrapper.text()).toContain('42')
    expect(wrapper.text()).toContain('全局')
    expect(wrapper.text()).toContain('玩家')
    expect(wrapper.text()).toContain('<img src=x onerror=alert(1)> hello')
    expect(wrapper.find('img').exists()).toBe(false)
  })

  it('emits a trimmed filter contract, load-more intent and stale state', async () => {
    const wrapper = mount(ChatHistoryView, {
      props: {
        state: 'stale',
        messages: [historyMessage],
        filters: createEmptyHistoryFilters(),
        nextCursor: 'next',
        isLoadingMore: false,
      },
      global: { stubs: commonStubs },
    })

    await wrapper.get('[data-testid="history-crossplatform-id"]').setValue('  EOS_1  ')
    await wrapper.get('[data-testid="history-sender-name"]').setValue('  Ada  ')
    await wrapper.get('form').trigger('submit')
    await wrapper.get('[data-testid="history-load-more"]').trigger('click')

    expect(wrapper.text()).toContain('当前显示上次成功结果')
    expect(wrapper.emitted('applyFilters')).toEqual([[expect.objectContaining({ crossplatformId: 'EOS_1', senderName: 'Ada' })]])
    expect(wrapper.emitted('loadMore')).toHaveLength(1)
  })
})

describe('ChatSettingsView', () => {
  it('uses the approved fields, explains retention semantics and emits validated settings', async () => {
    const wrapper = mount(ChatSettingsView, {
      props: { settings: chatSettings, isSaving: false, isResetting: false },
      global: { stubs: commonStubs },
    })

    expect(wrapper.text()).toContain('关闭聊天功能不会删除已有历史')
    expect(wrapper.text()).toContain('0 表示不自动清理')
    await wrapper.get('[data-testid="history-retention-days"]').setValue('0')
    await wrapper.get('[data-testid="command-prefixes"]').setValue('/,!,!')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('save')).toEqual([[expect.objectContaining({
      commandPrefixes: ['/', '!'],
      historyRetentionDays: 0,
    })]])
  })

  it('blocks invalid retention/prefix values and exposes reset intent', async () => {
    const wrapper = mount(ChatSettingsView, {
      props: { settings: chatSettings, isSaving: false, isResetting: false },
      global: { stubs: commonStubs },
    })

    await wrapper.get('[data-testid="history-retention-days"]').setValue('3651')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('save')).toBeUndefined()
    expect(wrapper.text()).toContain('历史保留天数必须是 0 到 3650 之间的整数')

    await wrapper.get('[data-testid="history-retention-days"]').setValue('30')
    await wrapper.get('[data-testid="command-prefixes"]').setValue('/,long')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.text()).toContain('每个命令前缀必须是一个非空白字符')

    await wrapper.get('[data-testid="reset-chat-settings"]').trigger('click')
    expect(wrapper.emitted('reset')).toHaveLength(1)
  })
})

describe('ColoredChatPreview', () => {
  it('renders template and message as plain text with controlled color styles', () => {
    const wrapper = mount(ColoredChatPreview, {
      props: {
        customName: '<b>{playerName}</b> {unknown}',
        nameColor: '00ff00',
        textColor: 'ff00aa',
        message: '<script>alert(1)</script>',
        context: { playerName: 'Ada', playerId: 'EOS_1', entityId: 7, chatType: 'Party' },
      },
    })

    expect(wrapper.text()).toContain('<b>Ada</b> {unknown}')
    expect(wrapper.text()).toContain('<script>alert(1)</script>')
    expect(wrapper.find('b').exists()).toBe(false)
    expect(wrapper.find('script').exists()).toBe(false)
    expect(wrapper.get('[data-testid="preview-name"]').attributes('style')).toContain('color: #00FF00')
    expect(wrapper.get('[data-testid="preview-message"]').attributes('style')).toContain('color: #FF00AA')
  })
})

describe('ColoredChatProfileDialog', () => {
  it('inserts all four approved variables and normalizes colors on create', async () => {
    const wrapper = mount(ColoredChatProfileDialog, {
      props: { open: true, mode: 'create', profile: null, isSubmitting: false },
      global: { stubs: commonStubs },
    })

    await wrapper.get('[data-testid="profile-id"]').setValue('EOS_new')
    for (const variable of ['playerName', 'playerId', 'entityId', 'chatType'])
      await wrapper.get(`[data-testid="insert-${variable}"]`).trigger('click')
    await wrapper.get('[data-testid="profile-name-color"]').setValue('#aabbcc')
    await wrapper.get('[data-testid="profile-text-color"]').setValue('112233')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('submit')).toEqual([[{
      crossplatformId: 'EOS_new',
      customName: '{playerName}{playerId}{entityId}{chatType}',
      nameColor: 'AABBCC',
      textColor: '112233',
      description: null,
    }]])
  })

  it('keeps the business key immutable while editing', () => {
    const wrapper = mount(ColoredChatProfileDialog, {
      props: { open: true, mode: 'edit', profile, isSubmitting: false },
      global: { stubs: commonStubs },
    })

    expect(wrapper.get('[data-testid="profile-id"]').attributes()).toHaveProperty('disabled')
    expect(wrapper.get('[data-testid="profile-id"]').element).toHaveProperty('value', 'EOS_player_1')
  })
})

describe('ColoredChatView', () => {
  it('renders profile/default tabs, profile filters and cursor actions', async () => {
    const wrapper = mount(ColoredChatView, {
      props: {
        profiles: [profile],
        profilesState: 'ready',
        profileFilter: '',
        nextCursor: 'next',
        settings: coloredSettings,
        isSavingSettings: false,
        isResettingSettings: false,
        isMutatingProfile: false,
      },
      global: { stubs: commonStubs },
    })

    expect(wrapper.text()).toContain('玩家 Profile')
    expect(wrapper.text()).toContain('默认设置')
    expect(wrapper.text()).toContain('EOS_player_1')
    await wrapper.get('[data-testid="profile-filter"]').setValue(' Ada ')
    await wrapper.get('[data-testid="apply-profile-filter"]').trigger('click')
    await wrapper.get('[data-testid="profiles-load-more"]').trigger('click')

    expect(wrapper.emitted('filterProfiles')).toEqual([['Ada']])
    expect(wrapper.emitted('loadMoreProfiles')).toHaveLength(1)
  })

  it('uses six color pickers and emits normalized default settings', async () => {
    const wrapper = mount(ColoredChatView, {
      props: {
        profiles: [],
        profilesState: 'empty',
        profileFilter: '',
        nextCursor: null,
        settings: coloredSettings,
        isSavingSettings: false,
        isResettingSettings: false,
        isMutatingProfile: false,
      },
      global: { stubs: commonStubs },
    })

    expect(wrapper.findAll('[data-color-picker]')).toHaveLength(6)
    await wrapper.get('[data-testid="global-default-color-input"]').setValue('#abcdef')
    await wrapper.get('[data-testid="colored-settings-form"]').trigger('submit')

    expect(wrapper.emitted('saveSettings')).toEqual([[expect.objectContaining({ globalDefaultColor: 'ABCDEF' })]])
  })

  it('confirms deletion without optimistically removing the profile', async () => {
    const wrapper = mount(ColoredChatView, {
      props: {
        profiles: [profile],
        profilesState: 'ready',
        profileFilter: '',
        nextCursor: null,
        settings: coloredSettings,
        isSavingSettings: false,
        isResettingSettings: false,
        isMutatingProfile: false,
      },
      global: { stubs: commonStubs },
    })

    await wrapper.get('[data-testid="delete-profile-EOS_player_1"]').trigger('click')
    expect(wrapper.text()).toContain('删除玩家 Profile')
    expect(wrapper.text()).toContain('EOS_player_1')
    await wrapper.get('[data-testid="confirm-delete-profile"]').trigger('click')

    expect(wrapper.emitted('deleteProfile')).toEqual([['EOS_player_1']])
    expect(wrapper.text()).toContain('EOS_player_1')
  })
})

describe('game chat locale coverage', () => {
  it('renders all management pages in English without falling back to Chinese', () => {
    const runtime = createAdminI18n({
      repository: {
        restore: () => 'en',
        save: () => true,
        subscribe: () => () => {},
      },
      documentElement: { lang: '' },
    })
    const global = { plugins: [runtime.i18n], stubs: commonStubs }

    const history = mount(ChatHistoryView, {
      props: {
        state: 'ready',
        messages: [historyMessage],
        filters: createEmptyHistoryFilters(),
        nextCursor: null,
        isLoadingMore: false,
      },
      global,
    })
    const settings = mount(ChatSettingsView, {
      props: {
        settings: chatSettings,
        isSaving: false,
        isResetting: false,
        feedbackMessage: 'gameChat.feedback.settingsOperationFailed',
      },
      global,
    })
    const colored = mount(ColoredChatView, {
      props: {
        profiles: [profile],
        profilesState: 'ready',
        profileFilter: '',
        nextCursor: null,
        settings: coloredSettings,
        isSavingSettings: false,
        isResettingSettings: false,
        isMutatingProfile: false,
      },
      global,
    })

    expect(history.text()).toContain('Chat history')
    expect(history.text()).toContain('Apply filters')
    expect(settings.text()).toContain('Chat settings')
    expect(settings.text()).toContain('Failed to update chat settings. Try again.')
    expect(settings.text()).toContain('Save chat settings')
    expect(colored.text()).toContain('Colored chat')
    expect(colored.text()).toContain('Player profiles')
    expect(colored.text()).toContain('Default settings')
    expect(`${history.text()}${settings.text()}${colored.text()}`).not.toMatch(/[\u3400-\u9FFF]/)
    runtime.dispose()
  })
})
