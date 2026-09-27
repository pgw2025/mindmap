# 阶段 2：样式/内容画布优先（首个改 handler 的阶段，风险最低）

> 目标：`handleUpdateStyle`（含多选批量）与 `handleContentSave` 从
> 「store 直写 + reloadMindMap」改为「画布命令 → detail 增量落库」，
> 并删除两处 `reloadMindMap()` 调用。
> 前置：阶段 1 全部任务完成（样式分支 + 墨色抑制 + debounce + 批量抑制标志）。

---

## 0. 现状分析（源码证据）

### 0.1 handleUpdateStyle（MindMapEditorView.vue ~956-982）

```ts
async function handleUpdateStyle(payload: NodeUpdatePayload) {
  if (!selectedNodeId.value) return
  try {
    // 多选（>1）时走批量更新，单次请求 + 单条撤销历史
    const ids = activeNodeIds.value.length > 1 ? activeNodeIds.value : [selectedNodeId.value]
    if (ids.length > 1) {
      const items: NodeBatchItem[] = ids.map((id) => ({ id, color: payload.color, /* 9 字段 */ }))
      await nodesStore.batchUpdate(items)
    } else {
      await nodesStore.update(selectedNodeId.value, payload)
    }
    reloadMindMap()          // ← 全量重建，视口重置、闪烁
  } catch (e) { message.error(...) }
}
```

痛点：样式根本没写入画布节点 —— 视觉更新 100% 依赖 reload；
等待后端 RTT 后才重绘，操作延迟 = RTT + 全量 render。

### 0.2 handleContentSave（~985-993）

`NodeContentModal` 提交的 payload 实际只有 `{ title, content }`
（NodeContentModal.vue:34-38）：title 是画布 text（需 icon 前缀），content 是
富文本正文（后端字段，画布不渲染）。现状走 `store.update` + `reloadMindMap`。

### 0.3 关键机制：node 实例方法是命令包装

`nodeCommandWraps.js`：`node.setData` → `execCommand('SET_NODE_DATA')`、
`node.setStyles` → `execCommand('SET_NODE_STYLES')`、`node.setText` →
`execCommand('SET_NODE_TEXT')`。**通过 node 实例调用必然触发 detail**
（exec → addHistory → emitDataUpdatesEvent）。
反之，直接调 `renderer.setNodeStyles(node, style)`（Render.js:1625，绕过
execCommand）只做 `setNodeDataRender` —— **不触发 detail、不落库**。
本阶段一律使用 node 实例方法或显式 `execCommand`，严禁直接调 renderer 方法。

### 0.4 多选批量为何要抑制 + 单次 batchUpdate

对 N 个激活节点逐个 `node.setStyles()` = N 次 `execCommand` =
N 次 `addHistory`（每次全树 `JSON.stringify`，Command.js:112-113）+
N 次全树 diff + N 条 detail update。若不抑制，会触发 N 个
`scheduleStyleUpdate` 防抖请求。复用 `collapseBatchSyncActive` 的既有模式
（useMindMapSync.ts:802-904）：置 `styleBatchSyncActive` → 命令循环 →
调用方单次 `batchUpdate` → 600ms 后复位。请求次数、undo 历史
（batchUpdate 单条 pushHistory，nodes.ts:162）与现状完全一致。

## 1. 涉及文件

| 文件 | 改动 |
|---|---|
| `frontend/src/views/editor/MindMapEditorView.vue` | 任务 2.1 / 2.2 / 2.3 |
| `frontend/src/views/editor/composables/useMindMapSync.ts` | 任务 2.0（导出 `findRenderNodeByUid`、`styleBatchSyncActive` 控制函数） |
| `frontend/src/themes/nodeInk.ts` | 注释修正（见阶段 1 任务 1.3 注意事项 4） |

## 2. 任务分解

### 任务 2.0：useMindMapSync 补充导出（前置微改）

**位置**：`useMindMapSync.ts` return 块（~1421-1442）。

**新增导出**（示意）：

```ts
return {
  // ... 既有导出
  findRenderNodeByUid,        // 既有内部函数（~1290），阶段 2/3/4 都要用
  /** 多选批量样式窗口：置位期间 handleUpdate 样式分支跳过，由调用方单次 batchUpdate */
  beginStyleBatch, endStyleBatch
}

/** 置位/复位 styleBatchSyncActive（阶段 1 已声明标志位） */
function beginStyleBatch() { styleBatchSyncActive = true }
function endStyleBatch() {
  // 覆盖 addHistory 派发窗口（detail emit 同步，但保险起见与 collapse 模式一致）
  setTimeout(() => { styleBatchSyncActive = false }, 600)
}
```

