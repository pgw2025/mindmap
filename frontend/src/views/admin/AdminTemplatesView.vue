<script setup lang="ts">
import { computed, h, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  NButton,
  NCard,
  NColorPicker,
  NDataTable,
  NInput,
  NInputNumber,
  NModal,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  NPagination,
  NDivider,
  NCollapse,
  NCollapseItem,
  useMessage,
  type DataTableColumns
} from 'naive-ui'
import { useTemplatesStore } from '@/stores/templates'
import * as templatesApi from '@/api/templates'
import type { AdminTemplateListItem, TemplateDetail } from '@/api/templates'
import { THEMES, type MindMapThemeConfig, type NodeLevelStyle } from '@/themes/presets'
import MindMap from 'simple-mind-map'
import Drag from 'simple-mind-map/src/plugins/Drag.js'

MindMap.usePlugin(Drag)

const store = useTemplatesStore()
const message = useMessage()

// ---------- 列表 ----------
const keyword = ref('')
const scope = ref<'all' | 'enabled' | 'disabled'>('all')
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)

const scopeOptions = [
  { label: '全部', value: 'all' },
  { label: '已启用', value: 'enabled' },
  { label: '已禁用', value: 'disabled' }
]

async function load(): Promise<void> {
  loading.value = true
  try {
    await store.loadAdmin()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    loading.value = false
  }
}

function applySearch(): void {
  page.value = 1
  store.adminKeyword = keyword.value
  load()
}

watch(scope, () => {
  page.value = 1
  store.setAdminScope(scope.value)
})

onMounted(load)

// ---------- 编辑弹窗 ----------
const editVisible = ref(false)
const editingId = ref<string | null>(null)
const submitting = ref(false)

// 基本表单
const formName = ref('')
const formDescription = ref('')
const formSortOrder = ref(0)
const formEnabled = ref(true)

// 样式配置（完整 MindMapThemeConfig）
const formConfig = ref<MindMapThemeConfig | null>(null)

// 当前选中预设（用于"从预设加载"）
const presetId = ref<string>('classic')

// 删除确认
const deleteModalVisible = ref(false)
const deleteTarget = ref<AdminTemplateListItem | null>(null)
const deleteSubmitting = ref(false)

// ---------- 预览实例 ----------
const previewRef = ref<HTMLDivElement | null>(null)
let previewInstance: MindMap | null = null
const editStructureMode = ref(false)
const selectedNodeId = ref<string | null>(null)

// 4 个层级的 key
const levelKeys = ['root', 'second', 'node', 'generalization'] as const
type LevelKey = (typeof levelKeys)[number]
const levelLabels: Record<LevelKey, string> = {
  root: '第一级（根节点）',
  second: '第二级',
  node: '第三级及以下',
  generalization: '第四级（概括）'
}

const shapeOptions = [
  { label: '矩形', value: 'rectangle' },
  { label: '圆角矩形', value: 'rounded' },
  { label: '圆形', value: 'circle' },
  { label: '椭圆', value: 'ellipse' },
  { label: '菱形', value: 'diamond' },
  { label: '平行四边形', value: 'parallelogram' },
  { label: '下划线', value: 'underline' }
]

const fontWeightOptions = [
  { label: '常规', value: 'normal' },
  { label: '加粗', value: 'bold' }
]

const lineStyleOptions = [
  { label: '曲线', value: 'curve' },
  { label: '直线', value: 'straight' }
]

const borderDashOptions = [
  { label: '无', value: 'none' },
  { label: '虚线 5,3', value: '5,3' },
  { label: '点线 3,3', value: '3,3' }
]

const presetOptions = computed(() =>
  THEMES.map((t) => ({ label: t.name, value: t.id }))
)

// ---------- 初始化/打开/关闭 ----------

function buildDefaultSampleTree(): unknown {
  // 默认预览结构：根 + 2 分支 + 各 2 子节点，覆盖 4 级
  return {
    data: { text: '中心主题' },
    children: [
      {
        data: { text: '分支一' },
        children: [
          { data: { text: '子节点 1-1' }, children: [{ data: { text: '叶子 1-1-1' } }] },
          { data: { text: '子节点 1-2' } }
        ]
      },
      {
        data: { text: '分支二' },
        children: [
          { data: { text: '子节点 2-1' } },
          { data: { text: '子节点 2-2' }, children: [{ data: { text: '叶子 2-2-1' } }] }
        ]
      }
    ]
  }
}

