<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import { useColorMode } from '@vueuse/core'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps<{
  collapsed?: boolean
}>()

const colorMode = useColorMode()
const { t } = useI18n()

const items = computed<DropdownMenuItem[][]>(() => [[{
  label: t('appearance.light'),
  icon: 'i-lucide-sun',
  type: 'checkbox',
  checked: colorMode.value === 'light',
  onSelect(event: Event) {
    event.preventDefault()
    colorMode.value = 'light'
  },
}, {
  label: t('appearance.dark'),
  icon: 'i-lucide-moon',
  type: 'checkbox',
  checked: colorMode.value === 'dark',
  onSelect(event: Event) {
    event.preventDefault()
    colorMode.value = 'dark'
  },
}, {
  label: t('appearance.system'),
  icon: 'i-lucide-monitor',
  type: 'checkbox',
  checked: colorMode.value === 'auto',
  onSelect(event: Event) {
    event.preventDefault()
    colorMode.value = 'auto'
  },
}]])
</script>

<template>
  <UDropdownMenu
    :items="items"
    :content="{ align: 'center', collisionPadding: 12 }"
    :ui="{ content: collapsed ? 'w-40' : 'w-(--reka-dropdown-menu-trigger-width)' }"
  >
    <UButton
      :label="t('appearance.label')"
      icon="i-lucide-sun-moon"
      :trailing-icon="collapsed ? undefined : 'i-lucide-chevrons-up-down'"
      color="neutral"
      variant="ghost"
      block
      :square="collapsed"
      :aria-label="collapsed ? t('appearance.label') : undefined"
      class="data-[state=open]:bg-elevated"
      :ui="{ trailingIcon: 'text-dimmed' }"
    />
  </UDropdownMenu>
</template>
