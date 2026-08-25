import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['favicon.svg', 'icons.svg'],
      // manifest 使用 public/manifest.json（index.html 已引用），插件只负责 Service Worker
      manifest: false,
      workbox: {
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//],
        globPatterns: ['**/*.{js,css,svg,png,woff2}'],
        // MindMapEditorView chunk 约 1MB，默认 2MB 上限需调高
        maximumFileSizeToCacheInBytes: 3 * 1024 * 1024,
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          {
            // 公开只读 GET 接口：network-first，断网时用缓存兜底（模板/健康检查不敏感）。
            // 鉴权接口（/api/auth/**）与所有写操作不匹配此规则，保持 network-only，避免 JWT 数据落缓存。
            urlPattern: ({ url, request }) =>
              request.method === 'GET' &&
              (url.pathname === '/api/health' ||
                url.pathname === '/api/templates' ||
                /^\/api\/templates\/[^/]+$/.test(url.pathname)),
            handler: 'NetworkFirst',
            options: {
              cacheName: 'api-public-read',
              networkTimeoutSeconds: 3,
              expiration: {
                maxEntries: 32,
                maxAgeSeconds: 24 * 60 * 60,
              },
              cacheableResponse: {
                statuses: [0, 200],
              },
            },
          },
        ],
      },
      devOptions: {
        // 本地用 vite preview 验证，不在 dev 模式挂 SW
        enabled: false,
      },
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      stream: fileURLToPath(new URL('./src/shims/stream.ts', import.meta.url)),
    },
  },
  define: {
    'process.env': {},
    global: 'globalThis',
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    target: 'es2020',
    sourcemap: false,
  },
})

