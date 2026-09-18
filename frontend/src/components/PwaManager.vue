<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useMessage, NModal, NButton, NSpace } from 'naive-ui'
import { CloudOfflineOutline } from '@vicons/ionicons5'
import { setupPwa } from '@/pwa'

const message = useMessage()

/** 当前离线标记：驱动顶部警示横幅显隐 */
const offline = ref(false)

/** PWA 更新弹窗显隐 */
const refreshModalVisible = ref(false)

/** 保存 updateSW 函数，供用户点击确认后调用 */
let doUpdateSW: ((reloadPage?: boolean) => Promise<void>) | null = null

let handle: { dispose: () => void } | undefined

onMounted(() => {
  handle = setupPwa({
    onNeedRefresh(updateSW) {
      doUpdateSW = updateSW
      refreshModalVisible.value = true
    },
    onOfflineReady() {
      message.success('离线模式已就绪，断网时仍可打开应用')
    },
    onNetworkChange(isOffline) {
      offline.value = isOffline
      if (!isOffline) {
        message.success('网络已恢复')
      }
    },
  })
})

onBeforeUnmount(() => {
  handle?.dispose()
})

function handleConfirmRefresh() {
  if (doUpdateSW) {
    doUpdateSW(true)
  }
}
</script>

<template>
  <Transition name="offline-banner">
    <div v-if="offline" class="offline-banner" role="alert">
      <n-icon size="18" :component="CloudOfflineOutline" />
      <span>当前离线，编辑不会被保存，请恢复网络后再操作</span>
    </div>
  </Transition>

  <!-- PWA 更新提示弹窗 -->
  <NModal
    v-model:show="refreshModalVisible"
    preset="card"
    title="发现新版本"
    style="max-width: 420px"
    :bordered="false"
    size="medium"
  >
    <div style="font-size: 14px; color: #334155; line-height: 1.6;">
      新版本已就绪，点击「立即刷新」后生效（当前编辑内容不会丢失）。
    </div>
    <template #footer>
      <NSpace justify="end">
        <NButton size="small" @click="refreshModalVisible = false">
          稍后
        </NButton>
        <NButton type="primary" size="small" @click="handleConfirmRefresh">
          立即刷新
        </NButton>
      </NSpace>
    </template>
  </NModal>
</template>

<style scoped>
.offline-banner {
  position: fixed;
  top: 0;
  left: 50%;
  transform: translateX(-50%);
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
  padding: 8px 16px;
  border-radius: 8px;
  background: rgba(208, 48, 80, 0.95);
  color: #fff;
  font-size: 13px;
  line-height: 1.4;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.15);
  max-width: 90vw;
}

.offline-banner-enter-active,
.offline-banner-leave-active {
  transition:
    opacity 0.3s,
    transform 0.3s;
}

.offline-banner-enter-from,
.offline-banner-leave-to {
  opacity: 0;
  transform: translate(-50%, -12px);
}
</style>
