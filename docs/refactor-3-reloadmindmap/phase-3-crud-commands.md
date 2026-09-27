# 阶段 3：增删命令化（结构操作走库命令）

> 目标：`handleAddChild` / `handleAddSibling` / `submitNodeDelete` 从
> 「store 直写 + reloadMindMap」改为「库命令 → detail 增量落库」。
> 前置：**阶段 0（历史栈归一）+ 阶段 1（增量通道）必须完成**；
> 阶段 2 非硬前置（增删不依赖样式分支），但推荐先做 2 降低本阶段验证噪音。

---

## 0. 现状分析（源码证据）

### 0.1 现状三函数（MindMapEditorView.vue）

| 函数 | 位置 | 现状链路 |
|---|---|---|
| `handleAddChild` | ~878-899 | `getNextSortOrder`（末尾+1）→ `store.create`（**await 后端 RTT**）→ 折叠父则再 `store.update(isCollapsed)`（又一次 RTT）→ `reloadMindMap`（全量重建） |
| `handleAddSibling` | ~901-921 | 根 guard → `store.create`（末尾）→ `reloadMindMap` |
| `handleDelete` → `submitNodeDelete` | ~923-954 | 确认弹窗 → `store.remove`（**await**）→ 清选中态 → `reloadMindMap` |

新增体验延迟 = 一次（或两次）后端 RTT + 全量 render；折叠父节点场景两倍。

### 0.2 库命令事实（实施依据）

- `Render.js:254` `INSERT_NODE` → `insertNode(openEdit=true, appointNodes=[], appointData=null, appointChildren=[])`（~786）：
  在每个 appointNodes 节点**正下方**插入同级（`getNodeDataIndex` 计算位置）。
- `Render.js:260` `INSERT_CHILD_NODE` → `insertChildNode(openEdit=true, appointNodes=[], appointData=null, appointChildren=[])`（~893）。
  根节点上调用时向根添加子节点；**appointData 显式带 `dir: 'right'` 可保持
  「根下新子节点总在右侧」的现状语义**（否则交给布局器平衡，可能落左）。
- `Render.js:288` `REMOVE_NODE` → `removeNode(appointNodes=[])`（~1413）：
  默认删 `activeNodeList`（多选全部）；**传 appointNodes=[单节点] 则只删该节点**
  —— 用后者保持「确认弹窗只删目标节点」的现状语义。
- `Render.js:311` `SET_NODE_EXPAND`：`setNodeExpand(node, expand)` 内部
  `execCommand('SET_NODE_DATA', {expand})` + `render()` —— 展开父节点走此命令，
  detail 的 expand 分支（既有）负责落库 isCollapsed（注意 **取反**：
  库 `expand: true` ↔ 后端 `isCollapsed: false`）。
- 新节点 uid：库 `createUid` 生成随机 uid（appointData 不带 uid 时），
  `handleCreateTree`（useMindMapSync:570-636）负责逐层 create 并
  `writeBackendIdToNode` 回写 + `pendingCreates` 登记。

### 0.3 新节点落库位置的自动闭环（重要）

`INSERT_NODE/INSERT_CHILD_NODE` 后 detail 的同一批 diff 包含：

1. `create`（新节点子树）→ `handleCreateTree`：
   `computeSortOrderForNewNode` 给的是「父节点下当前最大 sortOrder + 1」
   —— **末尾占位**，可能与画布实际位置（选中节点正下方）不符；
2. `update`（父节点，children 数组变化）→ `handleStructuralChangesBatch`
   （useMindMapSync:962-1057）：
   - 第一遍 move 检查：新节点 parentId 未变 → 跳过；
   - 第二遍**兄弟集合重编**（~1027-1056）：受影响父节点下全部子节点按
     渲染树顺序重编 `0..n-1` 单次 batchUpdate —— **新节点的最终落库位置
     = 画布渲染位置** ✓。

该闭环是既有机制（画布 Tab/Enter 一直在用），本阶段直接受益，无需新代码。
唯一索引冲突防护（`Duplicate entry` 修复）已在第二遍重编逻辑中处理。

