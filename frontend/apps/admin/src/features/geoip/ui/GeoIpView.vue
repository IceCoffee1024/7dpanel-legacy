<script setup lang="ts">
import type { GeoIpCredentialsDraft, GeoIpEffect, GeoIpFailureMode, GeoIpPolicyDraft, GeoIpProvider } from '../api/geoip'
import type { GeoIpController } from '../model/useGeoIp'

import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import GeoIpCredentialsForm from './GeoIpCredentialsForm.vue'

const props = defineProps<{ controller: GeoIpController }>()
const { t } = useI18n()
const form = reactive({ isEnabled: false, provider: 'LocalMmdb' as GeoIpProvider, failureMode: 'FailOpen' as GeoIpFailureMode, bypassAdmins: true, rejectionMessage: '', networkRules: [] as Array<{ ruleId: string, networkCidr: string, effect: GeoIpEffect, ordinal: number }>, countryRules: [] as Array<{ countryCode: string, effect: GeoIpEffect }> })
const testForm = reactive({ ipAddress: '' })
const providerItems = computed(() => [
  { label: t('geoIp.provider.LocalMmdb'), value: 'LocalMmdb' },
  { label: t('geoIp.provider.MaxMindWebService'), value: 'MaxMindWebService' },
])
const failureItems = computed(() => [
  { label: t('geoIp.failureMode.FailOpen'), value: 'FailOpen' },
  { label: t('geoIp.failureMode.FailClosed'), value: 'FailClosed' },
])
const effectItems = computed(() => [
  { label: t('geoIp.effect.Allow'), value: 'Allow' },
  { label: t('geoIp.effect.Deny'), value: 'Deny' },
])

watch(() => props.controller.policy.value, (policy) => {
  if (policy === null)
    return
  Object.assign(form, { isEnabled: policy.isEnabled, provider: policy.provider, failureMode: policy.failureMode, bypassAdmins: policy.bypassAdmins, rejectionMessage: policy.rejectionMessage, networkRules: policy.networkRules.map(rule => ({ ...rule })), countryRules: policy.countryRules.map(rule => ({ ...rule })) })
}, { immediate: true })

function addNetworkRule() {
  form.networkRules.push({ ruleId: `network-${form.networkRules.length + 1}`, networkCidr: '', effect: 'Allow', ordinal: form.networkRules.length })
}
function addCountryRule() {
  form.countryRules.push({ countryCode: '', effect: 'Allow' })
}
function draft(): GeoIpPolicyDraft | null {
  const policy = props.controller.policy.value
  if (policy === null)
    return null
  return { expectedVersion: policy.version, isEnabled: form.isEnabled, provider: form.provider, failureMode: form.failureMode, bypassAdmins: form.bypassAdmins, rejectionMessage: form.rejectionMessage.trim(), networkRules: form.networkRules.map((rule, ordinal) => ({ ruleId: rule.ruleId.trim(), networkCidr: rule.networkCidr.trim(), effect: rule.effect, ordinal })), countryRules: form.countryRules.map(rule => ({ countryCode: rule.countryCode.trim().toUpperCase(), effect: rule.effect })) }
}
function save() {
  const value = draft()
  if (value)
    void props.controller.save(value)
}
function updateCredentials(draft: GeoIpCredentialsDraft) {
  void props.controller.updateCredentials(draft)
}
</script>