function openCreate(): void {
  editingId.value = null
  formName.value = ''
  formDescription.value = ''
  formSortOrder.value = 0
  formEnabled.value = true
  presetId.value = 'classic'
  // 默认加载 classic 预设作为初始样式
  formConfig.value = JSON.parse(JSON.stringify(THEMES[0].config)) as MindMapThemeConfig
  editVisible.value = true
  nextTick(() => initPreview())
}

async function openEdit(row: AdminTemplateListItem): Promise<void> {
  try {
    const detail: TemplateDetail = await templatesApi.fetchAdminTemplate(row.id)
    editingId.value = row.id
    formName.value = detail.name
    formDescription.value = detail.description ?? ''
    formSortOrder.value = detail.sortOrder
    formEnabled.value = detail.isEnabled
    try {
      formConfig.value = JSON.parse(detail.configJson) as MindMapThemeConfig
    } catch {
      formConfig.value = JSON.parse(JSON.stringify(THEMES[0].config)) as MindMapThemeConfig
    }
    // 初始结构预览数据：用模板自带结构，否则用默认样本
    editVisible.value = true
    nextTick(() => initPreview(detail.initialStructureJson))
  } catch (e) {
    message.error((e as Error).message)
  }
}

function closeEdit(): void {
  editVisible.value = false
  destroyPreview()
  formConfig.value = null
}

function destroyPreview(): void {
  if (previewInstance) {
    try {
      previewInstance.destroy?.()
    } catch {
      /* ignore */
    }
    previewInstance = null
  }
  selectedNodeId.value = null
  editStructureMode.value = false
}

function initPreview(initialStructureJson?: string): void {
  destroyPreview()
  if (!previewRef.value || !formConfig.value) return

  let initData: unknown
  if (initialStructureJson && initialStructureJson.trim()) {
    try {
      initData = JSON.parse(initialStructureJson)
    } catch {
      initData = buildDefaultSampleTree()
    }
  } else {
    initData = buildDefaultSampleTree()
  }

  previewInstance = new MindMap({
    el: previewRef.value,
    data: initData as never,
    theme: 'classic',
    layout: 'mindMap',
    draggable: false,
    contextMenu: false,
    toolBar: false,
    enableFreeDrag: false,
    scrollbarStyle: 'thin',
    minScale: 0.2,
    maxScale: 2
  })

  // 首次渲染后应用样式配置 + 居中
  const onFirstRender = () => {
    previewInstance?.off('node_tree_render_end', onFirstRender)
    if (formConfig.value) {
      previewInstance?.setThemeConfig(formConfig.value, false)
    }
    const root = previewInstance?.renderer?.root
    if (root) {
      ;(previewInstance?.renderer as any)?.moveNodeToCenter(root)
    }
  }
  previewInstance.on('node_tree_render_end', onFirstRender)

  // 选中节点追踪（用于结构编辑模式的增删）
  previewInstance.on('node_active', (...args: unknown[]) => {
    const activeNodeList = args[1] as Array<{ nodeData?: { id?: string } }> | undefined
    if (activeNodeList && activeNodeList.length > 0) {
      selectedNodeId.value = activeNodeList[0]?.nodeData?.id ?? null
    } else {
      selectedNodeId.value = null
    }
  })
}

// ---------- 样式表单变更 → 实时应用到预览 ----------

function applyConfigToPreview(): void {
  if (!previewInstance || !formConfig.value) return
  previewInstance.setThemeConfig(formConfig.value)
}

watch(
  () => formConfig.value,
  () => {
    applyConfigToPreview()
  },
  { deep: true }
)

// ---------- 从预设加载 ----------

function loadFromPreset(): void {
  const preset = THEMES.find((t) => t.id === presetId.value) ?? THEMES[0]
  formConfig.value = JSON.parse(JSON.stringify(preset.config)) as MindMapThemeConfig
  message.success(`已加载预设「${preset.name}」，可在此基础上微调`)
}

// ---------- 结构编辑模式：增删节点 ----------

function toggleStructureMode(): void {
  editStructureMode.value = !editStructureMode.value
  if (editStructureMode.value && previewInstance) {
    previewInstance.draggable = false
  }
}

