# 阶段 1：增量通道补齐 —— 样式字段分支、helper、墨色抑制、聚合机制

> 目标：让 `data_change_detail` 增量通道能承载样式字段（color/fontSize/shape/…），
> 并为阶段 2 的「画布优先写入」准备好消费端。
> 本阶段**只扩展增量通道，不改任何 UI handler**——改造后行为与现状完全一致
> （样式 detail 仍不会产生，因为还没有人往画布写样式），是纯增量、零回归风险的阶段。

---

## 0. 现状分析（源码证据）

### 0.1 handleUpdate 目前只同步五类字段

`useMindMapSync.ts:671-752`（`handleUpdate`）的字段分支：

| 分支 | 行号 | 同步方式 |
|---|---|---|
| 文本 title | ~687 | `scheduleTextUpdate`（debounce 400ms） |
| 展开/折叠 isCollapsed | ~694 | `scheduleCollapseUpdate`（debounce 250ms） |
| 备注 note | ~705 | `scheduleNoteUpdate`（debounce 600ms） |
| extraData（关联线/摘要/外框） | ~712-734 | `scheduleExtraDataUpdate`（debounce 500ms） |
| 方向 direction | ~737-751 | `enqueueStructuralOp`（结构性队列） |

**样式字段完全没有分支**。detail diff 中样式变化（isSameObject 深比较必然检出，
`Command.js:231`）会被静默丢弃。

### 0.2 墨色注入依赖「无 color 分支」这一现状

- `useMindMapSync.ts:80` `injectedNodeInk` 表：主题/明暗切换时记录已注入的文字色。
- `nodeInk.ts:95-142` `patchNodeInkOverrides`：通过
  `execCommand('SET_NODE_DATA', node, { color: resolved.ink })`（~128 行）写画布。
- 该命令会触发 detail（exec → addHistory → emit），**但 handleUpdate 没有 color
  分支，所以注入色不会落库** —— 这就是「墨色只进渲染数据、不进数据库」的机制。

**一旦本阶段加上 color 分支，主题切换就会把解算墨色写进后端**。
必须同时引入抑制开关（任务 1.3），否则引入「切主题污染所有自定义底色节点的
color 字段」的数据损坏。

### 0.3 icon 的特殊性：它是 text 前缀，不是独立渲染字段

`convertToMindMapData`（useMindMapSync.ts:410）：

```ts
text: n.icon ? `${n.icon} ${n.title}` : n.title
```

后端 icon 字段在画布上以「text 前缀」形态存在；`extractTitleFromText`
（~1103-1112）在落库时按 store 节点的 icon 剥离前缀。
改 icon = 改 text + 改后端 icon 字段，需要专门处理（阶段 2 任务 2.3），
本阶段在样式分支的聚合 payload 中预留 icon 位。

## 1. 涉及文件

| 文件 | 改动 |
|---|---|
| `frontend/src/views/editor/composables/useMindMapSync.ts` | 任务 1.1-1.5 全部 |
| `frontend/src/views/editor/MindMapEditorView.vue` | 无（本阶段不改任何调用方） |
| `frontend/src/themes/nodeInk.ts` | 无（抑制开关放在调用方 useMindMapSync） |

## 2. 任务分解

### 任务 1.1：抽取单节点 DTO → 渲染 data 的 helper `dtoToNodeData`

**位置**：`useMindMapSync.ts`，把 `convertToMindMapData` ~409-467 行
（for 循环内的单节点映射段）抽为独立函数。

**现状**：整段映射逻辑内联在 `convertToMindMapData` 的 for 循环里，
阶段 4 粘贴（appointData 构造）需要复用它，无法引用。

**目标代码**（示意，纯搬运 + 参数化）：

```ts
/** 单个后端 DTO → simple-mind-map 渲染 data（不含 uid 注入策略，由调用方决定）
 *  与 convertToMindMapData 的行为保持逐字段一致。 */
function dtoToNodeData(
  n: NodeDto,
  ctx: {
    inkOverrides: Map<string, ResolvedNodeInk>
    injectedNodeInk: Map<string, string>   // 需要登记注入时传入；纯构造场景传 null
    rootId?: string | null                 // 用于 dir 判定（仅根直接子节点设 dir）
    withUid?: boolean                      // convertToMindMapData 传 true；粘贴构造传 false（见阶段 4）
  }
): Record<string, unknown> {
  const data: Record<string, unknown> = {
    text: n.icon ? `${n.icon} ${n.title}` : n.title,
    expand: !n.isCollapsed
  }
  if (ctx.withUid) {
    data.uid = n.id
    data.id = n.id
    data.backendId = n.id
  }
  if (n.color) data.color = n.color
  if (n.fontSize) data.fontSize = n.fontSize
  // ...（fontSize/fontFamily/fillColor/borderColor/shape/lineColor/lineDasharray/note/
  //      extraData 解析、墨色注入、dir 判定 —— 与现状 409-467 行逐行对应）
  return data
}
```

