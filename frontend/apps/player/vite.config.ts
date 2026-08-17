import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  return {
    base: '/player/',
    plugins: [vue()],
    server: {
      proxy: {
        '/api': {
          target: env.VITE_BACKEND_URL,
          changeOrigin: false,
        },
      },
    },
  }
})