function addChildNode(): void {
  if (!previewInstance) return
  const activeNodes = (previewInstance as any).renderer?.activeNodeList as any[] | undefined
  const target = activeNodes && activeNodes.length > 0 ? activeNodes[0] : null
  if (!target) {
    message.warning('请先选中一个节点')
    return
  }
  ;(previewInstance as any).execCommand?.('INSERT_CHILD')
  exportStructure()
}

function addSiblingNode(): void {
  if (!previewInstance) return
  const activeNodes = (previewInstance as any).renderer?.activeNodeList as any[] | undefined
  const target = activeNodes && activeNodes.length > 0 ? activeNodes[0] : null
  if (!target) {
    message.warning('请先选中一个节点')
    return
  }
  // 根节点没有同级
  if (target.isRoot) {
    message.warning('根节点没有同级')
    return
  }
  ;(previewInstance as any).execCommand?.('INSERT_SIBLING')
  exportStructure()
}

function deleteNode(): void {
  if (!previewInstance) return
  const activeNodes = (previewInstance as any).renderer?.activeNodeList as any[] | undefined
  const target = activeNodes && activeNodes.length > 0 ? activeNodes[0] : null
  if (!target) {
    message.warning('请先选中要删除的节点')
    return
  }
  if (target.isRoot) {
    message.warning('不能删除根节点')
    return
  }
  ;(previewInstance as any).execCommand?.('REMOVE_NODE')
  exportStructure()
}

/** 导出当前预览实例的节点结构 JSON（用于保存时提交） */
function exportStructure(): string {
  if (!previewInstance) return ''
  try {
    const data = (previewInstance as any).getData?.()
    return data ? JSON.stringify(data) : ''
  } catch {
    return ''
  }
}

// ---------- 保存 ----------

function buildSwatchJson(): string {
  if (!formConfig.value) return ''
  const swatch = {
    rootFill: formConfig.value.root.fillColor,
    secondFill: formConfig.value.second.fillColor,
    lineColor: formConfig.value.lineColor,
    bg: formConfig.value.backgroundColor
  }
  return JSON.stringify(swatch)
}

async function submitSave(): Promise<void> {
  const name = formName.value.trim()
  if (!name) {
    message.warning('请输入模板名称')
    return
  }
  if (!formConfig.value) {
    message.warning('样式配置缺失')
    return
  }
  // 等待节点文本编辑收尾（如果有正在编辑的文本）
  await nextTick()
  const initialStructure = exportStructure()

  submitting.value = true
  try {
    const payload: templatesApi.TemplateCreatePayload = {
      name,
      description: formDescription.value.trim() || undefined,
      sortOrder: formSortOrder.value,
      isEnabled: formEnabled.value,
      configJson: JSON.stringify(formConfig.value),
      initialStructureJson: initialStructure,
      swatchJson: buildSwatchJson()
    }
    if (editingId.value) {
      await store.update(editingId.value, payload)
      message.success('已保存')
    } else {
      await store.create(payload)
      message.success('已创建')
    }
    closeEdit()
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    submitting.value = false
  }
}

async function confirmToggleEnabled(row: AdminTemplateListItem): Promise<void> {
  try {
    await store.update(row.id, { isEnabled: !row.isEnabled })
    message.success(row.isEnabled ? '已禁用' : '已启用')
  } catch (e) {
    message.error((e as Error).message)
  }
}

function confirmDelete(row: AdminTemplateListItem): void {
  deleteTarget.value = row
  deleteModalVisible.value = true
}

