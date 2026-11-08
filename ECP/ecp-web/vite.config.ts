import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), vueDevTools()],

  // 自定义开发服务器配置
  server: {
    port: 3000, // 设置开发服务器端口号
    host: true, // 允许局域网访问（默认是 localhost，只能本机访问）
    open: true, // 启动开发服务器时自动打开浏览器
    strictPort: true, // 如果端口被占用，直接报错而不是自动+1

    proxy: {
      '/api': {
        target: 'https://localhost:7089', // 代理目标地址
        changeOrigin: true, // 是否改变请求头中的origin字段
        secure: false, // 如果是https接口，需要配置这个参数，仅用于本机HTTPS开发证书的代理连接
      },
    },
  },

  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
