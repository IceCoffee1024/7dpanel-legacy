import ui from '@nuxt/ui/vue-plugin'

import { createHead } from '@unhead/vue/client'
import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import App from './App.vue'

import './assets/css/main.css'

const app = createApp(App)

const head = createHead()
const router = createRouter({
  routes,
  history: createWebHistory(),
})

app.use(head)
app.use(router)
app.use(ui)

app.mount('#app')

// This will update routes at runtime without reloading the page
if (import.meta.hot) {
  handleHotUpdate(router)
}
