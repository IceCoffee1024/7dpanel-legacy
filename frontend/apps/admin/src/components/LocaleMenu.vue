<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { useAdminLocale } from '../app/i18n'

const props = defineProps<{
  collapsed?: boolean
}>()

const { t } = useI18n()
const { locale, setLocale } = useAdminLocale()

const currentLanguage = computed(() => locale.value === 'zh-CN'
  ? t('locale.simplifiedChinese')
  : t('locale.english'))
const items = computed<DropdownMenuItem[][]>(() => [[{
  label: t('locale.english'),
  icon: 'i-lucide-languages',
  type: 'checkbox',
  checked: locale.value === 'en',
  onSelect() {
    setLocale('en')
  },
}, {
  label: t('locale.simplifiedChinese'),
  icon: 'i-lucide-languages',
  type: 'checkbox',
  checked: locale.value === 'zh-CN',
  onSelect() {
    setLocale('zh-CN')
  },
}]])
const accessibleLabel = computed(() => props.collapsed
  ? t('locale.current', { language: currentLanguage.value })
  : undefined)
</script>

<template>
  <UDropdownMenu
    :items="items"
    :content="{ align: 'center', collisionPadding: 12 }"
    :ui="{ content: collapsed ? 'w-40' : 'w-(--reka-dropdown-menu-trigger-width)' }"
  >
    <UButton
      :aria-label="accessibleLabel"
      block
      class="data-[state=open]:bg-elevated"
      color="neutral"
      data-testid="locale-menu-trigger"
      icon="i-lucide-languages"
      :label="collapsed ? undefined : currentLanguage"
      :square="collapsed"
      :trailing-icon="collapsed ? undefined : 'i-lucide-chevrons-up-down'"
      :ui="{ trailingIcon: 'text-dimmed' }"
      variant="ghost"
    />
  </UDropdownMenu>
</template>
