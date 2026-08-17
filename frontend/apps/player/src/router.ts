import { createRouter, createWebHistory } from 'vue-router'

import LoginView from './views/LoginView.vue'
import StoreView from './views/StoreView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', redirect: { name: 'login' } },
    { path: '/login', name: 'login', component: LoginView },
    { path: '/store', name: 'store', component: StoreView },
    { path: '/:pathMatch(.*)*', redirect: { name: 'login' } },
  ],
})

export default router