async function submitDelete(): Promise<void> {
  if (!deleteTarget.value) return
  deleteSubmitting.value = true
  try {
    await store.remove(deleteTarget.value.id)
    message.success('已删除')
    deleteModalVisible.value = false
  } catch (e) {
    message.error((e as Error).message)
  } finally {
    deleteSubmitting.value = false
  }
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function renderSwatch(row: AdminTemplateListItem) {
  const sw = templatesApi.parseSwatch(row.swatchJson)
  if (!sw) return h('span', { style: 'color: var(--app-text-secondary)' }, '—')
  return h('div', { style: 'display:flex; gap:2px;' }, [
    h('span', { style: `width:14px;height:14px;border-radius:3px;background:${sw.rootFill};display:inline-block;border:1px solid rgba(0,0,0,.1)` }),
    h('span', { style: `width:14px;height:14px;border-radius:3px;background:${sw.secondFill};display:inline-block;border:1px solid rgba(0,0,0,.1)` }),
    h('span', { style: `width:14px;height:14px;border-radius:3px;background:${sw.lineColor};display:inline-block;border:1px solid rgba(0,0,0,.1)` }),
    h('span', { style: `width:14px;height:14px;border-radius:3px;background:${sw.bg};display:inline-block;border:1px solid rgba(0,0,0,.1)` })
  ])
}

const columns = computed<DataTableColumns<AdminTemplateListItem>>(() => [
  { title: '名称', key: 'name', minWidth: 140, ellipsis: { tooltip: true } },
  { title: '描述', key: 'description', minWidth: 180, ellipsis: { tooltip: true },
    render: (row) => row.description || '—' },
  { title: '色板', key: 'swatch', width: 90, render: (row) => renderSwatch(row) },
  { title: '排序', key: 'sortOrder', width: 70, align: 'center' },
  {
    title: '状态', key: 'isEnabled', width: 90,
    render: (row) =>
      row.isEnabled
        ? h(NTag, { type: 'success', size: 'small' }, () => '启用')
        : h(NTag, { type: 'default', size: 'small' }, () => '禁用')
  },
  { title: '创建人', key: 'createdByName', width: 100, ellipsis: { tooltip: true } },
  { title: '更新时间', key: 'updatedAt', width: 150, render: (row) => formatDate(row.updatedAt) },
  {
    title: '操作', key: 'actions', width: 220, fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 4 }, () => [
        h(NButton, { size: 'tiny', quaternary: true, type: 'primary', onClick: () => openEdit(row) }, () => '编辑'),
        h(NButton, {
          size: 'tiny', quaternary: true,
          type: row.isEnabled ? 'error' : 'success',
          onClick: () => confirmToggleEnabled(row)
        }, () => row.isEnabled ? '禁用' : '启用'),
        h(NButton, { size: 'tiny', quaternary: true, type: 'error', onClick: () => confirmDelete(row) }, () => '删除')
      ])
  }
])

onBeforeUnmount(() => {
  destroyPreview()
})
</script>

