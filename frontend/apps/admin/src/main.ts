import ui from '@nuxt/ui/vue-plugin'

import { createHead } from '@unhead/vue/client'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import { handleHotUpdate } from 'vue-router/auto-routes'
import App from './App.vue'
import { createAdminRouter } from './app/router'

import './assets/css/main.css'

const app = createApp(App)

const head = createHead()
const pinia = createPinia()
const router = createAdminRouter(pinia)

app.use(head)
app.use(pinia)
app.use(router)
app.use(ui)

app.mount('#app')

// This will update routes at runtime without reloading the page
if (import.meta.hot) {
  handleHotUpdate(router)
}