### 任务 2.1：handleUpdateStyle 改造（样式面板 → 画布命令）

**位置**：`MindMapEditorView.vue` ~956-982，整函数替换。

**目标代码**（示意）：

```ts
/** NodeUpdatePayload（后端字段）→ simple-mind-map 样式字段（库字段） */
function payloadToLibStyle(payload: NodeUpdatePayload): Record<string, unknown> {
  const s: Record<string, unknown> = {}
  if (payload.color != null) s.color = payload.color
  if (payload.fontSize != null) s.fontSize = payload.fontSize
  if (payload.fontFamily != null) s.fontFamily = payload.fontFamily
  if (payload.shape != null && payload.shape in shapeMap) s.shape = shapeMap[payload.shape]
  if (payload.backgroundColor != null) s.fillColor = payload.backgroundColor
  if (payload.borderColor != null) s.borderColor = payload.borderColor
  if (payload.edgeColor != null) s.lineColor = payload.edgeColor
  if (payload.edgeStyle != null) s.lineDasharray = edgeStyleMap[payload.edgeStyle]
  return s
}

async function handleUpdateStyle(payload: NodeUpdatePayload) {
  if (!selectedNodeId.value) return
  try {
    const ids = activeNodeIds.value.length > 1 ? activeNodeIds.value : [selectedNodeId.value]

    // 1. 画布优先：对每个激活节点写入渲染数据（node.setStyles = execCommand → detail）
    beginStyleBatch()
    let wrote = 0
    for (const id of ids) {
      const rn = findRenderNodeByUid(id)
      if (!rn) continue
      rn.setStyles(payloadToLibStyle(payload))   // 即时局部重绘，视口不动
      wrote++
    }

    // 2. icon 前缀联动（任务 2.3）：icon 变化要同时改 text 前缀
    if (payload.icon !== undefined) {
      applyIconToActiveNodes(ids, payload.icon)
    }

    // 3. 落库：多选单次 batchUpdate（单条 undo 历史，与现状一致）；
    //    单选走增量分支即可 —— styleBatchSyncActive 窗口内由这里显式落库
    const items: NodeBatchItem[] = ids.map((id) => ({
      id,
      ...(payload.color != null ? { color: payload.color } : {}),
      ...(payload.icon !== undefined ? { icon: payload.icon } : {}),
      // ... 其余 9 字段按 payload 透传（与现状 items 构造逐字段一致）
    }))
    if (items.length === 0 || wrote === 0) { endStyleBatch(); return }
    if (items.length > 1) {
      await nodesStore.batchUpdate(items)
    } else {
      await nodesStore.update(items[0].id, /* 单选完整 payload */)
    }
    endStyleBatch()
    // ← 无 reloadMindMap：画布已在第 1 步即时更新
  } catch (e) {
    endStyleBatch()
    message.error((e as Error).message)
  }
}
```

**注意事项**：

1. **落库路径的语义选择**：这里选择「抑制窗口 + 显式单次落库」而非
   「依赖 detail 逐节点防抖」，理由：请求次数与 undo 粒度与现状严格一致
   （batchUpdate 单条历史），且失败提示仍是同步 try/catch（用户在面板上能看到
   message.error，与现状体验一致）。detail 的样式分支在阶段 1 已建成，此处的
   抑制使其在本路径不重复触发；它继续服务于**画布原生右键/样式弹窗等
   未来直写场景**。
2. `payloadToLibStyle` 中 shape/edgeStyle 用正向映射（后端数字 → 库字符串），
   与 `convertToMindMapData` 相同的表（shapeMap/edgeStyleMap 已在 useMindMapSync
   导出？**实施时需要把 shapeMap/edgeStyleMap 一起导出**，或把
   payloadToLibStyle 放进 useMindMapSync —— 推荐后者，映射表不外泄）。
3. `endStyleBatch` 的 600ms 延迟复位期间若用户**再次**改样式，第二次调用
   `beginStyleBatch()` 会把已置位的标志再置一次（无害）；但第二次的
   detail 会被仍在窗口内的第一次抑制错过 —— **因此第 3 步显式落库必须
   在 endStyleBatch 的延迟窗口内完成且覆盖第二次修改**。实施时保证：
   `beginStyleBatch` 采用「计数器」而非布尔（begin 次数 < end 次数期间保持抑制），
   或将窗口缩短为 detail 同步 emit 结束即复位（detail emit 是同步的，`await` 前
   已完成 —— 可直接同步复位，不需要 600ms！与 collapse 的差异：collapse 库内部
   EXPAND_ALL 是同步逐节点命令，600ms 是保守值；样式循环也是同步命令链，
   **建议 endStyleBatch 直接同步复位**，验证 2-4 覆盖「连续两次快速改样式」场景）。
   **实施时以「同步复位 + 连续操作验证」为准**，若验证发现 detail 迟到
   （addHistory 有无节流需现场确认 `Command.addHistory` 的 emit 时机），
   再退回 600ms 窗口方案。
