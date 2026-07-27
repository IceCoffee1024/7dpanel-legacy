import type { GeoIpController } from '../model/useGeoIp'

import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import GeoIpView from './GeoIpView.vue'

const policy = Object.freeze({ version: 1, isEnabled: true, provider: 'LocalMmdb', failureMode: 'FailOpen', bypassAdmins: true, rejectionMessage: 'Denied', networkRules: [], countryRules: [], cacheHealth: { queueDepth: 0, rejectedRefreshCount: 0, lastCompletedAtUtc: null, lastLookupStatus: null, severity: 'Information', statusCode: 'ready' }, providers: [], recentDecisions: [] })
const credentials = Object.freeze({ accountId: Object.freeze({ isSet: false, fingerprint: null, updatedAtUtc: null }), licenseKey: Object.freeze({ isSet: false, fingerprint: null, updatedAtUtc: null }) })

const stubs = {
  Alert: { template: '<div><slot name="actions" /><slot /></div>' },
  Badge: { template: '<span><slot /></span>' },
  Button: { props: ['label', 'type'], emits: ['click'], template: '<button :type="type" @click="$emit(\'click\', $event)"><slot />{{ label }}</button>' },
  Card: { template: '<section><slot name="header" /><slot /></section>' },
  DashboardPanel: { template: '<div><slot name="header" /><slot name="body" /></div>' },
  Container: { template: '<div><slot /></div>' },
  Form: { emits: ['submit'], template: '<form v-bind="$attrs" @submit.prevent="$emit(\'submit\')"><slot /></form>' },
  FormField: { template: '<label><slot /></label>' },
  Input: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  Select: { props: ['items', 'modelValue'], emits: ['update:modelValue'], template: '<select v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option v-for="item in items" :key="item.value" :value="item.value">{{ item.label }}</option></select>' },
}

function controller(updateCredentials: ReturnType<typeof vi.fn>): GeoIpController {
  return {
    state: shallowRef('ready'), policy: shallowRef(policy), diagnostics: shallowRef(null), diagnosticsState: shallowRef('ready'), testResult: shallowRef(null), isMutating: shallowRef(false), errorCode: shallowRef(null), credentials: shallowRef(credentials), credentialsState: shallowRef('ready'), refresh: vi.fn(), save: vi.fn(), test: vi.fn(), updateCredentials, dispose: vi.fn(),
  } as unknown as GeoIpController
}

describe('GeoIpView credentials', () => {
  it('forwards both intents and immediately clears the local replacement input', async () => {
    const updateCredentials = vi.fn().mockResolvedValue(true)
    const wrapper = mount(GeoIpView, {
      props: { controller: controller(updateCredentials) },
      shallow: true,
      global: {
        plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: {} }, missingWarn: false, fallbackWarn: false })],
        stubs: { ...stubs, GeoIpCredentialsForm: false },
      },
    })
    const replacement = '12345'

    await wrapper.get('[data-testid="geoip-account-id-operation"]').setValue('Replace')
    await wrapper.get('[data-testid="geoip-account-id-value"]').setValue(replacement)
    await wrapper.get('[data-testid="geoip-license-key-operation"]').setValue('Clear')
    await wrapper.get('form[data-testid="geoip-credentials-form"]').trigger('submit')

    expect(updateCredentials).toHaveBeenCalledWith({ accountId: { operation: 'Replace', value: replacement }, licenseKey: { operation: 'Clear' } })
    expect((wrapper.get('[data-testid="geoip-account-id-value"]').element as HTMLInputElement).value).toBe('')
  })
})
