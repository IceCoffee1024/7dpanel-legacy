import process from 'node:process'

import ui from '@nuxt/ui/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'
import { configDefaults } from 'vitest/config'
import vueRouter from 'vue-router/vite'

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  return {
    plugins: [
      vueRouter({
        dts: 'src/route-map.d.ts',
      }),
      vue(),
      ui({
        ui: {
          colors: {
            primary: 'green',
            neutral: 'zinc',
          },
        },
        icon: {
          clientBundle: {
            scan: true,
          },
        },
      }),
    ],
    server: {
      proxy: {
        '/api': {
          target: env.VITE_BACKEND_URL || 'http://127.0.0.1:18080',
        },
      },
    },
    test: {
      environment: 'happy-dom',
      exclude: [...configDefaults.exclude, 'tests/e2e/**'],
      setupFiles: ['./src/shared/testing/setup.ts'],
      clearMocks: true,
      restoreMocks: true,
    },
  }
})
