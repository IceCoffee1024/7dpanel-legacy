<script setup lang="ts">
import { useHead } from '@unhead/vue'
import { useColorMode } from '@vueuse/core'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import AppShell from './app/AppShell.vue'
import { useAdminLocale } from './app/i18n'

const colorMode = useColorMode()
const route = useRoute()
const { nuxtLocale } = useAdminLocale()
const themeColor = computed(() => colorMode.value === 'dark' ? '#18181b' : '#ffffff')

useHead({
  meta: [
    { name: 'theme-color', content: themeColor },
  ],
})
</script>

<template>
  <UApp :locale="nuxtLocale">
    <RouterView v-if="route.meta.public" />
    <AppShell v-else />
  </UApp>
</template>
