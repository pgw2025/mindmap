# 改造 3：reloadMindMap 只用于整体重建 —— 完整实施方案

> 版本：v2（2026-09-26，含评审修订）
> 状态：方案评审通过，待实施
> 关联文档：`docs/performance-optimization-plan.html`（改造 3 原始条目）、`docs/refactor-1-reloadtree-plan.html`（改造 1，已完成）、`docs/refactor-2-recursive-query-plan.html`（改造 2，已完成）

---

## 1. 背景与问题

工具栏的 5 类操作（新增子节点 / 新增同级 / 删除 / 改样式 / 改内容 / 粘贴）目前全部走
「store 直写后端 → `reloadMindMap()` 全量重建画布」的路径：

```
用户操作 → nodesStore.create/update/batchUpdate/remove（等待后端 RTT）
         → reloadMindMap() = invalidatePending + setData 全量 + 整树重渲染
         → 视口/缩放/选中态重置，画布闪烁
```

而画布原生交互（Tab 新增、Enter 同级、双击编辑、Del 删除、拖拽移动）早已通过
simple-mind-map 的 `data_change_detail` 增量事件 + `useMindMapSync` 走「命令 → 增量 diff →
就地落库」的链路，无任何全量重建。

**同一编辑器里存在两套数据通道**：一条增量（原生交互），一条全量（工具栏）。
改造 3 的目标是把工具栏操作全部迁移到增量通道，`reloadMindMap` 收口到 4 个
「整体重建」场景。

## 2. 评审发现：原方案的两处前提修正

原方案（performance-optimization-plan.html 中改造 3 条目）有两个前提不成立，已在
v2 方案中修正：

1. **「删掉 reloadMindMap 即可」不成立**：工具栏改样式从未写入画布渲染节点，
   视觉更新完全依赖全量重建。删除 reload 必须与「画布优先写入」配套。
2. **「增量通道已覆盖样式」不成立**：`useMindMapSync.handleUpdate` 只同步
   text / expand / note / extraData / direction 五类字段，样式字段需要补齐分支。
3. **（评审新增）双历史栈冲突**：库在 `Command.js:51-54` 绑定 Ctrl+Z → 库内 BACK，
   与应用 store 历史栈（`handleUndo` → `nodesStore.undo()` + `reloadMindMap`）并存且
   互不同步。工具栏操作命令化后两条栈会同时记录同一操作，撤销语义错乱。
   必须在一切命令化改造之前先做「历史栈归一」（阶段 0）。

## 3. 核心设计：画布优先 + 增量通道全覆盖

所有用户可见编辑统一遵循单向数据流：

```
工具栏/面板/UI 操作
        │
        ▼
simple-mind-map 命令（execCommand / node.setXxx）   ←—— 画布先变，视口/缩放不重置
        │  内部：render 局部重排
        ▼
data_change_detail 增量 diff（Command.addHistory → emitDataUpdatesEvent）
        │
        ▼
useMindMapSync.handleUpdate / handleCreateTree / handleDeleteTree
  （节点级 debounce 合并 + 串行队列 + 失败重试）
        │
        ▼
nodesStore 就地更新 → 后端 API
```

`reloadMindMap()` 仅保留于 4 个整体重建场景：**undo/redo、版本回滚、空导图初始化、首次加载**。

## 4. 阶段地图（5 个阶段、19 个任务）

| 阶段 | 文档 | 任务数 | 优先级 | 依赖 |
|---|---|---|---|---|
| 阶段 0：历史栈归一 | `phase-0-history-unification.md` | 3 | P0 前置必做 | 无 |
| 阶段 1：增量通道样式补齐 | `phase-1-incremental-channel.md` | 5 | P0 | 无（与阶段 0 并行可） |
| 阶段 2：样式/内容画布优先 | `phase-2-style-content.md` | 4 | P1 | 阶段 1 全部 |
| 阶段 3：增删命令化 | `phase-3-crud-commands.md` | 4 | P1 | 阶段 0 + 阶段 1 |
| 阶段 4：粘贴与收口 | `phase-4-paste-finalize.md` | 3 | P2 | 阶段 0/1/3 |

推荐实施顺序：**0 → 1 → 2 → 3 → 4**，每阶段独立提交（commit 粒度见各阶段文档），
每阶段完成后回归该阶段验证清单再进入下一阶段。

### 依赖关系说明

- 阶段 0 是阶段 3/4 的硬前置：工具栏增删走库命令后，若 Ctrl+Z 仍走库内栈，
  一次撤销会在应用栈里留下新历史，redo 语义彻底错乱。
- 阶段 1 是阶段 2 的硬前置：样式写入画布后若 handleUpdate 没有样式分支，改动不会落库。
- 阶段 2 与阶段 3 之间无依赖，可按风险偏好先后（推荐先 2：纯 UI 写入路径替换，
  不涉及结构命令，风险最低）。
- 阶段 4 依赖阶段 3 的渲染节点查找与命令调用模式。

## 5. 全局设计原则

1. **最小化修改**：不动 `useMindMapSync` 的既有并发模型（opQueue 串行队列、
   节点级 debounce、pendingCreates、失败重试），只做增量扩展。
