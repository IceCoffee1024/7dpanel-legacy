<script setup lang="ts">
import { useToast } from '@nuxt/ui/composables'
import * as v from 'valibot'
import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
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
const { t } = useI18n()

const LoginSchema = v.object({
  username: v.pipe(v.string(), v.trim(), v.nonEmpty()),
  password: v.pipe(v.string(), v.nonEmpty()),
})

const errorMessage = computed(() => {
  if (auth.error === null)
    return ''
  if (auth.error === 'rate-limited')
    return t('auth.errors.rateLimited')
  if (auth.error === 'invalid-credentials')
    return t('auth.errors.invalidCredentials')
  return t('auth.errors.generic')
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
          title: t('auth.persistenceWarning'),
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
  <UForm
    :schema="LoginSchema"
    :state="credentials"
    class="space-y-5"
    @submit="submit"
  >
    <UFormField :label="t('auth.username')" name="username">
      <UInput
        id="username"
        v-model="credentials.username"
        autocomplete="username"
        class="w-full"
      />
    </UFormField>

    <UFormField :label="t('auth.password')" name="password">
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
      :aria-label="t('auth.remember')"
      :description="t('auth.rememberDescription')"
      :label="t('auth.remember')"
    />

    <p v-if="errorMessage" role="alert" class="text-sm text-error">
      {{ errorMessage }}
    </p>

    <UButton
      block
      :disabled="auth.status === 'submitting'"
      :label="t('auth.login')"
      :loading="auth.status === 'submitting'"
      type="submit"
    />
  </UForm>
</template>