### 0.4 行为变化声明（需要产品/用户确认）

| 行为 | 现状 | 改造后 | 定性 |
|---|---|---|---|
| 新增子节点出现位置 | 父节点子列表**末尾** | 选中节点子列表内（唯一子节点场景等价） | 一致（子节点首插场景末尾即唯一位置） |
| 新增同级出现位置 | 兄弟列表**末尾** | 选中节点**正下方** | **变化**：与画布 Enter 行为一致（推荐接受）；若需保持末尾，用 `INSERT_AFTER` 指定末尾兄弟，见任务 3.2 备选 |
| 新增延迟 | await RTT + 全量重绘 | 即时（局部 render），落库异步 | 改善 |
| 删除反馈 | await remove 成功后提示 | 乐观消失，落库失败走全局错误提示 + 重试 | 变化（见任务 3.3） |
| 折叠父上新增子 | 先 update 展开父（RTT）再 create（RTT）再 reload | SET_NODE_EXPAND + INSERT_CHILD_NODE 两条命令即时完成 | 改善 |

## 1. 涉及文件

| 文件 | 改动 |
|---|---|
| `frontend/src/views/editor/MindMapEditorView.vue` | 任务 3.1 / 3.2 / 3.3 |
| `frontend/src/views/editor/composables/useMindMapSync.ts` | 无改动（findRenderNodeByUid 已在阶段 2 导出） |
| `frontend/src/stores/nodes.ts` | 无改动 |

## 2. 任务分解

### 任务 3.1：handleAddChild 改造

**位置**：`MindMapEditorView.vue` ~878-899，整函数替换。

**目标代码**（示意）：

```ts
async function handleAddChild() {
  if (!selectedNodeId.value || readonly.value) return
  const id = selectedNodeId.value
  const storeNode = nodesStore.findNode(id)
  if (!storeNode) return
  const parentNode = findRenderNodeByUid(id)
  if (!parentNode) return

  // 1. 折叠态父节点：先展开（SET_NODE_EXPAND → detail expand 分支落库 isCollapsed）
  //    注意取反映射：库 expand=true ↔ 后端 isCollapsed=false
  if (parentNode.getData('expand') === false) {
    mindMapInstance!.execCommand('SET_NODE_EXPAND', parentNode, true)
  }

  // 2. 根下新增保持「总在右侧」现状：appointData 显式 dir='right'
  //    （非根子节点不设 dir，继承分支方向 —— 与 convertToMindMapData 策略一致）
  const isRoot = storeNode.id === nodesStore.rootNode?.id
  const appointData: Record<string, unknown> = { text: '新子节点' }
  if (isRoot) appointData.dir = 'right'

  // 3. 画布优先：命令插入 → detail create → handleCreateTree 异步落库
  //    openEdit=false 保持现状「新增后不进入编辑」
  mindMapInstance!.execCommand('INSERT_CHILD_NODE', false, [parentNode], appointData)
  // ← 无 store.create / getNextSortOrder / reloadMindMap
}
```

**注意事项**：

1. 函数签名保持 `async`（模板调用处不变），但内部已无 await —— 保留 async
   为将来扩展留余量，或改为同步函数并同步更新模板绑定（二选一，倾向后者，
   明确「即时返回」的语义）。**实施时以最小 diff 为准**：保持 async 不改模板。
2. `dir='right'` 的落库链：appointData.dir 进新节点 data → detail create →
   `handleCreateTree` ~609-612 检测 `pId === rootNode.id` 时
   `direction = data.dir === 'left' ? 0 : 1` → direction=1 ✓（与现状
   `direction: isRootChild ? 1 : undefined` 语义一致）。
3. 空导图初始化（画布无节点时新增首个节点）现状走独立分支
   （`convertToMindMapData` 返回 null 的场景）—— 本函数的 `parentNode`
   为 null 已提前 return；空图新增入口另有 `reloadMindMap`（收口后允许保留，
   见 phase-4 任务 4.2 清单）。
