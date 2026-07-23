<script setup lang="ts">
import { useToast } from '@nuxt/ui/composables'
import { computed, reactive, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../index'
import { resolveSafeRedirect } from '../model/safeRedirect'

const credentials = reactive({
  username: '',
  password: '',
})
const rememberLogin = shallowRef(false)

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const toast = useToast()

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
    await auth.login(credentials.username, password, rememberLogin.value)
    if (auth.isAuthenticated) {
      if (auth.persistenceWarning) {
        toast.add({
          title: '会话无法持久保存，刷新或关闭页面后需要重新登录',
          color: 'warning',
        })
      }
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

    <UCheckbox
      v-model="rememberLogin"
      aria-label="保持登录"
      description="关闭浏览器后，在访问令牌有效期内继续登录"
      label="保持登录"
    />

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