4. `wrote === 0`（渲染节点没找到，例如 reload 窗口内）时提前返回且不落库 ——
   与现状「await store.update 落库但画布未变」相比少了落库，可接受：
   渲染节点不存在说明画布与 store 脱节，属异常态，不应继续写。
5. **readonly**：`handleUpdateStyle` 的 UI 入口（样式面板）在 readonly 下应不可达；
   实施时在函数首行加 `if (readonly.value) return` 防御（现状无，加上更稳）。

### 任务 2.2：handleContentSave 改造（内容弹窗 → SET_NODE_TEXT + content 落库）

**位置**：`MindMapEditorView.vue` ~985-993。

**现状**：`await nodesStore.update(id, payload)` + `reloadMindMap()`，
payload = `{ title, content }`。

**目标代码**（示意）：

```ts
async function handleContentSave(payload: NodeUpdatePayload) {
  if (!selectedNodeId.value || readonly.value) return
  try {
    const id = selectedNodeId.value
    const storeNode = nodesStore.findNode(id)
    const rn = findRenderNodeByUid(id)
    if (!storeNode || !rn) return

    // 1. 画布优先：title 有变化 → SET_NODE_TEXT（带 icon 前缀），即时重绘
    const newTitle = (payload.title ?? '').trim()
    if (newTitle && newTitle !== storeNode.title) {
      const icon = payload.icon ?? storeNode.icon
      rn.setText(icon ? `${icon} ${newTitle}` : newTitle)
      // detail 文本分支 → scheduleTextUpdate → extractTitleFromText 正确剥离 → update({title})
    }

    // 2. content：画布不渲染，直接落库（不依赖 detail）
    if (payload.content !== undefined) {
      await nodesStore.update(id, { content: payload.content })
    }
    // ← 无 reloadMindMap
  } catch (e) {
    message.error((e as Error).message || '保存失败')
  }
}
```

**注意事项**：

1. title 走 `rn.setText()`（= `execCommand('SET_NODE_TEXT')`，nodeCommandWraps:7-9）
   → detail 文本分支（阶段 1 前已有）→ `scheduleTextUpdate` 400ms 后
   `update({ title })`。`extractTitleFromText`（useMindMapSync:1103-1112）按
   store 节点 icon 剥离前缀 —— store.icon 尚是旧值且此处未变 icon，
   剥离正确 ✓（icon 不变的场景下前缀一致）。
2. content 与 title 的落库是**两个不同通道**（content 显式 await、title 走防抖）——
   与现状单请求不同，但 NodeContentModal 关闭即返回列表的场景下，
   `flushPendingUpdates`（路由离开时调用，既有机制）会保证 title 飞出。
   验证 2-5 覆盖「保存后立即返回列表 → 刷新」场景。
3. 弹窗自身的 `message.success('内容已保存')`（NodeContentModal.vue:40）当前是
   **乐观提示**（emit 后立即提示）—— 改造后语义不变，仍为乐观提示。
4. 若 payload 里出现 note（未来扩展），`rn.setData({ note })` 即可走既有
   note 防抖分支（参考 handleNoteChange ~1147-1172 的既有写法）。

### 任务 2.3：icon 前缀联动 `applyIconToActiveNodes`

**位置**：`MindMapEditorView.vue` 新增辅助函数（供任务 2.1 调用）。

**问题机理**：icon 在画布上以 text 前缀存在（`convertToMindMapData`:
`${icon} ${title}`），改 icon 不改 text 则画布不变；只改 text 则
`extractTitleFromText` 在落库剥离时会用 **store 的旧 icon** —— 新旧 icon 不同
时 `startsWith` 判断失败，title 会被污染成 `"🎉 标题"` 整串。

**目标代码**（示意）：

```ts
/** icon 变更联动：先就地更新 store DTO 的 icon（供 extractTitleFromText 剥离），
 *  再重写画布 text 前缀。icon 字段本身的落库由调用方的 batchUpdate/update 显式携带。 */
function applyIconToActiveNodes(ids: string[], icon: string | undefined) {
  for (const id of ids) {
    const storeNode = nodesStore.findNode(id)
    if (!storeNode) continue
    const target = icon ?? ''                       // undefined → 清空
    storeNode.icon = target || null                 // 就地更新（不调 API）
    const rn = findRenderNodeByUid(id)
    if (rn) {
      rn.setText(target ? `${target} ${storeNode.title}` : storeNode.title)
    }
  }
}
```