2. **复用既有模式**：样式 debounce 完全复刻 `scheduleCollapseUpdate` /
   `scheduleNoteUpdate` 的结构；多选批量抑制复刻 `collapseBatchSyncActive` +
   `applyExpandCollapse` 的「抑制窗口 + 单次 batchUpdate」模式。
3. **行为保持**：`uid === backendId` 恒等映射（`convertToMindMapData` 既有修复）、
   失败重试、readonly 拦截（`processDataChangeDetail` 首行）、移动端共用 handler
   等既有机制一律不改语义。
4. **一处一处收口**：每个 handler 改造后立即 rg 确认 `reloadMindMap` 调用点减少，
   最终收口清单见 `phase-4-paste-finalize.md` 任务 4.2。
5. **不做的事**：不引入子树粘贴（现状本就是单节点粘贴）；不动 store 的
   undo/redo 本体实现；不引入新依赖；不改后端接口。

## 6. 总验收标准

全部阶段完成后必须同时满足：

- [ ] `rg "reloadMindMap\(" frontend/src` 仅剩 4 处调用：`handleUndo` / `handleRedo` /
      `handleVersionRollback` / 空导图初始化分支。
- [ ] 工具栏 5 类操作（含移动端）全部即时生效：无整树重绘、无闪烁、视口与缩放保持。
- [ ] 改样式 / 新增 / 删除 / 粘贴后刷新页面（F5），结果与操作前画布所见完全一致。
- [ ] Ctrl+Z / Ctrl+Y 与工具栏撤销/重做按钮行为一致（同一历史栈）。
- [ ] 主题切换、明暗切换不产生任何后端写请求（墨色抑制生效）。
- [ ] 1000 节点级导图上，改样式操作耗时对比改造前有数量级下降
      （基准方法见 `verification.md`）。
- [ ] 离线快照模式下工具栏操作行为与现状一致（失败入重试队列，不静默丢失）。

## 7. 文档目录

```
docs/refactor-3-reloadmindmap/
├── README.md                        ← 本文件：总览、阶段地图、验收标准
├── phase-0-history-unification.md   ← 阶段 0：历史栈归一（3 任务）
├── phase-1-incremental-channel.md   ← 阶段 1：增量通道样式补齐（5 任务）
├── phase-2-style-content.md         ← 阶段 2：样式/内容画布优先（4 任务）
├── phase-3-crud-commands.md          ← 阶段 3：增删命令化（4 任务）
├── phase-4-paste-finalize.md         ← 阶段 4：粘贴命令化 + reloadMindMap 收口（3 任务）
├── verification.md                   ← 验证手册：各阶段验证清单 + 回归矩阵 + 性能基准
└── risks.md                         ← 风险登记册：10 项风险、严重度、缓解措施、验证方法
```

## 8. 源码锚点速查（实施时以函数名定位为准，行号仅供参考）

| 文件 | 关键位置 |
|---|---|
| `frontend/src/views/editor/MindMapEditorView.vue` | `initMindMap` 构造选项 ~660-689；`handleAddChild` ~878；`handleAddSibling` ~901；`handleDelete` ~923；`submitNodeDelete` ~937；`handleUpdateStyle` ~956；`handleContentSave` ~985；`handleVersionRollback` ~996；`handleNoteChange` ~1147；`handleUndo/handleRedo` ~1451；`handleCopy` ~1462；`handlePaste` ~1472 |
| `frontend/src/views/editor/composables/useMindMapSync.ts` | `convertToMindMapData` ~389；`reloadMindMap` ~529；`applyNodeInkOverrides` ~554；`handleCreateTree` ~570；`handleDeleteTree` ~645；`handleUpdate` ~671；`scheduleTextUpdate` ~765；`collapseBatchSyncActive` ~807；`applyExpandCollapse` ~825；`handleStructuralChangesBatch` ~962；`processDataChangeDetail` ~1117；`findRenderNodeByUid` ~1290；`flushPendingUpdates` ~1368；`waitForPendingOps` ~1388 |
| `frontend/src/stores/nodes.ts` | `undoStack/redoStack` ~39；`pushHistory` ~59；`undo` ~191；`redo` ~198 |
| `frontend/src/themes/nodeInk.ts` | `patchNodeInkOverrides` ~95（内部 `execCommand('SET_NODE_DATA', node, {color})` ~128） |
| `frontend/node_modules/simple-mind-map/src/core/command/Command.js` | Ctrl+Z/Y 快捷键注册 51-57；`exec` 排除名单 60-75；`addHistory` 105-133；`back/forward` 136-174；`emitDataUpdatesEvent` 200-256 |
| `frontend/node_modules/simple-mind-map/src/core/command/KeyCommand.js` | `onKeydown` 113-150（`beforeShortcutRun` 钩子 139-144） |
| `frontend/node_modules/simple-mind-map/src/core/render/Render.js` | 命令注册 241-333；`insertNode` ~786；`insertChildNode` ~893；`removeNode(appointNodes=[])` ~1413；`setNodeStyles` ~1625；Del 快捷键 407-409 |
| `frontend/node_modules/simple-mind-map/src/core/render/node/nodeCommandWraps.js` | `node.setData` → `execCommand('SET_NODE_DATA')`；`node.setStyles` → `execCommand('SET_NODE_STYLES')`（全文 1-68 行） |
