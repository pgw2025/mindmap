# 阶段 0：历史栈归一（P0，前置必做）

> 目标：撤销/重做统一走应用 store 历史栈，禁用库内 BACK/FORWARD 对用户的可达性。
> 若跳过本阶段直接做阶段 3，工具栏命令化后两套历史栈会同时记录同一操作，
> 用户按 Ctrl+Z 将产生「库栈 back → detail → store 落库 → store 又 pushHistory」
> 的连锁记录，redo 语义彻底错乱。

---

## 0. 问题机理（源码证据）

### 现状：两套 undo 并存且互不同步

**套 1：库内历史栈（simple-mind-map Command）**

- `Command.js:51-57`：构造时注册快捷键

  ```js
  // Command.js registerShortcutKeys()
  this.mindMap.keyCommand.addShortcut('Control+z', () => {
    this.mindMap.execCommand('BACK')      // 库内历史 back()
  })
  this.mindMap.keyCommand.addShortcut('Control+y', () => {
    this.mindMap.execCommand('FORWARD')   // 库内历史 forward()
  })
  ```

- `Command.js:exec`（60-75 行）：除 `BACK/FORWARD/SET_NODE_ACTIVE/CLEAR_ACTIVE_NODE`
  外，**每次 execCommand 都自动 `addHistory()`**——库历史随每条命令自动积累。
- `Command.js:back()/forward()`（136-174 行）：恢复历史快照时同样调用
  `emitDataUpdatesEvent` → **触发 data_change_detail → 应用会把它当普通编辑落库**。
- `KeyCommand.js:onKeydown`（113-150 行）：window keydown 全局监听，
  焦点在 `document.body` 或节点文本编辑框时命中；`beforeShortcutRun` 钩子
  （139-144 行）返回 `true` 可中断该快捷键。

**套 2：应用 store 历史栈**

- `nodes.ts:39-40`：`undoStack / redoStack`（HistoryCommand：create/update/move/batch/delete）。
- `MindMapEditorView.vue:1450-1459`：工具栏撤销按钮 → `handleUndo()` →
  `await nodesStore.undo()` + `reloadMindMap()`（整体重建）。

**应用初始化选项（`MindMapEditorView.vue` initMindMap ~660-689）未传
`beforeShortcutRun`，也未调用 `keyCommand.save()/pause()`** —— 库的 Ctrl+Z 当前
就是活的。现状下用户已有两种 undo 入口：

| 入口 | 走的栈 | 现状表现 |
|---|---|---|
| 撤销按钮 | store 栈 + reloadMindMap | 正常 |
| Ctrl+Z（焦点在 body） | 库栈 BACK → detail → store 写 + pushHistory | 「撤销」被 store 记成新历史，按钮 redo 语义错乱 |

工具栏操作不走库命令时，库栈里只有画布原生交互（Tab/Enter/Del）+ 备注
`node.setData` + 墨色补丁 `SET_NODE_DATA` 的记录，冲突尚少；**阶段 3 命令化后
每条工具栏操作都会进库栈，冲突必然暴露**。

## 1. 涉及文件

| 文件 | 改动 |
|---|---|
| `frontend/src/views/editor/MindMapEditorView.vue` | 任务 0.1（构造选项新增 `beforeShortcutRun`）、任务 0.2（handleUndo/handleRedo 前置排空） |
| `frontend/src/views/editor/composables/useMindMapSync.ts` | 无改动（`waitForPendingOps` 已导出） |
| `frontend/src/stores/nodes.ts` | 无改动（undo/redo 本体不动） |

## 2. 任务分解

### 任务 0.1：`beforeShortcutRun` 拦截库 Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z

**位置**：`MindMapEditorView.vue` `initMindMap()` 内 `new MindMap({...})` 构造选项对象
（约 660-689 行，最后一个选项 `dragOpacityConfig` 之后）。

**现状代码**（节选，无该选项）：

```ts
mindMapInstance = new MindMap({
  el: mindMapRef.value,
  data: { data: { text: '中心主题' }, children: [] },
  // ... 其余选项
  dragOpacityConfig: {
    cloneNodeOpacity: 0.55,
    beingDragNodeOpacity: 0.25
  }
})
```

**目标代码**（示意）：

```ts
mindMapInstance = new MindMap({
  // ... 其余选项保持不变
  dragOpacityConfig: { /* ... */ },
  /**
   * 拦截库内置的 Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z 快捷键（默认绑定库内
   * BACK/FORWARD 历史栈），统一转发给应用自己的历史栈。
   * 返回 true 中断库对该快捷键的默认处理（KeyCommand.onKeydown 约定）。
   */
  beforeShortcutRun: (key: string) => {
    if (readonly.value) return true            // 只读画布：撤销一律禁用
    if (key === 'Control+z' || key === 'Control+shift+z') {
      void handleUndo()
      return true
    }
    if (key === 'Control+y') {
      void handleRedo()
      return true
    }
    return false   // 其余快捷键（Tab/Enter/Del/Control+a/...）放行库默认行为
  }
})
```

**注意事项**：

1. `beforeShortcutRun(key, activeNodeList)` 由 `KeyCommand.onKeydown` 在**每个命中
   的快捷键**上调用；必须对其余所有 key 返回 `false/undefined` 放行，否则会
   无声破坏 Tab/Enter/Del 等画布原生交互。
