import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import DiscordView from './DiscordView.vue'

function controller() {
  return {
    state: readonly(shallowRef('ready')),
    configuration: readonly(shallowRef({
      version: 3,
      isEnabled: true,
      mode: 'Bot',
      applicationId: 'app',
      guildId: 'guild',
      publicChannelId: 'channel',
      bridgeGameToDiscord: true,
      bridgeDiscordToGame: true,
      proxy: { isEnabled: true, endpoint: 'https://proxy.example', hasCredentials: true },
      hasBotToken: true,
      targets: [{ targetKey: 'public', deliveryMode: 'Webhook', channelId: 'channel', isEnabled: true, hasCredential: true }],
      updatedAtUtc: '2026-07-27T00:00:00Z',
    })),
    health: readonly(shallowRef({ gateway: { state: 'Degraded', errorCode: 'gateway_reconnecting', observedAtUtc: null }, inbound: { state: 'Unavailable', errorCode: 'signature_transport_missing', observedAtUtc: null } })),
    healthState: readonly(shallowRef('ready')),
    deliveries: readonly(shallowRef(Object.freeze([{ deliveryId: 'delivery-1', businessKey: 'business-1', targetKey: 'public', status: 'ResultUnknown', nextAttemptAtUtc: null, retryCount: 3, createdAtUtc: '2026-07-27T00:00:00Z', completedAtUtc: null }]))),
    deliveryState: readonly(shallowRef('ready')),
    bindings: readonly(shallowRef(Object.freeze([{ discordSubject: 'discord-user', crossplatformId: 'EOS_player', isActive: true, createdAtUtc: '2026-07-27T00:00:00Z', updatedAtUtc: '2026-07-27T00:00:00Z' }]))),
    bindingState: readonly(shallowRef('ready')),
    commands: readonly(shallowRef(Object.freeze([{ commandKey: 'serverstatus', isEnabled: true, remoteAllowed: true }]))),
    commandState: readonly(shallowRef('ready')),
    isMutating: readonly(shallowRef(false)),
    errorCode: readonly(shallowRef(null)),
    lastDelivery: readonly(shallowRef(null)),
    bindingCode: readonly(shallowRef(null)),
    refresh: vi.fn(),
    save: vi.fn(),
    updateSecret: vi.fn().mockResolvedValue(true),
    testDelivery: vi.fn(),
    retryDelivery: vi.fn(),
    createBindingCode: vi.fn(),
    removeBinding: vi.fn(),
    clearBindingCode: vi.fn(),
    dispose: vi.fn(),
  }
}

const stubs = {
  DashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' },
  UDashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' },
  DashboardNavbar: { template: '<header><slot name="leading"/><slot name="right"/></header>' },
  UDashboardNavbar: { template: '<header><slot name="leading"/><slot name="right"/></header>' },
  DashboardSidebarCollapse: true,
  UDashboardSidebarCollapse: true,
  Container: { template: '<div><slot/></div>' },
  UContainer: { template: '<div><slot/></div>' },
  Card: { template: '<section><slot name="header"/><slot/></section>' },
  UCard: { template: '<section><slot name="header"/><slot/></section>' },
  Alert: { props: ['title', 'description'], template: '<div role="alert">{{ title }} {{ description }}<slot name="description"/><slot name="actions"/></div>' },
  UAlert: { props: ['title', 'description'], template: '<div role="alert">{{ title }} {{ description }}<slot name="description"/><slot name="actions"/></div>' },
  Badge: { props: ['label'], template: '<span>{{ label }}<slot/></span>' },
  UBadge: { props: ['label'], template: '<span>{{ label }}<slot/></span>' },
  Button: { props: ['label', 'disabled', 'type'], emits: ['click'], template: '<button :disabled="disabled" :type="type" @click="$emit(\'click\')">{{ label }}<slot/></button>' },
  UButton: { props: ['label', 'disabled', 'type'], emits: ['click'], template: '<button :disabled="disabled" :type="type" @click="$emit(\'click\')">{{ label }}<slot/></button>' },
  Form: { template: '<form @submit.prevent="$emit(\'submit\')"><slot/></form>' },
  UForm: { template: '<form @submit.prevent="$emit(\'submit\')"><slot/></form>' },
  FormField: { props: ['label'], template: '<label>{{ label }}<slot/></label>' },
  UFormField: { props: ['label'], template: '<label>{{ label }}<slot/></label>' },
  Input: { props: ['modelValue', 'type'], emits: ['update:modelValue'], template: '<input :type="type" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  UInput: { props: ['modelValue', 'type'], emits: ['update:modelValue'], template: '<input :type="type" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  Select: true,
  USelect: true,
  Switch: true,
  USwitch: true,
  Skeleton: true,
  USkeleton: true,
  Table: { template: '<table><slot name="empty"/></table>' },
  UTable: { template: '<table><slot name="empty"/></table>' },
  Checkbox: true,
  UCheckbox: true,
}

describe('discordView', () => {
  it('provides a submission-only Interaction public key and inbound transport diagnostics', async () => {
    const value = controller()
    const wrapper = mount(DiscordView, { props: { controller: value as never }, global: { stubs } })

    const inbound = wrapper.get('[data-testid="discord-inbound-transport"]')
    expect(inbound.text()).toContain('/api/v1/integrations/discord/interactions')
    expect(inbound.text()).toContain('Gateway')
    expect(inbound.text()).toContain('signed HTTP')

    const input = inbound.get('input[type="password"]')
    expect((input.element as HTMLInputElement).value).toBe('')
    await input.setValue('replacement-interaction-public-key')
    await inbound.get('[data-testid="interaction-public-key-apply"]').trigger('click')

    expect(value.updateSecret).toHaveBeenCalledWith('interactionPublicKey', {
      operation: 'Replace',
      value: 'replacement-interaction-public-key',
    })
    expect((input.element as HTMLInputElement).value).toBe('')
    expect(wrapper.html()).not.toContain('existing-interaction-public-key')
  })

  it('never echoes secrets and requires explicit replace or confirmed clear operations', async () => {
    const value = controller()
    const wrapper = mount(DiscordView, { props: { controller: value as never }, global: { stubs } })

    const secret = wrapper.get('[data-testid="discord-secret-bot-token"]')
    const input = secret.get('input[type="password"]')
    expect((input.element as HTMLInputElement).value).toBe('')
    expect(wrapper.html()).not.toContain('existing-token')

    await secret.get('[data-testid="secret-replace"]').trigger('click')
    await input.setValue('replacement-token')
    await secret.get('[data-testid="secret-apply"]').trigger('click')
    expect(value.updateSecret).toHaveBeenCalledWith('botToken', { operation: 'Replace', value: 'replacement-token' })

    await secret.get('[data-testid="secret-clear"]').trigger('click')
    expect(secret.get('[data-testid="secret-apply"]').attributes('disabled')).toBeDefined()
    await secret.get('[data-testid="secret-clear-confirm"]').setValue(true)
    await secret.get('[data-testid="secret-apply"]').trigger('click')
    expect(value.updateSecret).toHaveBeenCalledWith('botToken', { operation: 'Clear' })
  })

  it('shows delivery, binding, command catalog, Gateway, and inbound health honestly', () => {
    const wrapper = mount(DiscordView, { props: { controller: controller() as never }, global: { stubs } })
    expect(wrapper.text()).toContain('ResultUnknown')
    expect(wrapper.text()).toContain('EOS_player')
    expect(wrapper.text()).toContain('serverstatus')
    expect(wrapper.text()).toContain('gateway_reconnecting')
    expect(wrapper.text()).toContain('signature_transport_missing')
  })
})