<template>
  <div class="admin-templates">
    <div class="page-title">
      <h2>模板管理</h2>
      <span class="hint">共 {{ store.adminTotal }} 个模板</span>
    </div>

    <NSpace class="filter-bar" align="center" :wrap="true" :size="8">
      <NSelect v-model:value="scope" :options="scopeOptions" size="small" style="width: 140px" />
      <NInput v-model:value="keyword" size="small" clearable placeholder="搜索名称/描述" style="width: 240px"
        @keyup.enter="applySearch" />
      <NButton size="small" type="primary" @click="applySearch">搜索</NButton>
      <NButton size="small" type="success" @click="openCreate">+ 新建模板</NButton>
    </NSpace>

    <NDataTable :columns="columns" :data="store.adminItems" :loading="loading" :bordered="false" :single-line="false"
      size="small" :scroll-x="1100" />

    <div class="pagination-wrap">
      <NPagination v-model:page="page" :page-size="pageSize" :item-count="store.adminTotal" :page-sizes="[10, 20, 50]"
        show-size-picker show-quick-jumper @update:page="(p) => { page = p; store.gotoAdminPage(p) }"
        @update:page-size="(s) => { pageSize = s; page = 1; store.gotoAdminPage(1) }" />
    </div>

    <!-- 编辑弹窗 -->
    <NModal v-model:show="editVisible" :mask-closible="false" :close-on-esc="false" display-directive="if"
      @after-leave="closeEdit" class="edit-modal">
      <div class="edit-shell">
        <div class="edit-header">
          <span class="edit-title">{{ editingId ? '编辑模板' : '新建模板' }}</span>
          <NButton size="small" quaternary @click="closeEdit">关闭</NButton>
        </div>

        <div class="edit-body">
          <!-- 左侧：表单 -->
          <div class="edit-left">
            <NCollapse :default-expanded-names="['basic', 'preset', 'level1', 'level2', 'level3', 'level4', 'line', 'bg']">
              <NCollapseItem title="基本信息" name="basic">
                <div class="form-row">
                  <label>模板名称</label>
                  <NInput v-model:value="formName" size="small" placeholder="如：商务蓝" maxlength="64" />
                </div>
                <div class="form-row">
                  <label>描述</label>
                  <NInput v-model:value="formDescription" type="textarea" size="small" :rows="2" placeholder="模板用途说明"
                    maxlength="512" />
                </div>
                <div class="form-row inline">
                  <label>排序值</label>
                  <NInputNumber v-model:value="formSortOrder" size="small" :min="0" style="width: 120px" />
                </div>
                <div class="form-row inline">
                  <label>启用</label>
                  <NSwitch v-model:value="formEnabled" size="small" />
                </div>
              </NCollapseItem>

              <NCollapseItem title="从预设加载（起点）" name="preset">
                <div class="form-row inline">
                  <NSelect v-model:value="presetId" :options="presetOptions" size="small" style="width: 180px" />
                  <NButton size="small" type="primary" @click="loadFromPreset">加载</NButton>
                </div>
                <p class="tip">选择一个内置主题作为起点，再在下方逐项微调。</p>
              </NCollapseItem>

              <NCollapseItem v-for="(lk, idx) in levelKeys" :key="lk"
                :title="`${idx + 1}.${levelLabels[lk]}`" :name="`level${idx + 1}`">
                <div v-if="formConfig" class="level-form">
                  <div class="form-row inline">
                    <label>形状</label>
                    <NSelect v-model:value="(formConfig[lk] as NodeLevelStyle).shape" :options="shapeOptions"
                      size="small" style="width: 160px" />
                  </div>
                  <div class="form-row inline">
                    <label>填充色</label>
                    <NColorPicker v-model:value="(formConfig[lk] as NodeLevelStyle).fillColor" size="small"
                      :modes="['hex']" />
                  </div>
                  <div class="form-row inline">
                    <label>文字色</label>
                    <NColorPicker v-model:value="(formConfig[lk] as NodeLevelStyle).color" size="small"
                      :modes="['hex']" />
                  </div>
                  <div class="form-row inline">
                    <label>边框色</label>
                    <NColorPicker v-model:value="(formConfig[lk] as NodeLevelStyle).borderColor" size="small"
                      :modes="['hex']" />
                  </div>
                  <div class="form-row inline">
                    <label>边框宽</label>
                    <NInputNumber v-model:value="(formConfig[lk] as NodeLevelStyle).borderWidth" size="small"
                      :min="0" :max="10" style="width: 120px" />
                  </div>
                  <div class="form-row inline">
                    <label>边框样式</label>
                    <NSelect v-model:value="(formConfig[lk] as NodeLevelStyle).borderDasharray"
                      :options="borderDashOptions" size="small" style="width: 160px" />
                  </div>
                  <div class="form-row inline">
                    <label>圆角</label>
                    <NInputNumber v-model:value="(formConfig[lk] as NodeLevelStyle).borderRadius" size="small"
                      :min="0" :max="50" style="width: 120px" />
                  </div>
                  <div class="form-row inline">
                    <label>字号</label>
                    <NInputNumber v-model:value="(formConfig[lk] as NodeLevelStyle).fontSize" size="small" :min="8"
                      :max="48" style="width: 120px" />
                  </div>
                  <div class="form-row inline">
                    <label>字重</label>
                    <NSelect v-model:value="(formConfig[lk] as NodeLevelStyle).fontWeight"
                      :options="fontWeightOptions" size="small" style="width: 160px" />
                  </div>
                </div>
              </NCollapseItem>

              <NCollapseItem title="连线样式" name="line">
                <div v-if="formConfig" class="level-form">
                  <div class="form-row inline">
                    <label>线色</label>
                    <NColorPicker v-model:value="formConfig.lineColor" size="small" :modes="['hex']" />
                  </div>
                  <div class="form-row inline">
                    <label>线宽</label>
                    <NInputNumber v-model:value="formConfig.lineWidth" size="small" :min="0.5" :max="10" :step="0.5"
                      style="width: 120px" />
                  </div>
                  <div class="form-row inline">
                    <label>线型</label>
                    <NSelect v-model:value="formConfig.lineStyle" :options="lineStyleOptions" size="small"
                      style="width: 160px" />
                  </div>
                  <div class="form-row inline">
                    <label>线虚线</label>
                    <NSelect v-model:value="formConfig.lineDasharray" :options="borderDashOptions" size="small"
                      style="width: 160px" />
                  </div>
                </div>
              </NCollapseItem>

              <NCollapseItem title="背景" name="bg">
                <div v-if="formConfig" class="level-form">
                  <div class="form-row inline">
                    <label>背景色</label>
                    <NColorPicker v-model:value="formConfig.backgroundColor" size="small" :modes="['hex']" />
                  </div>
                </div>
              </NCollapseItem>
            </NCollapse>
          </div>

          <!-- 右侧：实时预览 + 结构编辑 -->
          <div class="edit-right">
            <div class="preview-toolbar">
              <NButton size="tiny" :type="editStructureMode ? 'primary' : 'default'" @click="toggleStructureMode">
                {{ editStructureMode ? '退出结构编辑' : '编辑初始结构' }}
              </NButton>
              <template v-if="editStructureMode">
                <NButton size="tiny" quaternary type="primary" @click="addChildNode">+ 子节点</NButton>
                <NButton size="tiny" quaternary type="primary" @click="addSiblingNode">+ 同级</NButton>
                <NButton size="tiny" quaternary type="error" @click="deleteNode">删除</NButton>
              </template>
              <span class="tip-inline">双击节点可编辑文字</span>
            </div>
            <div ref="previewRef" class="preview-canvas"></div>
          </div>
        </div>

        <div class="edit-footer">
          <NButton size="small" @click="closeEdit">取消</NButton>
          <NButton size="small" type="primary" :loading="submitting" @click="submitSave">保存</NButton>
        </div>
      </div>
    </NModal>

    <!-- 删除确认 -->
    <NModal v-model:show="deleteModalVisible" preset="dialog" type="warning" title="删除模板" positive-text="确认删除"
      negative-text="取消" :positive-button-props="{ type: 'error', loading: deleteSubmitting }"
      display-directive="if" style="max-width: 460px" @positive-click="submitDelete">
      <p style="margin:0">
        确认删除模板「<b>{{ deleteTarget?.name }}</b>」？已使用该模板的导图不受影响，但用户将不再能选择此模板。
      </p>
    </NModal>
  </div>