**注意事项**：

1. **纯重构、行为等价**：`convertToMindMapData` 改为循环内调用
   `dtoToNodeData(n, {...})`，输出必须逐字段一致。迁移后用一次「改造前后
   `JSON.stringify(convertToMindMapData(同一份 nodes))` 对比」验证（验证 1-2）。
2. 墨色登记（`injectedNodeInk.set`）只在全量转换场景发生；粘贴构造场景
   `withUid=false` 且不登记（新节点的墨色由后续主题切换按需补）。
3. `uid/id/backendId` 三字段必须可开关：粘贴 appointData 若带旧 uid，
   `handleCreateTree` 的 `uidToBackendId.has(uid)` 检查（~599 行）会命中并
   **跳过创建** —— 这是阶段 4 的关键陷阱，本任务先把开关预留出来。

### 任务 1.2：handleUpdate 增加样式聚合分支

**位置**：`useMindMapSync.ts` `handleUpdate` 内，direction 分支（~737）之前插入。

**目标代码**（示意）：

```ts
// ---------- 样式字段变化（聚合 debounce，key=`style:${backendId}`） ----------
// color 受 inkPatchActive 抑制（任务 1.3）；icon 与 text 前缀联动（阶段 2 任务 2.3）。
const styleFields: Array<{ lib: string; backend: string; convert?: (v: unknown) => unknown }> = [
  { lib: 'fontSize', backend: 'fontSize' },
  { lib: 'fontFamily', backend: 'fontFamily' },
  { lib: 'shape', backend: 'shape', convert: (v) => shapeStrToNum[v as string] },
  { lib: 'fillColor', backend: 'backgroundColor' },
  { lib: 'lineColor', backend: 'edgeColor' },
  { lib: 'lineDasharray', backend: 'edgeStyle', convert: (v) => dashToEdgeStyle[v as string] },
  { lib: 'borderColor', backend: 'borderColor' }
]
const stylePartial: Record<string, unknown> = {}
for (const f of styleFields) {
  const oldV = (oldData.data as any)[f.lib]
  const newV = (data.data as any)[f.lib]
  if (oldV !== newV) {
    stylePartial[f.backend] = f.convert ? f.convert(newV) : newV
  }
}
// color 单独处理：受墨色抑制开关保护
const oldColor = (oldData.data as any).color
const newColor = (data.data as any).color
if (oldColor !== newColor && !inkPatchActive) {
  stylePartial.color = newColor
}
if (Object.keys(stylePartial).length > 0) {
  scheduleStyleUpdate(backendId, stylePartial)
}
```

**反向映射表**（与文件头部既有正向表对照新增）：

```ts
/** simple-mind-map 形状字符串 → 后端 NodeShape 数字（shapeMap 的翻转） */
const shapeStrToNum: Record<string, number> = Object.fromEntries(
  Object.entries(shapeMap).map(([k, v]) => [v, Number(k)])
)
/** lineDasharray → 后端 EdgeStyle 数字（edgeStyleMap 的翻转） */
const dashToEdgeStyle: Record<string, number> = Object.fromEntries(
  Object.entries(edgeStyleMap).map(([k, v]) => [v, Number(k)])
)
```

**注意事项**：

1. `shapeMap` 有两个 key 映射 `'none'`（edgeStyleMap 0 与 3 都是 'none'）——翻转表
   只保留后者（3），接受「edgeStyle=0 落库后被读回为 3」的既有序列化语义
   （`convertToMindMapData` 正向读取 `n.edgeStyle in edgeStyleMap` 时
   0/3 都映射 'none'，渲染等价）。在代码注释说明。
2. 样式分支**不做** per-field debounce：同节点多字段变化（面板一次改多个样式）
   打包进**一个** partial、一次请求 —— 与现状 `handleUpdateStyle` 单节点走
   一次 `nodesStore.update(payload)` 的请求次数对齐。
