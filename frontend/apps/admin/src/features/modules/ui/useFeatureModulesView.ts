import type { TableColumn } from '@nuxt/ui'
import type { FeatureModule, FeatureModuleLifecycleState } from '../api/modules'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import { useFeatureModules } from '../model/useFeatureModules'

export function useFeatureModulesView() {
  const route = useRoute()
  const router = useRouter()
  const { t } = useI18n()
  const enableTarget = shallowRef<FeatureModule | null>(null)
  const disableTarget = shallowRef<FeatureModule | null>(null)

  function handleSessionExpired() {
    void router.replace({ path: '/login', query: { redirect: route.fullPath } })
  }

  const featureModules = useFeatureModules({ onSessionExpired: handleSessionExpired })
  const columns = computed<TableColumn<FeatureModule>[]>(() => [
    { accessorKey: 'moduleId', header: t('modules.table.module') },
    { accessorKey: 'lifecycleState', header: t('modules.table.lifecycle') },
    { id: 'health', header: t('modules.table.health') },
    { id: 'details', header: t('modules.table.details') },
    { id: 'actions', header: '' },
  ])
  const isMutating = computed(() => featureModules.pendingModuleId.value !== null)
  const tableData = computed<FeatureModule[]>(() => [...featureModules.modules.value])

  function lifecycleColor(state: FeatureModuleLifecycleState): 'success' | 'neutral' | 'warning' {
    if (state === 'Enabled')
      return 'success'
    if (state === 'Draining' || state === 'RestartRequired')
      return 'warning'
    return 'neutral'
  }

  function list(values: readonly string[]): string {
    return values.length === 0 ? t('modules.common.none') : values.join(', ')
  }

  function observed(value: string): string {
    return new Date(value).toLocaleString()
  }

  function openEnable(module: FeatureModule) {
    if (featureModules.canMutate.value && module.isToggleable && !module.isEnabled)
      enableTarget.value = module
  }

  function openDisable(module: FeatureModule) {
    if (featureModules.canMutate.value && module.isToggleable && module.isEnabled)
      disableTarget.value = module
  }

  function updateEnableOpen(open: boolean) {
    if (!open && !isMutating.value)
      enableTarget.value = null
  }

  function updateDisableOpen(open: boolean) {
    if (!open && !isMutating.value)
      disableTarget.value = null
  }

  async function confirmEnable() {
    const target = enableTarget.value
    if (target !== null && await featureModules.enable(target.moduleId, target.rowVersion))
      enableTarget.value = null
  }

  async function confirmDisable() {
    const target = disableTarget.value
    if (target !== null && await featureModules.disable(target.moduleId, target.rowVersion))
      disableTarget.value = null
  }

  return {
    t,
    featureModules,
    enableTarget,
    disableTarget,
    columns,
    isMutating,
    tableData,
    lifecycleColor,
    list,
    observed,
    openEnable,
    openDisable,
    updateEnableOpen,
    updateDisableOpen,
    confirmEnable,
    confirmDisable,
  }
}
