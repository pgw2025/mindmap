# 阶段 4：粘贴命令化 + reloadMindMap 收口

> 目标：`handlePaste` 走库命令 + 增量落库（含样式），顺带修复画布原生粘贴
> 样式丢失的现状缺陷；`reloadMindMap` 调用点收口至 4 处。
> 前置：阶段 0 / 1 / 3 完成（渲染节点查找、helper、命令模式均已就绪）。

---

## 0. 现状分析（源码证据）

### 0.1 handlePaste 现状（MindMapEditorView.vue ~1472-1502）

```ts
async function handlePaste() {
  if (!clipboardNode.value || !selectedNodeId.value) return
  const sourceNode = clipboardNode.value          // 单节点 DTO 浅拷贝（handleCopy:1466）
  const targetNode = nodesStore.findNode(selectedNodeId.value)
  // 根节点不能粘贴为同级，改为粘贴为子节点
  const parentId = targetNode.parentId ?? targetNode.id
  const payload: NodeCreatePayload = {
    parentId,
    title: `${sourceNode.title} (副本)`,
    content: sourceNode.content ?? undefined,
    note: sourceNode.note ?? undefined,
    sortOrder: getNextSortOrder(parentId),        // 末尾
    color/fontSize/shape/icon/backgroundColor/borderColor/edgeColor/edgeStyle: 源样式透传
  }
  await nodesStore.create(payload)
  reloadMindMap()
  message.success('已粘贴节点')
}
```

**现状语义**：单节点粘贴（**不含子树** —— `handleCopy` 只存单 DTO），
根节点时降级为粘贴子节点，样式字段全量透传，标题加 `(副本)` 后缀。

**范围声明**：本阶段**保持单节点粘贴语义不变**，不引入子树复制
（最小化修改原则；子树粘贴若未来需要，`handleCopy` 需递归收集子树 +
`appointChildren` 组装，另立方案）。

### 0.2 顺带修复的现状缺陷：handleCreateTree 丢弃样式字段

`useMindMapSync.ts:615-623` 的 create payload 只含
`parentId/title/sortOrder/isCollapsed/direction` —— **任何**走 detail create 的
新节点（画布 Tab/Enter、原生 Ctrl+C/V 粘贴）落库时**样式全部丢失**：

- 画布原生复制粘贴带样式的节点 → 落库是裸节点 → F5 后样式消失（**现状 bug**）；
- 阶段 4 命令化粘贴若不处理，同样丢样式。

任务 4.1 扩展 `handleCreateTree` 从 DetailNode.data 提取样式字段，
一并修复上述现状缺陷（画布原生粘贴 + 工具栏粘贴统一样式落库）。

### 0.3 appointData 的 uid 陷阱

`handleCreateTree` ~599 行：

```ts
// 已存在映射，说明是 undo 恢复？直接跳过
if (uidToBackendId.has(uid)) continue
```

`uidToBackendId` 初始化时以「全部后端 ID」为 key 登记
（`convertToMindMapData` ~477：`uidToBackendId.set(n.id, n.id)`）。
**若粘贴的 appointData 带上源节点的 uid/backendId（= 后端 ID），
detail create 到达时 `uidToBackendId.has(源id)` 命中 → 跳过创建 → 粘贴无效果！**

因此任务 1.1 的 `dtoToNodeData` 在粘贴场景必须 `withUid=false`，
让库 `createUid` 为新节点生成全新 uid。

## 1. 涉及文件

| 文件 | 改动 |
|---|---|
| `frontend/src/views/editor/MindMapEditorView.vue` | 任务 4.1（handlePaste）、任务 4.2（收口核查） |
| `frontend/src/views/editor/composables/useMindMapSync.ts` | 任务 4.1b（handleCreateTree 样式提取） |
| `frontend/src/stores/nodes.ts` | 无改动 |

## 2. 任务分解

### 任务 4.1：handlePaste 改造

**位置**：`MindMapEditorView.vue` ~1472-1502，整函数替换。

**目标代码**（示意）：