<template>
  <UDashboardPanel id="geoip-access-policy">
    <template #header>
      <UDashboardNavbar :title="t('geoIp.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template><template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('geoIp.common.refresh')"
            variant="outline"
            :loading="controller.state.value === 'loading'"
            @click="controller.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert
          v-if="controller.policy.value"
          :color="controller.policy.value.failureMode === 'FailOpen' ? 'warning' : 'error'"
          :title="t('geoIp.currentFailureMode', { mode: controller.policy.value.failureMode })"
          :description="t(controller.policy.value.failureMode === 'FailOpen' ? 'geoIp.failureMode.failOpenDescription' : 'geoIp.failureMode.failClosedDescription')"
        />
        <USkeleton v-if="controller.state.value === 'loading'" class="h-48 w-full" />
        <UAlert v-else-if="controller.state.value === 'forbidden'" color="error" :title="t('geoIp.state.forbidden')" />
        <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('geoIp.state.unavailable')">
          <template #actions>
            <UButton
              color="neutral"
              :label="t('geoIp.common.retry')"
              variant="outline"
              @click="controller.refresh"
            />
          </template>
        </UAlert>
        <UAlert v-else-if="controller.state.value === 'stale'" color="warning" :title="t('geoIp.state.stale')" />
        <UAlert
          v-if="controller.errorCode.value"
          color="error"
          :title="t('geoIp.state.operationIncomplete')"
          :description="controller.errorCode.value"
        />

        <template v-if="controller.policy.value">
          <UCard>
            <template #header>
              <div>
                <h2 class="font-semibold">
                  {{ t('geoIp.policy.title') }}
                </h2><p class="text-sm text-muted">
                  {{ t('geoIp.policy.description') }}
                </p>
              </div>
            </template>
            <UForm class="space-y-5" :state="form" @submit="save">
              <div class="grid gap-4 md:grid-cols-2">
                <UFormField :label="t('geoIp.policy.enabled')">
                  <USwitch v-model="form.isEnabled" />
                </UFormField><UFormField :label="t('geoIp.policy.bypassAdmins')">
                  <USwitch v-model="form.bypassAdmins" />
                </UFormField><UFormField :label="t('geoIp.policy.provider')">
                  <USelect v-model="form.provider" :items="providerItems" />
                </UFormField><UFormField :label="t('geoIp.policy.failureMode')">
                  <USelect v-model="form.failureMode" :items="failureItems" />
                </UFormField><UFormField class="md:col-span-2" :label="t('geoIp.policy.rejectionMessage')">
                  <UTextarea v-model="form.rejectionMessage" :maxlength="256" />
                </UFormField>
              </div>

              <section class="space-y-3">
                <div class="flex items-center justify-between">
                  <div>
                    <h3 class="font-medium">
                      {{ t('geoIp.networkRules.title') }}
                    </h3><p class="text-sm text-muted">
                      {{ t('geoIp.networkRules.description') }}
                    </p>
                  </div><UButton
                    color="neutral"
                    icon="i-lucide-plus"
                    :label="t('geoIp.common.add')"
                    size="sm"
                    variant="outline"
                    @click="addNetworkRule"
                  />
                </div><div v-if="form.networkRules.length === 0" class="text-sm text-muted">
                  {{ t('geoIp.networkRules.empty') }}
                </div><div v-for="(rule, index) in form.networkRules" :key="index" class="grid gap-3 rounded-lg border border-default p-3 md:grid-cols-[1fr_1.5fr_9rem_auto]">
                  <UInput v-model="rule.ruleId" :placeholder="t('geoIp.networkRules.ruleIdPlaceholder')" /><UInput v-model="rule.networkCidr" placeholder="203.0.113.0/24" /><USelect v-model="rule.effect" :items="effectItems" /><UButton
                    color="error"
                    icon="i-lucide-trash-2"
                    :label="t('geoIp.common.remove')"
                    variant="ghost"
                    @click="form.networkRules.splice(index, 1)"
                  />
                </div>
              </section>

              <section class="space-y-3">
                <div class="flex items-center justify-between">
                  <div>
                    <h3 class="font-medium">
                      {{ t('geoIp.countryRules.title') }}
                    </h3><p class="text-sm text-muted">
                      {{ t('geoIp.countryRules.description') }}
                    </p>
                  </div><UButton
                    color="neutral"
                    icon="i-lucide-plus"
                    :label="t('geoIp.common.add')"
                    size="sm"
                    variant="outline"
                    @click="addCountryRule"
                  />
                </div><div v-if="form.countryRules.length === 0" class="text-sm text-muted">
                  {{ t('geoIp.countryRules.empty') }}
                </div><div v-for="(rule, index) in form.countryRules" :key="index" class="grid gap-3 rounded-lg border border-default p-3 md:grid-cols-[1fr_9rem_auto]">
                  <UInput v-model="rule.countryCode" maxlength="2" placeholder="CN" /><USelect v-model="rule.effect" :items="effectItems" /><UButton
                    color="error"
                    icon="i-lucide-trash-2"
                    :label="t('geoIp.common.remove')"
                    variant="ghost"
                    @click="form.countryRules.splice(index, 1)"
                  />
                </div>
              </section>
              <div class="flex justify-end">
                <UButton :label="t('geoIp.policy.save')" type="submit" :loading="controller.isMutating.value" />
              </div>
            </UForm>
          </UCard>

          <GeoIpCredentialsForm
            :credentials="controller.credentials.value"
            :credentials-state="controller.credentialsState.value"
            :disabled="controller.isMutating.value || controller.state.value === 'forbidden'"
            @submit="updateCredentials"
          />

          <UCard>
            <template #header>
              <div>
                <h2 class="font-semibold">
                  {{ t('geoIp.test.title') }}
                </h2><p class="text-sm text-muted">
                  {{ t('geoIp.test.description') }}
                </p>
              </div>
            </template><div class="flex flex-col gap-3 sm:flex-row">
              <UInput
                id="geoip-test-ip-address"
                v-model="testForm.ipAddress"
                :aria-label="t('geoIp.test.title')"
                class="min-w-64"
                name="geoip-test-ip-address"
                placeholder="203.0.113.10"
              /><UButton
                :label="t('geoIp.test.submit')"
                :disabled="!testForm.ipAddress.trim()"
                :loading="controller.isMutating.value"
                @click="controller.test(testForm.ipAddress)"
              />
            </div><UAlert
              v-if="controller.testResult.value"
              class="mt-4"
              :color="controller.testResult.value.accepted ? 'success' : 'warning'"
              :title="controller.testResult.value.state"
              :description="controller.testResult.value.maskedIp"
            />
          </UCard>

          <UCard>
            <template #header>
              <h2 class="font-semibold">
                {{ t('geoIp.diagnostics.title') }}
              </h2>
            </template><UAlert
              v-if="controller.diagnosticsState.value === 'unavailable'"
              color="warning"
              :title="t('geoIp.diagnostics.unavailableTitle')"
              :description="t('geoIp.diagnostics.unavailableDescription')"
            /><div v-else-if="controller.diagnosticsState.value === 'loading'" class="text-sm text-muted">
              {{ t('geoIp.common.loading') }}
            </div><div v-else-if="controller.diagnostics.value" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <div class="rounded-lg border border-default p-3">
                <div class="text-sm text-muted">
                  {{ t('geoIp.diagnostics.status') }}
                </div><div class="mt-1 font-medium">
                  {{ controller.diagnostics.value.severity }} · {{ controller.diagnostics.value.statusCode }}
                </div>
              </div><div class="rounded-lg border border-default p-3">
                <div class="text-sm text-muted">
                  {{ t('geoIp.diagnostics.queueDepth') }}
                </div><div class="mt-1 font-medium">
                  {{ controller.diagnostics.value.queueDepth }}
                </div>
              </div><div class="rounded-lg border border-default p-3">
                <div class="text-sm text-muted">
                  {{ t('geoIp.diagnostics.rejectedRefreshes') }}
                </div><div class="mt-1 font-medium">
                  {{ controller.diagnostics.value.rejectedRefreshCount }}
                </div>
              </div><div class="rounded-lg border border-default p-3">
                <div class="text-sm text-muted">
                  {{ t('geoIp.diagnostics.lastLookup') }}
                </div><div class="mt-1 font-medium">
                  {{ controller.diagnostics.value.lastLookupStatus ?? t('geoIp.common.none') }}
                </div>
              </div>
            </div><div v-if="controller.policy.value.providers.length" class="mt-4 grid gap-3 md:grid-cols-2">
              <div v-for="provider in controller.policy.value.providers" :key="provider.provider" class="rounded-lg border border-default p-3">
                <div class="flex items-center justify-between">
                  <span class="font-medium">{{ provider.provider }}</span><UBadge color="neutral" :label="t(provider.isExternal ? 'geoIp.diagnostics.externalService' : 'geoIp.diagnostics.localData')" />
                </div><div class="mt-2 text-sm text-muted">
                  {{ t('geoIp.diagnostics.providerVersion', { dataVersion: provider.dataVersion ?? t('geoIp.common.unknown'), buildEpoch: provider.buildEpoch ?? t('geoIp.common.unknown') }) }}
                </div>
              </div>
            </div>
          </UCard>

          <UCard>
            <template #header>
              <h2 class="font-semibold">
                {{ t('geoIp.decisions.title') }}
              </h2>
            </template><div v-if="controller.policy.value.recentDecisions.length === 0" class="text-sm text-muted">
              {{ t('geoIp.decisions.empty') }}
            </div><div v-else class="space-y-2">
              <div v-for="decision in controller.policy.value.recentDecisions" :key="`${decision.occurredAtUtc}-${decision.maskedIp}`" class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-default p-3">
                <div>
                  <div class="font-mono">
                    {{ decision.maskedIp }}
                  </div><div class="text-xs text-muted">
                    {{ new Date(decision.occurredAtUtc).toLocaleString() }} · {{ decision.reasonCode }} · {{ decision.lookupStatus }}
                  </div>
                </div><UBadge :color="decision.decision === 'Allow' ? 'success' : decision.decision === 'Deny' ? 'error' : 'neutral'" :label="decision.decision" />
              </div>
            </div>
          </UCard>
        </template>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
