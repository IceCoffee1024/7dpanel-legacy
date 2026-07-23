import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import ApiKeysPage from './api-keys.vue'

it('composes the API Key feature as the protected page content', () => {
  const wrapper = mount(ApiKeysPage, {
    global: {
      stubs: {
        ApiKeysView: { template: '<section data-testid="api-keys-view" />' },
      },
    },
  })

  expect(wrapper.find('[data-testid="api-keys-view"]').exists()).toBe(true)
})