**落库闭环**：

- text 变化 → detail 文本分支 → `runTextUpdate` → `extractTitleFromText`
  此时读到**新** icon（第 1 步就地更新）→ 正确剥离 → `update({ title: 不变 })`
  （幂等，多发一次无害请求）。
- icon 字段由任务 2.1 第 3 步的 items/update 显式携带 → `update({ icon })`。
- 单节点场景下两条请求（title 幂等 + icon）可能合并进同一次防抖——
  `scheduleTextUpdate` 只带 title；icon 走显式 update。最终一致性 ✓。
  **可选优化**（实施时确认）：单选改 icon 时直接走
  `scheduleStyleUpdate(id, { icon })`（阶段 1 聚合分支）而不再显式 update，
  由 400ms 防抖合并 —— 验证 2-6 二选一，以请求数少者为准。

**注意事项**：

1. `storeNode.icon = target || null` 的就地写是刻意的（extractTitleFromText
   读 store），**不要**封装成 store API 调用 —— API 由落库步骤统一发。
2. 多选 + icon：现状样式面板多选时 icon 是否可改需现场确认（NodeToolbar 的
   icon 入口）；若多选入口不存在，`applyIconToActiveNodes` 的 ids 实际恒为
   单元素 —— 保留循环写法不增复杂度。
3. 新建节点（无 icon）不受影响：`convertToMindMapData` 的 `${icon} ${title}`
   仅在 icon 真值时拼接。

### 任务 2.4：nodeInk.ts 注释修正 + 死代码清理

- `nodeInk.ts:90` 注释「SET_NODE_DATA 不进撤销历史」改为
  「SET_NODE_DATA 不产生应用层历史（handleUpdate color 分支受 inkPatchActive
  抑制）；库内命令历史仍会记录，但不参与用户撤销」。
- `rg "reloadMindMap"` 确认本阶段删除了 2 处调用（handleUpdateStyle /
  handleContentSave），并在 PR 描述记录当前剩余调用点数。

## 3. 阶段验证

| # | 验证项 | 操作 | 期望 |
|---|---|---|---|
| 2-1 | 样式即时生效 | 单选节点 → 样式面板改颜色/字号/形状/边框 | 画布**立即**变化，视口/缩放/选中态不动；无闪烁；Network 400ms 内一次 `PUT /nodes/{id}`（仅变更字段） |
| 2-2 | 多选批量 | 多选 3+ 节点 → 改背景色 | 画布全部即时变化；Network **一次** batch 请求；Ctrl+Z 一次全部还原 |
| 2-3 | F5 一致性 | 完成 2-1/2-2 后 F5 | 颜色/形状/边框全部保持 |
| 2-4 | 连续快速改样式 | 单节点 1 秒内连续改 3 次颜色 | 最终颜色正确落库（最后一次为准）；无中间态覆盖；请求 ≤ 2 次（防抖合并） |
| 2-5 | 内容保存 | 弹窗改标题 + 正文 → 保存 → 立即返回列表 → 刷新 | 标题与正文都保持（flushPendingUpdates 兜底 title 防抖） |
| 2-6 | icon 修改 | 样式面板改 icon（含清空） | 画布前缀即时变化；F5 后 icon 与 title 均正确（title 不含 icon 残留） |
| 2-7 | 墨色回归 | 自定义底色节点 → 切深色 → 切浅色 → 改颜色 → 再切深色 | 墨色注入照常生效；切主题零后端请求；改颜色落库为用户值 |
| 2-8 | undo 矩阵 | 依次验证：改样式 / 多选样式 / 改内容 / 改 icon 各一次 undo | 每项一次还原，画布与后端一致（undo 后 F5 验证） |
| 2-9 | 移动端回归 | 移动端样式面板/内容弹窗重复 2-1/2-5 | 与桌面一致（handler 共用） |
| 2-10 | 离线快照 | 离线模式改样式 | 同步状态提示错误；恢复网络后重试成功；无静默丢失 |

## 4. 提交策略

单 commit：`refactor(editor): style & content updates via canvas commands instead of full reload`

## 5. 回滚策略

两个 handler 各自独立、函数级替换，revert 该 commit 即回现状；
`payloadToLibStyle/applyIconToActiveNodes` 为新增辅助函数，随 revert 一并移除。
阶段 1 的通道扩展与本阶段无耦合（本阶段若 revert，通道分支闲置但无害）。
