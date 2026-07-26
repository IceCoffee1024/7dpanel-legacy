import { defineConfig } from '@hey-api/openapi-ts'

export default defineConfig({
  input: './openapi/7dpanel.v1.json',
  output: {
    clean: true,
    path: './src/shared/api/generated',
  },
  plugins: [
    '@hey-api/typescript',
    {
      name: '@hey-api/client-fetch',
      throwOnError: true,
    },
    {
      name: '@hey-api/sdk',
      client: true,
      responseStyle: 'data',
    },
    {
      name: '@pinia/colada',
      mutationOptions: true,
      queryKeys: {
        tags: true,
      },
      queryOptions: true,
    },
  ],
})
