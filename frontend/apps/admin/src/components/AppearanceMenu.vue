<script setup lang="ts">
import { computed } from 'vue'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useColorMode } from '@vueuse/core'

defineProps<{
  collapsed?: boolean
}>()

const colorMode = useColorMode()

const items = computed<DropdownMenuItem[][]>(() => [[{
  label: '浅色',
  icon: 'i-lucide-sun',
  type: 'checkbox',
  checked: colorMode.value === 'light',
  onSelect(event: Event) {
    event.preventDefault()
    colorMode.value = 'light'
  }
}, {
  label: '深色',
  icon: 'i-lucide-moon',
  type: 'checkbox',
  checked: colorMode.value === 'dark',
  onSelect(event: Event) {
    event.preventDefault()
    colorMode.value = 'dark'
  }
}, {
  label: '跟随系统',
  icon: 'i-lucide-monitor',
  type: 'checkbox',
  checked: colorMode.value === 'auto',
  onSelect(event: Event) {
    event.preventDefault()
    colorMode.value = 'auto'
  }
}]])
</script>

<template>
  <UDropdownMenu
    :items="items"
    :content="{ align: 'center', collisionPadding: 12 }"
    :ui="{ content: collapsed ? 'w-40' : 'w-(--reka-dropdown-menu-trigger-width)' }"
  >
    <UButton
      label="外观"
      icon="i-lucide-sun-moon"
      :trailing-icon="collapsed ? undefined : 'i-lucide-chevrons-up-down'"
      color="neutral"
      variant="ghost"
      block
      :square="collapsed"
      :aria-label="collapsed ? '外观' : undefined"
      class="data-[state=open]:bg-elevated"
      :ui="{ trailingIcon: 'text-dimmed' }"
    />
  </UDropdownMenu>
</template>
