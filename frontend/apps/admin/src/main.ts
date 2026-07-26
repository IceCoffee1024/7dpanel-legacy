import ui from '@nuxt/ui/vue-plugin'

import { PiniaColada } from '@pinia/colada'
import { createHead } from '@unhead/vue/client'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import { handleHotUpdate } from 'vue-router/auto-routes'
import App from './App.vue'
import { ADMIN_LOCALE_KEY, createAdminI18n } from './app/i18n'
import { createAdminRouter } from './app/router'
import { connectServerState } from './app/serverState'

import './assets/css/main.css'

const app = createApp(App)

const head = createHead()
const pinia = createPinia()
const router = createAdminRouter(pinia)
const localeRuntime = createAdminI18n()

app.use(head)
app.use(pinia)
app.use(PiniaColada, {
  queryOptions: {
    staleTime: 0,
    refetchOnWindowFocus: false,
  },
})
connectServerState(pinia)
app.use(router)
app.use(localeRuntime.i18n)
app.use(ui)
app.provide(ADMIN_LOCALE_KEY, localeRuntime)

app.mount('#app')

// This will update routes at runtime without reloading the page
if (import.meta.hot) {
  handleHotUpdate(router)
}