4. `getNextSortOrder` 在改造后若无其他调用方，**删除**（rg 确认）；
   仍有调用方（如粘贴）则保留至阶段 4。

### 任务 3.2：handleAddSibling 改造

**位置**：`MindMapEditorView.vue` ~901-921，整函数替换。

**目标代码**（示意）：

```ts
async function handleAddSibling() {
  if (!selectedNodeId.value || readonly.value) return
  const storeNode = nodesStore.findNode(selectedNodeId.value)
  if (!storeNode?.parentId) {
    message.warning('根节点没有同级')   // 保留现状 guard
    return
  }
  const node = findRenderNodeByUid(selectedNodeId.value)
  if (!node) return

  // 根直接子节点新增保持右侧现状语义
  const isRootChild = storeNode.parentId === nodesStore.rootNode?.id
  const appointData: Record<string, unknown> = { text: '新节点' }
  if (isRootChild) appointData.dir = 'right'

  // 同级插入位置 = 选中节点正下方（与画布 Enter 一致，见 0.4 行为变化声明）
  mindMapInstance!.execCommand('INSERT_NODE', false, [node], appointData)
}
```

**注意事项**：

1. `insertNode` 内部对 `node.isRoot` 有 guard（Render.js:819：root 直接 return）——
   但根节点的 UI 入口应已禁用；现状 `!node?.parentId` guard 保留双保险。
2. **位置语义备选**（若产品要求严格保持「末尾插入」）：先找到父节点最后一个
   渲染子节点，`execCommand('INSERT_AFTER', [lastSibling], appointData)`。
   二选一在实施前与用户确认一次即可；默认推荐正下方（与画布一致，
   用户心智统一，且撤销/重编链路完全一致）。
3. 多选时 `[node]` 固定传单节点 —— 工具栏按钮的选中语义以 selectedNodeId 为准
   （首个），不批量插入（与现状一致；库默认对 activeNodeList 全部插入的行为
   被 appointNodes 覆盖，不会误插）。

### 任务 3.3：submitNodeDelete 改造

**位置**：`MindMapEditorView.vue` ~937-954，整函数替换。
`handleDelete`（弹窗触发链）不动。

**目标代码**（示意）：

```ts
async function submitNodeDelete(): Promise<boolean> {
  if (!selectedNodeId.value) return true
  const id = selectedNodeId.value
  const storeNode = nodesStore.findNode(id)
  const rn = findRenderNodeByUid(id)
  nodeDeleteSubmitting.value = false   // 乐观关闭：不再等待后端

  if (!rn || !storeNode) return true
  // 画布优先：单节点删除（appointNodes 传单节点，保持「只删确认框针对的节点」语义，
  // 不删多选的其余节点 —— 库默认 REMOVE_NODE 删整个 activeNodeList，故必须显式传参）
  mindMapInstance!.execCommand('REMOVE_NODE', [rn])

  // 落库：detail delete → handleDeleteTree（子树 uid 映射清理 + store.remove 级联）
  //       走 opQueue 串行队列 + 失败重试，失败由全局同步状态提示
  selectedNodeId.value = null
  showToolbar.value = false
  nodeDeleteConfirmVisible.value = false
  return true
}
```

**注意事项（语义变化，实施前确认）**：

1. **反馈语义变化**：现状 `await store.remove` 成功后 `message.success('已删除')`、
   失败弹窗保留。改造后为乐观删除（画布即时消失，弹窗即时关闭），
   落库失败走既有 `markError` → 顶部同步状态「保存失败/重试」链路 +
   `retryFailedOps` 重试队列（404 时 `markSkipped` 丢弃，语义自洽）。
   **建议**：乐观删除场景下移除逐次 `message.success`（高频删除会刷屏），
   失败提示复用全局同步状态条 —— 与画布 Del 键的行为对齐（现状 Del 键
   也是乐观删除 + 失败进重试队列，本改造正是让两条路径一致）。
