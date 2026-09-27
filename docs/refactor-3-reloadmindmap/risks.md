# 风险登记册

> 编号规则：R-01 ~ R-10。每项含：严重度（高/中/低）、影响阶段、机理、缓解措施、
> 验证方法、应急回退。

| # | 风险 | 严重度 | 阶段 | 状态 |
|---|---|---|---|---|
| R-01 | 双历史栈冲突（库 BACK/FORWARD vs store 栈） | **高** | 0 | 已设计缓解 |
| R-02 | 墨色注入污染后端 color 字段 | **高** | 1 | 已设计缓解 |
| R-03 | 样式命令绕过 execCommand → detail 断链 | **高** | 2 | 已设计缓解 |
| R-04 | icon 前缀剥离失败 → title 落库污染 | 中 | 2/4 | 已设计缓解 |
| R-05 | 粘贴 appointData 带旧 uid → create 被跳过 | 中 | 4 | 已设计缓解 |
| R-06 | handleCreateTree 丢样式字段（现状缺陷放大） | 中 | 4 | 顺带修复 |
| R-07 | 新增同级位置语义变化（末尾 → 正下方） | 中 | 3 | 待用户确认 |
| R-08 | 删除/粘贴反馈语义异步化 | 低 | 3/4 | 待用户确认 |
| R-09 | undo × pendingCreates 竞态（幽灵节点） | 中 | 0/3 | 已设计缓解 |
| R-10 | detail diff O(N) 固定开销（超大图） | 低 | 全局 | 接受 + 预留优化 |

---

## R-01 双历史栈冲突（严重度：高）

**机理**：库 `Command.js:51-54` 绑定 Ctrl+Z → `execCommand('BACK')`（库内历史）；
应用 `handleUndo` 走 `nodesStore.undo()` + `reloadMindMap`（store 历史）。
两套栈互不同步：库栈由每条命令自动积累（`Command.exec` → `addHistory`），
store 栈由 store 写方法 pushHistory。命令化后每条工具栏操作同时进两栈；
库 BACK 会触发 detail（`back()` → `emitDataUpdatesEvent`）→ store 落库 +
**再** pushHistory —— 一次撤销被 store 记为新历史，redo 语义错乱，且可能产生
「画布显示中间态、后端含撤销残留」的幽灵数据。

**缓解**（phase-0）：`beforeShortcutRun` 拦截 Ctrl+Z/Y/Shift+Z，转发应用
handleUndo/handleRedo 并返回 true 中断库默认处理；undo/redo 前置
`waitForPendingOps` 排空挂起写入。

**验证**：phase-0 验证 0-1/0-2；verification.md §2.4 undo 全矩阵。

**应急**：拦截异常时将分支改为恒 `return true`（禁用快捷键 undo，仅保按钮）。

## R-02 墨色注入污染 color（严重度：高）

**机理**：`patchNodeInkOverrides` 用 `execCommand('SET_NODE_DATA', {color})`
向画布写解算墨色（nodeInk.ts:128）。现状因 handleUpdate **没有** color 分支，
注入色不落库。阶段 1 加 color 分支后，每次主题/明暗切换都会把解算墨色写进
所有「有自定义底色」节点的后端 color 字段 —— 用户数据被主题算法覆盖。

**缓解**（phase-1 任务 1.3）：`inkPatchActive` 开关包裹 `patchNodeInkOverrides`
调用窗口；**因 handleUpdate 是 async（首个 await 后标志可能已复位），抑制判定
必须在 `processDataChangeDetail` 入口同步快照并随 diff 传参**（阶段 1 文档
任务 1.3 注意事项 1 为准）。

**验证**：phase-1 验证 1-4（切主题零 PUT）；phase-2 验证 2-7（墨色回归）。

**应急**：若传参方案实现复杂度超预期，可先临时把 color 从样式分支移除
（用户手动改色仍走阶段 2 的显式落库，不依赖 color diff），缩小暴露面后再补。

## R-03 样式命令绕过 execCommand（严重度：高）

**机理**：`renderer.setNodeStyles(node, style)`（Render.js:1625）虽注册为命令
（Render.js:303），但**直接调用**只走 `setNodeDataRender` —— 不 addHistory、
不 emit detail、不落库，且无任何报错 —— 静默断链。同类：`renderer.setNodeStyle`、
直接改 `nodeData.data`。

**缓解**（phase-2 §0.3 约定）：只用 node 实例方法
（`node.setStyles/setStyle/setData/setText`，nodeCommandWraps 保证走
execCommand）或显式 `execCommand(...)`。**Code review 检查项**：改造后全仓
`rg "renderer\.setNodeStyle" frontend/src` 必须无结果。

**验证**：phase-2 验证 2-1（Network 有 PUT 才算通）。

**应急**：无（属实现纪律，不涉及取舍）。

## R-04 icon 前缀剥离失败 → title 污染（严重度：中）

**机理**：icon 以 text 前缀存在于画布（`${icon} ${title}`）。
`extractTitleFromText`（useMindMapSync:1103）按 **store 节点的 icon** 剥离 ——
若 store.icon 是旧值而画布 text 已带新 icon 前缀，`startsWith` 判断失败 →
title 落库为 `"🎉 标题"` 整串。触发场景：阶段 2 改 icon、阶段 4 粘贴带 icon 节点。

**缓解**：阶段 2 任务 2.3「先就地更新 store.icon，再改 text」；
阶段 4 任务 4.1b「`extractTitleFromText` 增加显式 icon 参数 + `__pasteIcon`
透传」。

**验证**：phase-2 验证 2-6；phase-4 验证 4-3（F5 后 title 无前缀残留）。

