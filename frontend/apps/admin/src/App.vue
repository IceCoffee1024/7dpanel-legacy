<script setup lang="ts">
import { useHead } from '@unhead/vue'
import { useColorMode } from '@vueuse/core'
import { computed } from 'vue'

import AppShell from './app/AppShell.vue'

const colorMode = useColorMode()
const themeColor = computed(() => colorMode.value === 'dark' ? '#18181b' : '#ffffff')

useHead({
  meta: [
    { name: 'theme-color', content: themeColor },
  ],
})
</script>

<template>
  <UApp>
    <AppShell />
  </UApp>
</template>
