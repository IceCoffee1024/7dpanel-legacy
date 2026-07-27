<script setup lang="ts">
import type { DailyRewardPolicyUpdateRequest } from '../api/dailyRewardPolicy'
import type { DailyRewardPolicyController } from '../model/useDailyRewardPolicy'

import { reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ controller: DailyRewardPolicyController }>()
const emit = defineEmits<{ save: [draft: DailyRewardPolicyUpdateRequest], refresh: [] }>()
const { t } = useI18n()

interface DailyRewardPolicyForm {
  rewardPackageId: string
  enabled: boolean
  expectedRowVersion: bigint | null
}

const form = reactive<DailyRewardPolicyForm>({
  rewardPackageId: '',
  enabled: true,
  expectedRowVersion: null,
})

watch(() => props.controller.draft.value, (next) => {
  form.rewardPackageId = next.rewardPackageId
  form.enabled = next.enabled
  form.expectedRowVersion = next.expectedRowVersion
}, { immediate: true })

function submit() {
  if (form.rewardPackageId.trim() === '')
    return

  const request: DailyRewardPolicyUpdateRequest = {
    rewardPackageId: form.rewardPackageId,
    enabled: form.enabled,
    expectedRowVersion: form.expectedRowVersion,
  }
  emit('save', request)
}

function saveErrorDescription() {
  const code = props.controller.saveError.value?.code
  return code === null || code === undefined
    ? ''
    : t(`dailyReward.error.${code}`)
}
</script>

<template>
  <UDashboardPanel id="daily-reward-policy">
    <template #header>
      <UDashboardNavbar :title="t('dailyReward.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('dailyReward.refresh')"
            :loading="props.controller.state.value === 'loading'"
            variant="outline"
            @click="emit('refresh')"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert
          color="neutral"
          icon="i-lucide-info"
          :title="t('dailyReward.explanationTitle')"
          :description="t('dailyReward.explanationDescription')"
        />
        <UAlert
          v-if="props.controller.state.value === 'not-configured'"
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('dailyReward.state.notConfiguredTitle')"
          :description="t('dailyReward.state.notConfiguredDescription')"
        />
        <UAlert
          v-else-if="props.controller.state.value === 'stale'"
          color="warning"
          icon="i-lucide-cloud-alert"
          :title="t('dailyReward.state.staleTitle')"
          :description="t('dailyReward.state.staleDescription')"
        />
        <UAlert
          v-else-if="props.controller.state.value === 'forbidden'"
          color="error"
          icon="i-lucide-shield-x"
          :title="t('dailyReward.state.forbiddenTitle')"
          :description="t('dailyReward.state.forbiddenDescription')"
        />
        <UAlert
          v-else-if="props.controller.state.value === 'failed'"
          color="error"
          icon="i-lucide-circle-alert"
          :title="t('dailyReward.state.failedTitle')"
          :description="t('dailyReward.state.failedDescription')"
        />
        <UAlert
          v-if="props.controller.saveError.value"
          color="error"
          icon="i-lucide-circle-alert"
          :title="t('dailyReward.error.title')"
          :description="saveErrorDescription()"
        />

        <UCard>
          <template #header>
            <div>
              <h2 class="font-semibold text-highlighted">
                {{ t('dailyReward.form.title') }}
              </h2>
              <p class="mt-1 text-sm text-muted">
                {{ t('dailyReward.form.description') }}
              </p>
            </div>
          </template>

          <UForm :state="form" class="space-y-5" @submit="submit">
            <UFormField :label="t('dailyReward.form.ruleId')" :description="t('dailyReward.form.ruleIdDescription')">
              <UInput :model-value="t('dailyReward.ruleId')" disabled />
            </UFormField>
            <UFormField
              name="rewardPackageId"
              :label="t('dailyReward.form.rewardPackageId')"
              :description="t('dailyReward.form.rewardPackageIdDescription')"
              required
            >
              <UInput v-model="form.rewardPackageId" autocomplete="off" :placeholder="t('dailyReward.form.rewardPackageIdPlaceholder')" />
            </UFormField>
            <UFormField name="enabled" :label="t('dailyReward.form.enabled')" :description="t('dailyReward.form.enabledDescription')">
              <USwitch v-model="form.enabled" />
            </UFormField>
            <div class="flex flex-wrap items-center justify-between gap-3">
              <p v-if="props.controller.policy.value" class="text-xs text-muted">
                {{ t('dailyReward.version', { version: props.controller.policy.value.rowVersion.toString() }) }}
              </p>
              <UButton
                class="ml-auto"
                :disabled="form.rewardPackageId.trim() === '' || props.controller.state.value === 'forbidden'"
                :label="t('dailyReward.save')"
                :loading="props.controller.isSaving.value"
                type="submit"
              />
            </div>
          </UForm>
        </UCard>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
