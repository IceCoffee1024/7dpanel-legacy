import type { Pinia } from 'pinia'
import type { RouterHistory } from 'vue-router'
import { createRouter, createWebHistory } from 'vue-router'
import { routes } from 'vue-router/auto-routes'

import { useAuthStore } from '../features/auth'
import { resolveSafeRedirect } from '../features/auth/model/safeRedirect'

export function createAdminRouter(pinia: Pinia, history: RouterHistory = createWebHistory()) {
  const router = createRouter({ routes, history })

  router.beforeEach((to) => {
    const auth = useAuthStore(pinia)

    if (to.path === '/login' && auth.isAuthenticated)
      return resolveSafeRedirect(to.query.redirect, router)

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return {
        path: '/login',
        query: { redirect: to.fullPath },
      }
    }
  })

  return router
}