```ts
async function handlePaste() {
  if (!clipboardNode.value || !selectedNodeId.value || readonly.value) return
  const source = clipboardNode.value
  const targetStore = nodesStore.findNode(selectedNodeId.value)
  const targetRender = findRenderNodeByUid(selectedNodeId.value)
  if (!targetStore || !targetRender) return

  // 1. 构造 appointData：复用 dtoToNodeData（withUid=false —— 见 0.3 uid 陷阱），
  //    标题加 (副本)，content/note 之外仅保留画布可渲染的样式字段
  const appointData = dtoToNodeData(
    { ...source, title: `${source.title} (副本)` },
    { withUid: false }        // 不带 uid/id/backendId → 库生成新 uid → 正常走 create
  )
  // dtoToNodeData 已生成 text（含 icon 前缀）；追加内容字段为空态默认
  // expand 默认 true（dtoToNodeData 按 isCollapsed 计算，粘贴节点默认展开）

  // 2. 根节点目标 → 粘贴为子节点（现状降级语义）；否则粘贴为目标节点的同级
  if (targetStore.parentId == null) {
    mindMapInstance!.execCommand('INSERT_CHILD_NODE', false, [targetRender], appointData)
  } else {
    mindMapInstance!.execCommand('INSERT_NODE', false, [targetRender], appointData)
  }
  // ← 无 store.create / getNextSortOrder / reloadMindMap
  // 落库：detail create → handleCreateTree（任务 4.1b 已补样式提取）→ 异步 create
  message.success('已粘贴节点')   // 乐观提示（与现状一致；画布已即时出现）
}
```

**注意事项**：

1. **必须先完成任务 4.1b**（handleCreateTree 样式提取），否则粘贴的样式落库丢失
   —— 实施顺序上 4.1b 先于 4.1。
2. `dtoToNodeData` 会把 `content`（富文本正文）丢弃 —— content 不渲染画布，
   但现状粘贴会带 content。**落库 content 需要 handleCreateTree 支持**：
   DetailNode.data 不含 content（convertToMindMapData 从不写 content 字段），
   增量通道**无法**携带 content —— 两个选择：
   - A（推荐，最小改动）：粘贴后补一次显式 `nodesStore.update(新节点backendId, { content })`
     —— 新节点 backendId 需 await create 完成（`getBackendIdOrWait`）；
     实现为：粘贴命令后 `enqueueStructuralOp` 中
     `const bid = await waitForNodeCreate(uid)`… 实际取 uid：命令返回值/新节点
     可通过 `insertNode` 后的 `afterExecCommand` 事件或渲染树新增节点 diff 拿到。
     **简化实现**：detail create 分支扩展 —— 在 appointData 中**额外塞一个
     自定义透传字段** `__pasteContent`，`handleCreateTree` create payload 中
     读取并入库后从 data 中删除（`writeBackendIdToNode` 链路外，用
     `rn.setData({ __pasteContent: undefined })` 清理）。该方案让 content 走
     同一条 create 请求，无二次请求。
   - B（更干净，超范围）：后端 create 接口本就支持 content 字段 ——
     `handleCreateTree` 无从拿到，故只能走 A。
   **决策：采用 A（appointData 透传字段）**，在 `handleCreateTree` 提取样式时
   一并读取 `__pasteContent` → create payload `content` 字段，成功后从渲染 data
   清除透传字段（`node.setData({ __pasteContent: undefined })`，
   detail diff 对 undefined 清除不敏感 —— isSameObject 深比较后无落库副作用）。
   note 同理（现状粘贴带 note，convertToMindMapData 有 note 字段 →
   DetailNode.data.note 有值 → 4.1b 提取 note 直接支持 ✓ 无需透传）。
3. 根节点目标降级：`targetStore.parentId == null` → INSERT_CHILD_NODE ——
   与现状 `parentId = targetNode.parentId ?? targetNode.id` 语义一致。
4. 粘贴位置：`INSERT_NODE` 在目标正下方（现状末尾）—— 行为变化同阶段 3
   0.4 声明（与画布一致方向），实施前统一确认。
5. `message.success` 保留为乐观提示（画布已即时出现，提示符合直觉）。

### 任务 4.1b：handleCreateTree 提取样式/内容字段（现状缺陷修复）