3. **icon 不进本分支的 diff 检测**：icon 变化表现为 text 变化（前缀），由
   文本分支捕获；icon 字段值本身由阶段 2 任务 2.3 显式放进 partial。
   本分支的聚合 payload 预留 `icon` 键位（由调用方注入），diff 检测只覆盖
   上表 7 字段 + color。
4. `undefined → 具体值` 与 `具体值 → undefined` 都要同步（清空样式场景，
   `if (n.color)` 的正向映射决定 undefined 不写字段；反向 diff 时
   `oldV !== newV` 对 undefined 成立 ✓）。

### 任务 1.3：墨色抑制开关 `inkPatchActive`

**位置**：`useMindMapSync.ts` `applyNodeInkOverrides`（~554-560）+ 任务 1.2 的 color 判断。

**目标代码**（示意）：

```ts
/** 墨色补丁窗口标记：patchNodeInkOverrides 内部的 SET_NODE_DATA({color})
 *  会触发 data_change_detail，此窗口内 handleUpdate 的 color 分支必须跳过，
 *  否则主题/明暗切换会把「解算墨色」写进后端 color 字段（数据污染）。 */
let inkPatchActive = false

function applyNodeInkOverrides() {
  const inst = getMindMapInstance()
  const renderTheme = opts.getRenderTheme?.() ?? null
  if (!inst || !renderTheme) return
  const overrides = computeNodeInkOverrides(nodesStore.nodes, renderTheme)
  inkPatchActive = true
  try {
    patchNodeInkOverrides({ instance: inst, overrides, injected: injectedNodeInk })
  } finally {
    inkPatchActive = false
  }
}
```

**注意事项**：

1. **同步窗口足够**：detail 的 emit 链（exec → addHistory → emit →
   `processDataChangeDetail` → `handleUpdate` 启动）全部同步执行；
   `handleUpdate` 内部首个 `await getBackendIdOrWait` 之前，color 分支判断
   尚未执行——**注意**：`handleUpdate` 是 async，样式分支在 `await` 之后执行时
   `inkPatchActive` 可能已复位。**因此抑制判断必须放在分支读取处且同步执行**。
   最稳妥做法：`processDataChangeDetail`（~1117）入口对整批 items 判定
   `const inkSuppress = inkPatchActive`，把该布尔值随 diff 传入 `handleUpdate`；
   `handleUpdate(diff, inkSuppress)`。**实施时按此传参方案**，避免 async 竞态。
2. `patchNodeInkOverrides` 写多个节点 = 多次 SET_NODE_DATA = 多次 detail emit，
   全部落在 try 窗口内，批量覆盖 ✓。
3. 首次加载的墨色注入发生在 `convertToMindMapData`（~425-427，写入 data 而非命令），
   且 `isSettingData` 窗口拦截 detail（`processDataChangeDetail` ~1119 行），
   不受本开关影响。
4. `nodeInk.ts:90` 注释「SET_NODE_DATA 不进撤销历史」的表述在改造后需要修正：
   库内历史（Command.addHistory）会记录，但不产生应用层 pushHistory/落库。
   阶段 2 实施时顺手更正该注释（避免误导）。

### 任务 1.4：`scheduleStyleUpdate` 节点级 debounce（400ms）

**位置**：`useMindMapSync.ts`，与 `scheduleNoteUpdate`（~914-927）完全同构。

**目标代码**（示意）：

```ts
/** 样式聚合更新核心（供 flush 与失败重试复用） */
async function runStyleUpdate(backendId: string, partial: Record<string, unknown>): Promise<void> {
  await nodesStore.update(backendId, partial)
}

/** 样式更新调度（debounce 400ms，key=`style:${backendId}`）
 *  partial 为一次 diff 的聚合对象；同节点连续触发时 flush 闭包捕获最新 partial。 */
function scheduleStyleUpdate(backendId: string, partial: Record<string, unknown>) {
  const existing = styleDebounceTimers.get(backendId)
  if (existing) clearTimeout(existing.timer)
  dropFailedOpsByKey(`style:${backendId}`)
  const flush = () => {
    styleDebounceTimers.delete(backendId)
    return enqueueStructuralOp(() => runStyleUpdate(backendId, partial), `style:${backendId}`)
  }
  styleDebounceTimers.set(backendId, { timer: setTimeout(flush, 400), flush })
}
```

