<route lang="json">
{
  "meta": {
    "public": true
  }
}
</route>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'

import LoginForm from '../features/auth/ui/LoginForm.vue'

const { t } = useI18n()
</script>

<template>
  <main class="grid min-h-dvh place-items-center bg-default px-5 py-10 sm:px-8">
    <section class="w-full max-w-sm" aria-labelledby="login-title">
      <div class="mb-4 flex justify-end">
        <LocaleMenu />
      </div>
      <header class="mb-8 flex items-center gap-3">
        <span class="flex size-11 shrink-0 items-center justify-center rounded-md bg-primary text-inverted">
          <UIcon name="i-lucide-server-cog" class="size-6" />
        </span>
        <div>
          <p class="text-sm font-semibold text-highlighted">
            7DPanel
          </p>
          <p class="text-sm text-muted">
            {{ t('auth.brandDescription') }}
          </p>
        </div>
      </header>

      <div class="rounded-md border border-default bg-elevated/25 p-6 shadow-sm sm:p-7">
        <div class="mb-6">
          <h1 id="login-title" class="text-xl font-semibold text-highlighted">
            {{ t('auth.pageTitle') }}
          </h1>
          <p class="mt-2 text-sm text-muted">
            {{ t('auth.pageDescription') }}
          </p>
        </div>

        <LoginForm />
      </div>
    </section>
  </main>
</template>
