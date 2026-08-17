<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { NCard, NForm, NFormItem, NInput, NButton, useMessage, NAlert } from 'naive-ui'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const message = useMessage()
const auth = useAuthStore()

const username = ref('')
const email = ref('')
const password = ref('')
const loading = ref(false)
const errorMsg = ref<string | null>(null)

async function submit() {
  if (!username.value || !email.value || !password.value) {
    errorMsg.value = '请完整填写信息'
    return
  }
  if (password.value.length < 8) {
    errorMsg.value = '密码至少 8 位'
    return
  }
  loading.value = true
  errorMsg.value = null
  try {
    await auth.register({
      username: username.value,
      email: email.value,
      password: password.value
    })
    message.success('注册成功')
    router.replace('/')
  } catch (e) {
    errorMsg.value = (e as Error).message
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="register-page">
    <NCard class="register-card" title="创建账号">
      <NAlert v-if="errorMsg" type="error" :title="errorMsg" class="alert" />
      <NForm @keyup.enter="submit">
        <NFormItem label="用户名">
          <NInput v-model:value="username" placeholder="3-32 位" />
        </NFormItem>
        <NFormItem label="邮箱">
          <NInput v-model:value="email" placeholder="example@site.com" />
        </NFormItem>
        <NFormItem label="密码">
          <NInput v-model:value="password" type="password" show-password-on="click" placeholder="至少 8 位" />
        </NFormItem>
        <NButton type="primary" block :loading="loading" @click="submit">注册</NButton>
      </NForm>
      <div class="footer">
        <RouterLink to="/login">已有账号？去登录</RouterLink>
      </div>
    </NCard>
  </div>
</template>

<style scoped lang="scss">
.register-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 16px;
  background: var(--app-bg);
}
.register-card {
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
  a { color: var(--app-brand); }
}
</style>
