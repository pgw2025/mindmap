import { createApp } from 'vue'
import { createPinia } from 'pinia'
import naive from 'naive-ui'
import App from './App.vue'
import router from './router'
import './styles/main.scss'

// Service Worker 注册（prompt 模式 + 更新/离线提示）由 PwaManager 组件接管
const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(naive)
app.mount('#app')