**位置**：`useMindMapSync.ts` `handleCreateTree` ~615-631（createPromise 构造段）。

**目标代码**（示意）：

```ts
const createPromise = (async (): Promise<string> => {
  try {
    const created = await nodesStore.create({
      parentId: pId,
      title,
      sortOrder: task.sortOrder,
      isCollapsed: task.node.data.expand === false,
      direction,
      // —— 新增：样式字段提取（detail create 落库样式；修复画布原生粘贴丢样式缺陷）——
      ...(extractStyleFromDetailData(task.node.data)),
      // note：convertToMindMapData 映射链已带（画布原生粘贴/工具栏粘贴均有效）
      ...(task.node.data.note ? { note: task.node.data.note } : {}),
      // __pasteContent：工具栏粘贴的正文透传（任务 4.1 注意事项 2）
      ...((task.node.data as any).__pasteContent != null
        ? { content: (task.node.data as any).__pasteContent } : {})
    })
    writeBackendIdToNode(uid, created.id)
    writeBackendIdToDetailNode(task.node, created.id)
    // 清理透传字段（不留在渲染数据，避免序列化/导出污染）
    if ((task.node.data as any).__pasteContent != null) {
      const rn = findRenderNodeByUid(uid)
      rn?.setData({ __pasteContent: undefined })
    }
    return created.id
  } finally {
    pendingCreates.delete(uid)
  }
})()
```

**`extractStyleFromDetailData`（新增辅助，示意）**：

```ts
/** DetailNode.data（库字段）→ create payload 样式字段（后端字段）。
 *  与 handleUpdate 样式分支（阶段 1 任务 1.2）共用同一张映射表，
 *  避免两处映射漂移。 */
function extractStyleFromDetailData(d: DetailNode['data']): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  if (d.color) out.color = d.color
  if (d.fontSize != null) out.fontSize = d.fontSize
  if (d.fontFamily) out.fontFamily = d.fontFamily
  if (d.shape != null && d.shape in shapeStrToNum) out.shape = shapeStrToNum[d.shape as string]
  if (d.fillColor) out.backgroundColor = d.fillColor
  if (d.borderColor) out.borderColor = d.borderColor
  if (d.lineColor) out.edgeColor = d.lineColor
  if (d.lineDasharray != null && d.lineDasharray in dashToEdgeStyle) {
    out.edgeStyle = dashToEdgeStyle[d.lineDasharray]
  }
  return out
}
```

**注意事项**：

1. **icon 无法走此通道**：icon 在画布上是 text 前缀，detail create 的 title
   经 `extractTitleFromText`（无 backendId，用 text 原样，~1106-1108）
   —— 若源节点带 icon，text=`"🎉 标题 (副本)"`，剥离失败 → **title 污染**。
   解决：`extractTitleFromText(text)` 依赖 store icon，而新节点 store 尚无记录。
   **方案**：`handleCreateTree` 内 title 提取前，用正则剥离已知 emoji 前缀？
   —— 不可靠。**推荐方案**：粘贴 appointData 的 text 不带 icon 前缀
   （`dtoToNodeData` 产物再处理：`data.text = source.title`），
   **icon 放进透传字段 `__pasteIcon`**（同 content 方案）→ create payload
   `icon: __pasteIcon` + title 无前缀 → 落库正确；
   画布上副本节点无 icon 前缀 —— 与 title 落库一致
   （**画布副本显示与库内 title/icon 分离的规则一致：F5 后 icon 重新拼前缀**）。
   **副作用声明**：粘贴瞬间副本节点画布上不显示 icon，落库后 F5 才显示 ——
   不理想。**替代方案**：appointData.text 带 icon 前缀（画布即时正确）+
   透传 `__pasteIcon` + `handleCreateTree` 中 title 提取优先用 `__pasteIcon`
   剥离（`extractTitleFromText` 增加可选 `explicitIcon` 参数）→ 画布正确 +
   落库正确。**采用替代方案**，改动集中在 extractTitleFromText 一个可选参数。
