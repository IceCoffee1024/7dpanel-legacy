import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import ServerConfigurationPage from './configuration.vue'

it('composes the server configuration feature', () => {
  const wrapper = mount(ServerConfigurationPage, {
    global: { stubs: { ServerConfigurationView: { template: '<section data-testid="configuration-view" />' } } },
  })

  expect(wrapper.find('[data-testid="configuration-view"]').exists()).toBe(true)
})
