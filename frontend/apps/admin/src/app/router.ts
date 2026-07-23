import type { Pinia } from 'pinia'
import type { RouterHistory } from 'vue-router'
import { watch } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { routes } from 'vue-router/auto-routes'

import { useAuthStore } from '../features/auth'
import { resolveSafeRedirect } from '../features/auth/model/safeRedirect'

export function createAdminRouter(pinia: Pinia, history: RouterHistory = createWebHistory()) {
  const router = createRouter({ routes, history })
  const auth = useAuthStore(pinia)

  router.beforeEach((to) => {
    if (to.path === '/login' && auth.isAuthenticated)
      return resolveSafeRedirect(to.query.redirect, router)

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return {
        path: '/login',
        query: { redirect: to.fullPath },
      }
    }
  })

  watch(() => auth.isAuthenticated, (isAuthenticated) => {
    const currentRoute = router.currentRoute.value
    if (!isAuthenticated && currentRoute.meta.requiresAuth) {
      void router.replace({
        path: '/login',
        query: { redirect: currentRoute.fullPath },
      })
    }
  })

  return router
}
