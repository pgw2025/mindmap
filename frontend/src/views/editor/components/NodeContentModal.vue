<script setup lang="ts">
import { ref, watch } from 'vue'
import { useMessage, NModal, NInput, NButton, NSpace } from 'naive-ui'
import type { NodeDto, NodeUpdatePayload } from '@/api/nodes'
import RichTextEditor from '../RichTextEditor.vue'

const props = defineProps<{
  show: boolean
  node: NodeDto | null
}>()

const emit = defineEmits<{
  'update:show': [boolean]
  'save': [payload: NodeUpdatePayload]
}>()

const message = useMessage()
const editingNodeTitle = ref('')
const editingNodeContent = ref('')
const editingNodeNote = ref('')
const savingContent = ref(false)

/** 弹窗打开时载入选中节点的字段 */
watch(() => props.show, (v) => {
  if (v && props.node) {
    editingNodeTitle.value = props.node.title
    editingNodeContent.value = props.node.content || ''
    editingNodeNote.value = props.node.note || ''
  }
})

async function saveNodeContent() {
  if (!props.node) return
  savingContent.value = true
  try {
    const payload: NodeUpdatePayload = {
      title: editingNodeTitle.value,
      content: editingNodeContent.value || undefined,
      note: editingNodeNote.value || undefined
    }
    emit('save', payload)
    emit('update:show', false)
    message.success('内容已保存')
  } catch (e) {
    message.error((e as Error).message || '保存失败')
  } finally {
    savingContent.value = false
  }
}
</script>

<template>
  <NModal :show="show" @update:show="emit('update:show', $event)" preset="card" title="📝 编辑节点内容"
    style="width: 600px; max-width: 92vw" :mask-closable="false">
    <div class="content-edit-body">
      <div class="field-group">
        <label class="field-label">标题</label>
        <NInput v-model:value="editingNodeTitle" placeholder="节点标题" maxlength="200" />
      </div>
      <div class="field-group">
        <label class="field-label">正文内容</label>
        <RichTextEditor v-model="editingNodeContent" />
      </div>
      <div class="field-group">
        <label class="field-label">备注</label>
        <NInput v-model:value="editingNodeNote" type="textarea" placeholder="节点备注（可选）"
          :autosize="{ minRows: 2, maxRows: 4 }" maxlength="2000" />
      </div>
    </div>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="emit('update:show', false)">取消</NButton>
        <NButton type="primary" :loading="savingContent" @click="saveNodeContent">
          {{ savingContent ? '保存中...' : '保存' }}
        </NButton>
      </NSpace>
    </template>
  </NModal>
</template>

<style scoped lang="scss">
.content-edit-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-secondary, #666);
}
</style>
