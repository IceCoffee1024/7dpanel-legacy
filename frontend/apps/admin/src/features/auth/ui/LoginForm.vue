<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../index'
import { resolveSafeRedirect } from '../model/safeRedirect'

const credentials = reactive({
  username: '',
  password: '',
})

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const errorMessage = computed(() => {
  if (auth.error === null)
    return ''
  if (auth.error === 'rate-limited')
    return '请求过于频繁，请稍后重试'
  if (auth.error === 'invalid-credentials')
    return '用户名或密码错误'
  return '登录失败，请稍后重试'
})

async function submit() {
  if (auth.status === 'submitting')
    return

  const password = credentials.password
  try {
    await auth.login(credentials.username, password)
    if (auth.isAuthenticated) {
      await router.replace(resolveSafeRedirect(route.query.redirect, router))
    }
  }
  finally {
    credentials.password = ''
  }
}
</script>

<template>
  <UForm :state="credentials" class="space-y-5" @submit="submit">
    <UFormField label="用户名" name="username">
      <UInput
        id="username"
        v-model="credentials.username"
        autocomplete="username"
        class="w-full"
      />
    </UFormField>

    <UFormField label="密码" name="password">
      <UInput
        id="password"
        v-model="credentials.password"
        autocomplete="current-password"
        class="w-full"
        type="password"
      />
    </UFormField>

    <p v-if="errorMessage" role="alert" class="text-sm text-error">
      {{ errorMessage }}
    </p>

    <UButton
      block
      :disabled="auth.status === 'submitting'"
      label="登录"
      :loading="auth.status === 'submitting'"
      type="submit"
    />
  </UForm>
</template>