**四处登记（必须逐一完成，否则 flush/作废/关页排空会漏掉样式）**：

| 登记点 | 位置 | 动作 |
|---|---|---|
| `styleDebounceTimers` Map 声明 | ~295-301 debounce Map 群 | 新增，key = backendId |
| `hasPendingWriteOps` | ~305-314 | 新增 `if (styleDebounceTimers.size > 0) return true` |
| `clearPerNodeTimers` | ~316-333 | 新增同 text/collapse/note/extraData 四行清理 |
| `invalidatePendingNodeWrites` | ~338-348 | `clearMap(styleDebounceTimers)` |
| `flushPendingUpdates` | ~1368-1382 | `flushMap(styleDebounceTimers)` |

**注意事项**：

1. `enqueueStructuralOp` 带 key=`style:${backendId}` → 失败重试队列自动按
   节点去重、新修改淘汰旧失败值（既有机制，零额外代码）。
2. `nodesStore.update` 的 pushHistory（nodes.ts:117）自动为每次样式落库产生
   undo 快照 —— 与现状一致（现状 `handleUpdateStyle` 也走 `store.update`）。

### 任务 1.5：样式批量抑制标记 `styleBatchSyncActive`（阶段 2 预埋）

**位置**：`useMindMapSync.ts`，与 `collapseBatchSyncActive`（~807）同构。

**目标代码**（示意）：

```ts
/** 多选批量样式同步抑制标记：
 *  多选改样式会对每个激活节点执行 SET_NODE_STYLES，逐节点触发 update diff
 *  （N 次 detail = N 次全树 diff + N 个防抖请求）。抑制窗口内由调用方
 *  （阶段 2 handleUpdateStyle）自行完成单次 batchUpdate 落库，
 *  样式分支跳过 —— 与 collapseBatchSyncActive + applyExpandCollapse 模式同构。 */
let styleBatchSyncActive = false
```

**接线**：

- `handleUpdate` 样式分支入口判断：`if (styleBatchSyncActive) return`（跳过整段
  样式检测；其余字段分支照常）。
- 窗口复位沿用 `applyExpandCollapse` 的 `setTimeout(..., 600)` 模式（覆盖
  addHistory 派发窗口）；阶段 2 的调用方在 `finally` 中复位。

**本阶段不启用**（没有调用方置位），只是把判断分支加上 —— 置位代码在阶段 2
任务 2.1。这是「本阶段零行为变化」承诺的一部分。

## 3. 阶段验证

| # | 验证项 | 操作 | 期望 |
|---|---|---|---|
| 1-1 | 行为零变化回归 | 全量回归 verification.md 的「画布原生交互」矩阵（Tab/Enter/Del/拖拽/双击/折叠/备注/关联线） | 与改造前逐项一致（本阶段无人写样式，新分支不触发） |
| 1-2 | helper 等价性 | 对任一导图，改造前后分别 `JSON.stringify(convertToMindMapData(nodes))` 对比 | 完全相等 |
| 1-3 | 墨色抑制链路（预演） | 临时在控制台对某节点 `mindMap.execCommand('SET_NODE_DATA', node, { color: '#123456' })` → 400ms 后观察网络面板 | **出现**一次 `PUT /nodes/{id}` 携带 color（样式分支生效） |
| 1-4 | 抑制开关 | 切换深色/浅色/主题若干次 → 观察网络面板 | **无任何** `PUT /nodes` 请求携带解算墨色 color |
| 1-5 | debounce 登记完整性 | 改样式（控制台预演）后立即 F5（拦截 beforeunload）→ 观察 flush | 关页前样式 flush 飞出（flushPendingUpdates 已登记）；刷新后颜色保持 |
| 1-6 | 失败重试 | DevTools 断网 → 控制台改样式 → 恢复网络 → 点保存/重试 | 样式 update 重放成功，颜色最终落库 |

## 4. 提交策略

单 commit：`refactor(sync): add style branches & helpers to incremental channel`

## 5. 回滚策略

本阶段是纯增量（新 Map/新分支/新标志），revert 单 commit 即回到现状。
唯一注意点：任务 1.1 的 helper 抽取若 revert，阶段 4 的粘贴改造需重写 ——
建议 helper 抽取单独 commit（`refactor(sync): extract dtoToNodeData helper`），
与分支扩展分开，便于按需保留。
