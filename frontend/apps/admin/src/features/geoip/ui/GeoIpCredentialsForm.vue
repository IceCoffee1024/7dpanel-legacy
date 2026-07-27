<script setup lang="ts">
import type { GeoIpCredentials, GeoIpCredentialsDraft, GeoIpSecretOperation } from '../api/geoip'
import type { GeoIpCredentialsState } from '../model/useGeoIp'

import { computed, onUnmounted, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

type CredentialOperation = GeoIpSecretOperation['operation']

const props = defineProps<{
  credentials: GeoIpCredentials | null
  credentialsState: GeoIpCredentialsState
  disabled: boolean
}>()
const emit = defineEmits<{ submit: [draft: GeoIpCredentialsDraft] }>()
const { t } = useI18n()
const form = reactive({ accountIdOperation: 'Keep' as CredentialOperation, accountIdValue: '', licenseKeyOperation: 'Keep' as CredentialOperation, licenseKeyValue: '' })
const operationItems = computed(() => [
  { label: t('geoIp.credentials.operation.Keep'), value: 'Keep' },
  { label: t('geoIp.credentials.operation.Replace'), value: 'Replace' },
  { label: t('geoIp.credentials.operation.Clear'), value: 'Clear' },
])
const invalid = computed(() => (form.accountIdOperation === 'Replace' && !form.accountIdValue.trim()) || (form.licenseKeyOperation === 'Replace' && !form.licenseKeyValue.trim()))

watch(() => form.accountIdOperation, (operation) => { if (operation !== 'Replace') form.accountIdValue = '' })
watch(() => form.licenseKeyOperation, (operation) => { if (operation !== 'Replace') form.licenseKeyValue = '' })

function clearReplacementValues() {
  form.accountIdValue = ''
  form.licenseKeyValue = ''
}
function operation(value: CredentialOperation, replacement: string): GeoIpSecretOperation | null {
  if (value === 'Keep') return { operation: 'Keep' }
  if (value === 'Clear') return { operation: 'Clear' }
  const normalized = replacement.trim()
  return normalized ? { operation: 'Replace', value: normalized } : null
}
function submit() {
  const accountId = operation(form.accountIdOperation, form.accountIdValue)
  const licenseKey = operation(form.licenseKeyOperation, form.licenseKeyValue)
  if (accountId === null || licenseKey === null) return
  const draft = Object.freeze({ accountId, licenseKey })
  clearReplacementValues()
  emit('submit', draft)
}
function formatUpdatedAt(value: string | null) {
  return value === null ? t('geoIp.common.none') : new Date(value).toLocaleString()
}

onUnmounted(clearReplacementValues)
</script>

<template>
  <UCard data-testid="geoip-credentials">
    <template #header>
      <div>
        <h2 class="font-semibold">{{ t('geoIp.credentials.title') }}</h2>
        <p class="text-sm text-muted">{{ t('geoIp.credentials.description') }}</p>
      </div>
    </template>

    <UAlert v-if="props.credentialsState === 'unavailable'" color="warning" :title="t('geoIp.credentials.unavailableTitle')" :description="t('geoIp.credentials.unavailableDescription')" />
    <div v-else-if="props.credentialsState === 'loading'" class="text-sm text-muted">{{ t('geoIp.common.loading') }}</div>
    <UForm v-else-if="props.credentials" data-testid="geoip-credentials-form" class="space-y-5" :state="form" @submit="submit">
      <section class="space-y-3 rounded-lg border border-default p-4">
        <div>
          <h3 class="font-medium">{{ t('geoIp.credentials.accountId.title') }}</h3>
          <p class="text-sm text-muted">{{ t('geoIp.credentials.status', { status: t(props.credentials.accountId.isSet ? 'geoIp.credentials.set' : 'geoIp.credentials.notSet') }) }}</p>
          <p v-if="props.credentials.accountId.fingerprint" class="text-xs text-muted">{{ t('geoIp.credentials.fingerprint', { fingerprint: props.credentials.accountId.fingerprint }) }}</p>
          <p class="text-xs text-muted">{{ t('geoIp.credentials.updatedAt', { updatedAt: formatUpdatedAt(props.credentials.accountId.updatedAtUtc) }) }}</p>
        </div>
        <UFormField :label="t('geoIp.credentials.action')">
          <USelect v-model="form.accountIdOperation" data-testid="geoip-account-id-operation" :disabled="props.disabled" :items="operationItems" />
        </UFormField>
        <UFormField v-if="form.accountIdOperation === 'Replace'" :label="t('geoIp.credentials.accountId.value')" required>
          <UInput v-model="form.accountIdValue" data-testid="geoip-account-id-value" autocomplete="new-password" :disabled="props.disabled" type="password" />
        </UFormField>
      </section>

      <section class="space-y-3 rounded-lg border border-default p-4">
        <div>
          <h3 class="font-medium">{{ t('geoIp.credentials.licenseKey.title') }}</h3>
          <p class="text-sm text-muted">{{ t('geoIp.credentials.status', { status: t(props.credentials.licenseKey.isSet ? 'geoIp.credentials.set' : 'geoIp.credentials.notSet') }) }}</p>
          <p v-if="props.credentials.licenseKey.fingerprint" class="text-xs text-muted">{{ t('geoIp.credentials.fingerprint', { fingerprint: props.credentials.licenseKey.fingerprint }) }}</p>
          <p class="text-xs text-muted">{{ t('geoIp.credentials.updatedAt', { updatedAt: formatUpdatedAt(props.credentials.licenseKey.updatedAtUtc) }) }}</p>
        </div>
        <UFormField :label="t('geoIp.credentials.action')">
          <USelect v-model="form.licenseKeyOperation" data-testid="geoip-license-key-operation" :disabled="props.disabled" :items="operationItems" />
        </UFormField>
        <UFormField v-if="form.licenseKeyOperation === 'Replace'" :label="t('geoIp.credentials.licenseKey.value')" required>
          <UInput v-model="form.licenseKeyValue" data-testid="geoip-license-key-value" autocomplete="new-password" :disabled="props.disabled" type="password" />
        </UFormField>
      </section>

      <div class="flex justify-end">
        <UButton :disabled="props.disabled || invalid" :label="t('geoIp.credentials.save')" :loading="props.disabled" type="submit" />
      </div>
    </UForm>
  </UCard>
</template>