2. **焦点约束由库保证**：`defaultEnableCheck` 要求事件 target 是 `document.body`
   或节点文本编辑框，节点编辑框内按 Ctrl+Z 会被编辑器自身的 undo 消化
   （`before_show_text_edit` → `startTextEdit` 暂停机制，Render.js:415-420）。
   实施后需回归验证「编辑框内 Ctrl+Z 撤销文字，画布不动」。
3. `handleUndo/handleRedo` 是 `async`，转发处 `void` 掉返回值即可（与按钮点击一致）。
4. `Control+shift+z` 是否已注册在库 keyMap 里以实际版本为准；若未注册，
   onKeydown 不会命中该组合，`beforeShortcutRun` 的该分支不会执行——保留分支
   无害，且为多数编辑器惯例兜底。
5. 拦截后库内 `BACK/FORWARD` 命令不再可达，库历史栈退化为「detail diff 的
   快照基准」，`handleCreateTree` 中「已存在映射说明是 undo 恢复 → 跳过」的
   分支不再由库 back 触发，无行为影响。

### 任务 0.2：undo/redo 前置排空挂起写入

**位置**：`MindMapEditorView.vue` ~1450-1459。

**现状代码**：

```ts
/** 撤销/重做后刷新画布 */
async function handleUndo() {
  await nodesStore.undo()
  reloadMindMap()
}

async function handleRedo() {
  await nodesStore.redo()
  reloadMindMap()
}
```

**目标代码**（示意）：

```ts
/** 撤销/重做：先排空挂起写入，再回滚历史，最后整体重建画布 */
async function handleUndo() {
  // 撤销前必须让飞行中的写入全部落地：debounce 窗口、串行队列、pendingCreates
  // 若不排空，create 请求返回后 writeBackendIdToNode 会向已重建的画布写幽灵节点，
  // 且后端将保留「即将被撤销」的那次操作结果。
  if (hasPendingWriteOps()) {
    await waitForPendingOps()
  }
  await nodesStore.undo()
  reloadMindMap()   // 内部 invalidatePendingNodeWrites() 已作废重建前的一切挂起值
}

async function handleRedo() {
  if (hasPendingWriteOps()) {
    await waitForPendingOps()
  }
  await nodesStore.redo()
  reloadMindMap()
}
```

**注意事项**：

1. `waitForPendingOps` / `hasPendingWriteOps` 已在 `MindMapEditorView.vue` ~556 行
   的 useMindMapSync 解构返回值中，无需新增导出。
2. `waitForPendingOps`（useMindMapSync.ts:1388-1399）内部：flush 全部 debounce →
   await opQueue → await pendingCreates。这三类等待在阶段 1/2/3 扩展
   （styleDebounceTimers 等）时必须同步登记，否则排空会漏（各阶段文档已列）。
3. `reloadMindMap()` 首行 `invalidatePendingNodeWrites()`（useMindMapSync.ts:533）
   与本任务互补：前者「作废」挂起值，本任务「落地」飞行值。顺序固定为
   **先落地 → 再回滚 → 再重建**。
4. `hasPendingWriteOps()` 只是快速短路（多数场景无挂起），不加也无正确性问题，
   但加了能避免一次无谓的 Promise.allSettled 调度。

### 任务 0.3：阶段验证

| # | 验证项 | 操作 | 期望 |
|---|---|---|---|
| 0-1 | 快捷键归一 | 画布 Tab 新增节点 → 焦点移出画布按 Ctrl+Z | 一次还原（应用栈 undo + reload），再按 Ctrl+Y 一次重做；**不允许**出现「按一下 Ctrl+Z 画布回退但后端多出记录」 |
| 0-2 | 库栈不可达 | 连续做 3 次画布操作后按住 Ctrl+Z 多次 | 还原次数 = 应用栈深度，不会出现库栈独立回退的中间态（画布与工具栏状态不一致） |
| 0-3 | 编辑框内撤销 | 双击节点进入文字编辑 → 输入若干字 → Ctrl+Z | 撤销的是**文字输入**，画布结构不动（库 startTextEdit 暂停机制仍生效） |
| 0-4 | undo 前置排空 | 新增节点后**立即**（400ms debounce 窗口内）改它的文字 → 立刻点撤销按钮 | 不出现幽灵节点；后端不残留该 create/update；画布回到新增前 |
| 0-5 | 其余快捷键放行 | 依次按 Tab / Enter / Del / Control+a / Control+l / Control+Up | 全部保持库默认行为不变 |
| 0-6 | readonly | 只读模式打开画布按 Ctrl+Z | 无任何反应，无后端请求 |
| 0-7 | 撤销按钮回归 | 工具栏按钮 undo/redo 交替若干次 | 与改造前行为一致（本阶段未改按钮链路，仅加了前置排空） |

## 3. 提交策略

单 commit：`refactor(editor): unify undo history to app stack via beforeShortcutRun`

## 4. 回滚策略

删除 `beforeShortcutRun` 选项 + 还原 `handleUndo/handleRedo` 两行即可，
无数据/结构影响，风险极低。若 Ctrl+Z 拦截在个别浏览器异常，可临时将拦截分支
改为 `return true`（彻底禁用快捷键 undo，仅保留按钮），功能不丢。