**应急**：`extractTitleFromText` 兜底增强（非破坏性）：strip 失败时尝试用
「首个空格前的 token」匹配已知 emoji 模式 —— **不推荐**，规则脆弱；
更简单的应急是暂停 icon 入口的命令化（保留旧路径仅对 icon 生效）。

## R-05 粘贴 appointData 带旧 uid → create 被跳过（严重度：中）

**机理**：`handleCreateTree` ~599 `uidToBackendId.has(uid)` 命中即跳过创建
（防 undo 恢复重复创建）。`uidToBackendId` 初始化时以全部后端 ID 为 key。
若粘贴 appointData 携带源节点 uid/backendId（= 后端 GUID），detail create 到达时
映射命中 → **粘贴静默无效果**（画布出现副本但后端无记录，F5 后消失）。

**缓解**（phase-4 §0.3 + 任务 1.1）：`dtoToNodeData` 粘贴场景 `withUid=false`，
让库 createUid 生成全新 uid。

**验证**：phase-4 验证 4-7（高频粘贴后 F5 全部保持）。

**应急**：无必要（设计已规避；若出现即为 withUid 接线错误，直接修复）。

## R-06 handleCreateTree 丢样式字段（严重度：中，现状缺陷放大）

**机理**：`handleCreateTree` create payload 只有
parentId/title/sortOrder/isCollapsed/direction —— **所有**走 detail create 的
新节点样式落库即丢。现状已影响画布原生 Ctrl+C/V 粘贴（F5 样式消失，存量 bug）；
工具栏粘贴命令化后若不修会直接暴露。

**缓解**（phase-4 任务 4.1b）：`extractStyleFromDetailData` 提取样式/note/透传
content，与 handleUpdate 样式分支共用同一张映射表。

**验证**：phase-4 验证 4-1/4-4（含原生粘贴缺陷修复回归）。

**应急**：4.1b 独立成 commit，可单独回退（回退后恢复现状缺陷，不新增问题）。

## R-07 新增同级位置语义变化（严重度：中，待用户确认）

**机理**：现状 `getNextSortOrder` = 末尾追加；`INSERT_NODE` = 选中节点正下方
（`getNodeDataIndex`）。画布 Enter 键行为与改造后工具栏一致，但与改造前
工具栏行为不同 —— 已习惯「新节点总在最末」的用户会感知差异。

**缓解选项**（phase-3 任务 3.2 注意事项 2，实施前确认）：
A. 接受新语义（推荐：与画布一致、心智统一，撤销/落库链路无差别）；
B. 保持末尾：定位父节点末位渲染子节点 → `execCommand('INSERT_AFTER', [last], data)`。

**验证**：phase-3 验证 3-3/3-4 按选定选项执行。

**应急**：切换 A/B 仅改一条命令行，无结构影响。

## R-08 删除/粘贴反馈语义异步化（严重度：低，待用户确认）

**机理**：现状删除 await 后端后才提示成功/失败；改造后为乐观删除
（即时消失），落库失败走全局同步状态条 + 重试队列，不再有逐次 message。
高频删除时体验更佳，但「点删除→立刻刷新页面」极端场景下存在丢失窗口
（beforeunload 拦截 `hasPendingWriteOps` 已兜底提示）。

**缓解**：保留确认弹窗（防误删）；beforeunload 拦截；验证 3-5/3-6。

**应急**：若用户反馈强烈，submitNodeDelete 改回「await waitForPendingOps 后
再关弹窗」的折中（延迟 ~数百 ms，换取确定反馈）。

## R-09 undo × pendingCreates 竞态（严重度：中）

**机理**：新增节点 create 在飞时用户 undo → store 内存回滚 + reloadMindMap
重建画布；随后 create 响应返回，`writeBackendIdToNode` 向已重建的渲染树写
uid=旧值的映射（walk 找不到 uid → no-op）但 `uidToBackendId.set` 无条件执行 →
**映射表残留死条目**；后端也已保留该 create → F5 后「被撤销的节点复活」。

**缓解**（phase-0 任务 0.2）：undo/redo 前置 `waitForPendingOps`
（flush 防抖 + await opQueue + await pendingCreates），先落地再回滚。

**验证**：phase-0 验证 0-4；phase-3 验证 3-6。

**应急**：`waitForPendingOps` 内 pendingCreates 等待若超时（弱网），给
3s 超时上限 + 提示「有未完成的保存，撤销可能不一致」。

## R-10 detail diff O(N) 固定开销（严重度：低，接受）

**机理**：每条库命令触发 `addHistory` → 全树 stringify + detail 时
transformTreeDataToObject×2 + deepClone×2 + diff（Command.js）。命令化后
工具栏操作也承担该成本 —— 但相对被替换的「全量 setData + 整树布局渲染」
仍是数量级优势。5000+ 节点图可能出现命令间感知延迟。

**缓解**：当前接受；预留优化位 —— `processDataChangeDetail` 入口 rAF 合批、
或（库层面）listenerCount>0 时跳过无变化命令的 addHistory。

**验证**：verification.md §5.3 成本注记；大图基准跑一遍确认无感知卡顿。

**应急**：5000+ 节点场景若卡顿 → 提前实施 rAF 合批（改动局部于
processDataChangeDetail 入口，独立小任务）。

---

## 风险监控清单（实施期间持续核对）

- [ ] 每个 PR 自查 R-03（无 renderer 直调）
- [ ] 阶段 1 完成后专项跑 R-02 验证
- [ ] 阶段 3 开工前确认 R-07 选项（A/B）与 R-08 语义
- [ ] 阶段 4 合入前跑 R-04/R-05/R-06 三联验证（4-1/4-3/4-7）
- [ ] 全程留意后端日志 `Duplicate entry`（唯一索引回归，R-无直接关联但为
      handleStructuralChangesBatch 既有保障的回归哨兵）