2. **确认弹窗保留**：`handleDelete` 的弹窗链路不变（根节点 tip、删除确认）。
   未来若加「多选批量删除」，改传 `activeNodeList` 对应渲染节点数组即可。
3. 删除后库会激活兄弟/父节点（`node_active` 事件）—— 现状 `selectedNodeId=null/
   showToolbar=false` 手动清选中态在前，工具栏不会闪现新节点信息。
   验证 3-5 覆盖。
4. `rootDeleteTipVisible`（根节点删除 tip）链路不动。
5. **pendingCreates 竞态**（阶段 0 任务 0.2 已兜底）：新增节点后 create 尚在飞
   即删除 → detail delete 与 create 都进 opQueue 串行执行（useMindMapSync:
   1129-1144 creates/deletes 同一队列，顺序保证）；409/404 由
   `isNotFoundError` 分支消化。验证 3-6 覆盖。

### 任务 3.4：`getNextSortOrder` 清理 + 阶段验证

- `rg "getNextSortOrder" frontend/src` → 若仅剩 handleAddChild/handleAddSibling/
  handlePaste 三处使用，且前两者已删，则保留（阶段 4 粘贴改造后）再删；
  本阶段不删（粘贴仍在用）。
- `rg "reloadMindMap\(" frontend/src/views/editor/MindMapEditorView.vue`
  记录当前调用点数（预期：本阶段后剩 handleUndo/handleRedo/handleVersionRollback/
  handlePaste/空图初始化 ≈ 5-6 处）。

## 3. 阶段验证

| # | 验证项 | 操作 | 期望 |
|---|---|---|---|
| 3-1 | 新增子节点 | 选中父节点 → 工具栏新增子节点 | **即时**出现（不等 RTT）；F5 后保持；位置 = 子列表内（末尾/首插等价场景） |
| 3-2 | 折叠父新增 | 折叠节点 → 工具栏新增子节点 | 父节点自动展开 + 子节点即时出现；`PUT isCollapsed=false` 与 `POST create` 均正确；F5 保持 |
| 3-3 | 新增同级 | 选中中间某节点 → 新增同级 | 新节点出现在**选中节点正下方**；F5 后位置一致（兄弟重编闭环生效）；方向（根子节点）为右 |
| 3-4 | 与画布一致性 | 同一位置分别用工具栏按钮与 Enter 键新增同级 | 位置行为完全一致 |
| 3-5 | 删除 | 删除带多级子树的节点 → 确认 | 画布即时消失、弹窗即时关闭、选中态清空；子树级联删除；F5 保持 |
| 3-6 | 新增后立即删除 | 新增子节点（create 在飞）→ 立即删除它 | 无幽灵节点；后端最终一致（create→remove 串行）；无报错刷屏 |
| 3-7 | undo 矩阵 | 新增子/新增同级/删除 各一次 undo + redo | 每次一键还原；后端 diff 正确（F5 验证） |
| 3-8 | 方向回归 | 根下左侧已有子节点 → 选中根 → 新增子节点 | 新节点出现在右侧（现状语义保持）；`direction=1` 落库 |
| 3-9 | readonly | 只读模式尝试所有入口（按钮应不可见/禁用） | 无任何命令执行、无后端请求 |
| 3-10 | 性能对比 | 500+ 节点图上新增/删除 | 操作即时（<50ms 视觉反馈），对比改造前（RTT + 全量 render 数百 ms） |
| 3-11 | 唯一索引回归 | 兄弟间高频新增/删除交替 → F5 | 无 `Duplicate entry` 日志、位置无错乱 |

## 4. 提交策略

单 commit：`refactor(editor): toolbar add/remove via mindmap commands with incremental sync`

## 5. 回滚策略

三个 handler 函数级替换，revert 即回现状。特别地：阶段 0 的
`beforeShortcutRun` 拦截与本阶段强相关（undo 一致性），revert 本阶段时
阶段 0 可独立保留（其自身零行为损失）。
