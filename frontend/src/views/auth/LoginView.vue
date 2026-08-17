<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NCard, NForm, NFormItem, NInput, NButton, useMessage, NAlert } from 'naive-ui'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const auth = useAuthStore()

const account = ref('')
const password = ref('')
const loading = ref(false)
const errorMsg = ref<string | null>(null)

async function submit() {
  if (!account.value || !password.value) {
    errorMsg.value = '请输入账号和密码'
    return
  }
  loading.value = true
  errorMsg.value = null
  try {
    await auth.login({ account: account.value, password: password.value })
    message.success('登录成功')
    const redirect = (route.query.redirect as string | undefined) ?? '/'
    router.replace(redirect)
  } catch (e) {
    errorMsg.value = (e as Error).message
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <NCard class="login-card" title="登录思维导图">
      <NAlert v-if="errorMsg" type="error" :title="errorMsg" class="alert" />
      <NForm @keyup.enter="submit">
        <NFormItem label="用户名或邮箱">
          <NInput v-model:value="account" placeholder="用户名 / 邮箱" :input-props="{ autocomplete: 'username' }" />
        </NFormItem>
        <NFormItem label="密码">
          <NInput v-model:value="password" type="password" show-password-on="click" placeholder="密码" :input-props="{ autocomplete: 'current-password' }" />
        </NFormItem>
        <NButton type="primary" block :loading="loading" @click="submit">登录</NButton>
      </NForm>
      <div class="footer">
        <span>还没有账号？</span>
        <RouterLink to="/register">立即注册</RouterLink>
      </div>
    </NCard>
  </div>
</template>

<style scoped lang="scss">
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 16px;
  background: var(--app-bg);
}
.login-card {
  width: 100%;
  max-width: 420px;
  background: var(--app-card-bg);
}
.alert {
  margin-bottom: 12px;
}
.footer {
  margin-top: 12px;
  text-align: center;
  font-size: 13px;
  color: var(--app-text-secondary);
  a {
    color: var(--app-brand);
    margin-left: 4px;
  }
}
</style>