</template>

<style scoped lang="scss">
.admin-templates {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.page-title {
  display: flex;
  align-items: baseline;
  gap: 12px;

  h2 {
    margin: 0;
    font-size: 20px;
    font-weight: 600;
  }

  .hint {
    font-size: 12px;
    color: var(--app-text-secondary);
    margin-left: auto;
  }
}

.filter-bar {
  flex-wrap: wrap;
}

.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}

// 编辑弹窗
.edit-modal {
  width: 92vw;
  max-width: 1280px;
  height: 88vh;
}

.edit-shell {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--app-card-bg);
  border-radius: 8px;
  overflow: hidden;
}

.edit-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--app-border);
  flex-shrink: 0;

  .edit-title {
    font-size: 16px;
    font-weight: 600;
  }
}

.edit-body {
  flex: 1;
  display: flex;
  gap: 12px;
  padding: 12px;
  overflow: hidden;
}

.edit-left {
  width: 380px;
  flex-shrink: 0;
  overflow-y: auto;
  padding-right: 4px;
}

.edit-right {
  flex: 1;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--app-border);
  border-radius: 6px;
  overflow: hidden;
  background: #fafafa;
}

.preview-toolbar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  border-bottom: 1px solid var(--app-border);
  background: var(--app-card-bg);
  flex-shrink: 0;

  .tip-inline {
    margin-left: auto;
    font-size: 12px;
    color: var(--app-text-secondary);
  }
}

.preview-canvas {
  flex: 1;
  min-height: 0;
  position: relative;
}

.edit-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid var(--app-border);
  flex-shrink: 0;
}

// 表单
.form-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;

  label {
    font-size: 12px;
    color: var(--app-text-secondary);
  }

  &.inline {
    flex-direction: row;
    align-items: center;
    gap: 8px;

    label {
      width: 64px;
      flex-shrink: 0;
    }
  }
}

.level-form {
  display: flex;
  flex-direction: column;
}

.tip {
  margin: 6px 0 0;
  font-size: 12px;
  color: var(--app-text-secondary);
}

@media (max-width: 767px) {
  .edit-modal {
    width: 100vw;
    max-width: 100vw;
    height: 100vh;
  }

  .edit-body {
    flex-direction: column;
  }

  .edit-left {
    width: 100%;
    max-height: 40%;
  }
}
</style>
