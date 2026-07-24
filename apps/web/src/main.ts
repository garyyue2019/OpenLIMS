import { createApp } from 'vue'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Layout,
  Result,
  Spin
} from 'ant-design-vue'
import 'ant-design-vue/dist/reset.css'
import App from './App.vue'
import { router } from './router'
import { initializeAuth } from './auth-store'
import './styles.css'

async function bootstrap() {
  await initializeAuth()
  createApp(App).use(router).use(Alert).use(Button).use(Card).use(Descriptions).use(Layout).use(Result).use(Spin).mount('#app')
}

void bootstrap()