2. 映射表与阶段 1 任务 1.2 的 `styleFields` 共用（同一份 lib↔backend 表），
   防止两处映射漂移 —— 实施时把表提取为模块级常量。
3. 该修复同时作用于画布原生 Ctrl+C/V 粘贴（现状丢样式）—— 验证 4-4 覆盖，
   作为缺陷修复在 PR 描述中注明。

### 任务 4.2：reloadMindMap 收口核查

**位置**：全仓 grep。

**执行清单**：

```powershell
rg -n "reloadMindMap\(" frontend/src
```

**预期最终调用点（≤4 处语义场景）**：

| 调用点 | 文件位置 | 保留理由 |
|---|---|---|
| `handleUndo` | MindMapEditorView.vue ~1453 | 应用栈回滚 → 需整体重建 |
| `handleRedo` | MindMapEditorView.vue ~1458 | 同上 |
| `handleVersionRollback` | MindMapEditorView.vue ~1001 | 版本回滚 → 后端状态整体替换 |
| 空导图初始化分支 | MindMapEditorView.vue（convertToMindMapData 返回 null 的兜底 / 首节点创建路径） | 空图无渲染树，只能整体 setData |

若 rg 结果超出上表（如分享页/组件复用），逐个判断：
- 语义属于「整体重建」→ 保留并补入上表；
- 属于普通编辑后刷新 → **遗漏的改造点**，回到对应阶段补改造。

**同时确认**：`getNextSortOrder`（~871-875）在粘贴改造后无调用方 → 删除；
`NodeCreatePayload` 的直接构造仅剩空图初始化（若有）。

### 任务 4.3：端到端验收

按 `verification.md` 全量执行：

- 总验收标准清单（README.md §6）逐项勾选；
- 性能基准记录进 verification.md §5（改造前后数据留档）；
- 全回归矩阵通过后，在 performance-optimization-plan.html 的改造 3 条目标记
  「已完成」状态（文档更新，非代码）。

## 3. 阶段验证

| # | 验证项 | 操作 | 期望 |
|---|---|---|---|
| 4-1 | 基础粘贴 | 复制带颜色/形状/边框的节点 → 选中另一节点 → 工具栏粘贴 | 副本即时出现在目标正下方，样式完整；`POST create` 携带全部样式；F5 保持 |
| 4-2 | 根节点粘贴 | 复制 → 选中根 → 粘贴 | 降级为根的子节点（现状语义）；direction 正确（默认 right 布局行为） |
| 4-3 | icon/正文粘贴 | 复制带 icon+content 的节点 → 粘贴 | 副本画布即时显示 icon 前缀；落库 icon/title/content 全部正确（title 无污染） |
| 4-4 | 现状缺陷修复回归 | 画布原生 Ctrl+C（选中节点）→ Ctrl+V | F5 后样式保持（修复前丢失）——缺陷修复验证 |
| 4-5 | 收口核查 | `rg -n "reloadMindMap\(" frontend/src` | 结果与任务 4.2 预期表完全一致 |
| 4-6 | undo 粘贴 | 粘贴后 Ctrl+Z | 副本节点消失（detail delete → remove）；redo 恢复；F5 一致 |
| 4-7 | 高频粘贴 | 快速连续粘贴 5 次 | 5 个副本全部落库（opQueue 串行）；无 uid 冲突/跳过（4.1 的 uid 陷阱回归） |
| 4-8 | 移动端 | 移动端复制/粘贴入口重复 4-1 | 行为一致（handler 共用） |

## 4. 提交策略

两个 commit：

1. `fix(sync): persist style/content fields on incremental node creation`
   （任务 4.1b，独立缺陷修复，可先行合入且单独回归）
2. `refactor(editor): paste via INSERT_NODE command, finalize reloadMindMap scope`
   （任务 4.1 + 4.2 + 死代码清理）

## 5. 回滚策略

- 4.1b 是缺陷修复，**建议永久保留**（revert 会恢复丢样式缺陷）；
- 4.1 函数级替换，revert 即回现状；透传字段 `__pasteIcon/__pasteContent`
  在无粘贴场景下不会出现在任何 data 中（无残留风险）；
- 4.2 是核查任务，无代码回滚诉求。
