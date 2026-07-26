import antfu from '@antfu/eslint-config'

export default antfu({
  formatters: {
    /**
     * Format CSS, LESS, SCSS files, also the `<style>` blocks in Vue
     * By default uses Prettier
     */
    css: true,
    /**
     * Format HTML files
     * By default uses Prettier
     */
    html: true,
    /**
     * Format Markdown files
     * Supports Prettier and dprint
     * By default uses Prettier
     */
    markdown: 'prettier',
  },
  ignores: [
    'auto-imports.d.ts',
    'components.d.ts',
    'src/shared/api/generated/**',
    'src/route-map.d.ts',
  ],
  vue: {
    overrides: {
      'vue/multi-word-component-names': 'off',
      'vue/max-attributes-per-line': ['error', { singleline: 3 }],
      // 'vue/component-definition-name-casing': ['error', 'kebab-case'],
    },
  },
  stylistic: {
    semi: false,
    quotes: 'single',
    indent: 2,
  },
  rules: {
    'no-console': 'off', // Allow console.log for debugging
  },
  typescript: true,
})
