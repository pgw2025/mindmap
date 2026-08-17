<script setup lang="ts">
import { watch, onBeforeUnmount, ref } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import TextAlign from '@tiptap/extension-text-align'
import Placeholder from '@tiptap/extension-placeholder'
import Underline from '@tiptap/extension-underline'
import { NButton, NInput, NSpace } from 'naive-ui'

const props = defineProps<{
  modelValue?: string
  placeholder?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const showLinkInput = ref(false)
const linkUrl = ref('')

const editor = useEditor({
  content: props.modelValue || '',
  extensions: [
    StarterKit,
    Underline,
    Link.configure({
      openOnClick: false,
      HTMLAttributes: { rel: 'noopener noreferrer nofollow', target: '_blank' }
    }),
    TextAlign.configure({ types: ['heading', 'paragraph'] }),
    Placeholder.configure({
      placeholder: props.placeholder || '输入节点正文内容...'
    })
  ],
  onUpdate: () => {
    emit('update:modelValue', editor.value?.getHTML() || '')
  }
})

watch(
  () => props.modelValue,
  (val) => {
    if (val !== undefined && val !== editor.value?.getHTML()) {
      editor.value?.commands.setContent(val || '')
    }
  }
)

onBeforeUnmount(() => {
  editor.value?.destroy()
})

function setLink() {
  const url = linkUrl.value.trim()
  if (!url) {
    editor.value?.chain().focus().extendMarkRange('link').unsetLink().run()
  } else {
    const full = /^https?:\/\//.test(url) ? url : `https://${url}`
    editor.value?.chain().focus().extendMarkRange('link').setLink({ href: full }).run()
  }
  showLinkInput.value = false
  linkUrl.value = ''
}

function openLinkEditor() {
  const prev = editor.value?.getAttributes('link').href as string | undefined
  linkUrl.value = prev || ''
  showLinkInput.value = true
}
</script>

<template>
  <div class="rich-text-editor">
    <div class="toolbar" v-if="editor">
      <NButton
        size="tiny"
        :type="editor.isActive('bold') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleBold().run()"
        title="加粗"
      >
        <span class="tb-icon"><b>B</b></span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive('italic') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleItalic().run()"
        title="斜体"
      >
        <span class="tb-icon"><i>I</i></span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive('underline') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleUnderline().run()"
        title="下划线"
      >
        <span class="tb-icon"><u>U</u></span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive('strike') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleStrike().run()"
        title="删除线"
      >
        <span class="tb-icon"><s>S</s></span>
      </NButton>
      <span class="tb-divider"></span>
      <NButton
        size="tiny"
        :type="editor.isActive('bulletList') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleBulletList().run()"
        title="无序列表"
      >
        <span class="tb-icon">• ─</span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive('orderedList') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleOrderedList().run()"
        title="有序列表"
      >
        <span class="tb-icon">1. ─</span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive('blockquote') ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().toggleBlockquote().run()"
        title="引用"
      >
        <span class="tb-icon">❝</span>
      </NButton>
      <span class="tb-divider"></span>
      <NButton
        size="tiny"
        :type="editor.isActive('link') ? 'primary' : 'default'"
        quaternary
        @click="openLinkEditor"
        title="链接"
      >
        <span class="tb-icon">🔗</span>
      </NButton>
      <span class="tb-divider"></span>
      <NButton
        size="tiny"
        :type="editor.isActive({ textAlign: 'left' }) ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().setTextAlign('left').run()"
        title="左对齐"
      >
        <span class="tb-icon">⬅</span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive({ textAlign: 'center' }) ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().setTextAlign('center').run()"
        title="居中"
      >
        <span class="tb-icon">⬌</span>
      </NButton>
      <NButton
        size="tiny"
        :type="editor.isActive({ textAlign: 'right' }) ? 'primary' : 'default'"
        quaternary
        @click="editor.chain().focus().setTextAlign('right').run()"
        title="右对齐"
      >
        <span class="tb-icon">➡</span>
      </NButton>
    </div>

    <div v-if="showLinkInput" class="link-input-bar">
      <NInput
        v-model:value="linkUrl"
        size="small"
        placeholder="输入链接 URL"
        @keyup.enter="setLink"
      />
      <NSpace>
        <NButton size="small" type="primary" @click="setLink">确定</NButton>
        <NButton size="small" @click="showLinkInput = false; linkUrl = ''">取消</NButton>
      </NSpace>
    </div>

    <EditorContent :editor="editor" class="editor-content" />
  </div>
</template>

<style scoped lang="scss">
.rich-text-editor {
  border: 1px solid var(--app-border, #e0e0e6);
  border-radius: 8px;
  overflow: hidden;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 2px;
  align-items: center;
  padding: 4px 6px;
  background: var(--app-bg, #f5f7fa);
  border-bottom: 1px solid var(--app-border, #e0e0e6);
}

.tb-icon {
  font-size: 14px;
  min-width: 18px;
  text-align: center;
}

.tb-divider {
  width: 1px;
  height: 20px;
  background: var(--app-border, #d0d0d6);
  margin: 0 2px;
}

.link-input-bar {
  display: flex;
  gap: 8px;
  align-items: center;
  padding: 6px 8px;
  background: #fffbeb;
  border-bottom: 1px solid var(--app-border, #e0e0e6);
}

.editor-content {
  min-height: 120px;
  max-height: 300px;
  overflow-y: auto;
  padding: 12px 16px;

  :deep(.tiptap) {
    outline: none;
    min-height: 96px;
    font-size: 14px;
    line-height: 1.6;
    color: var(--app-text-primary, #333);

    p {
      margin: 0 0 8px;
    }

    ul,
    ol {
      padding-left: 24px;
      margin: 0 0 8px;
    }

    blockquote {
      padding-left: 12px;
      border-left: 3px solid var(--app-border, #d0d0d6);
      color: var(--app-text-secondary, #666);
      margin: 0 0 8px;
    }

    a {
      color: var(--app-primary, #18a058);
      text-decoration: underline;
      cursor: pointer;
    }

    p:last-child {
      margin-bottom: 0;
    }

    p.is-editor-empty:first-child::before {
      content: attr(data-placeholder);
      color: var(--app-text-secondary, #aaa);
      pointer-events: none;
      float: left;
      height: 0;
    }
  }
}
</style>
