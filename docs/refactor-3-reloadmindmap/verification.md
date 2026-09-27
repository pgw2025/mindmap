# 验证手册：回归矩阵、阶段验证汇总与性能基准

> 各阶段验证项已分散在 phase-0 ~ phase-4 文档中；本手册汇总为三部分：
> ① 分阶段验证清单（速查）；② 全量回归矩阵（每阶段完成后必跑的公共回归）；
> ③ 性能基准方法与基线记录。

---

## 1. 分阶段验证速查表

| 阶段 | 验证项数量 | 关键项 |
|---|---|---|
| 阶段 0 | 7 项 | Ctrl+Z 归一（0-1）、库栈不可达（0-2）、编辑框内撤销（0-3）、undo 前置排空（0-4）、快捷键放行（0-5）、readonly（0-6） |
| 阶段 1 | 6 项 | 行为零变化（1-1）、helper 等价（1-2）、样式分支生效（1-3）、墨色抑制（1-4）、关页 flush（1-5）、失败重试（1-6） |
| 阶段 2 | 10 项 | 样式即时生效（2-1）、多选单请求（2-2）、F5 一致（2-3）、连续改样式（2-4）、内容保存（2-5）、icon（2-6）、墨色回归（2-7）、undo 矩阵（2-8）、移动端（2-9）、离线（2-10） |
| 阶段 3 | 11 项 | 新增子/同级（3-1/3-3）、折叠父新增（3-2）、删除（3-5）、新增即删竞态（3-6）、undo 矩阵（3-7）、方向（3-8）、唯一索引（3-11）、性能对比（3-10） |
| 阶段 4 | 8 项 | 样式粘贴（4-1）、根降级（4-2）、icon/正文（4-3）、原生粘贴缺陷修复（4-4）、收口 rg（4-5）、高频粘贴（4-7） |

## 2. 全量回归矩阵（每阶段完成后必跑）

### 2.1 画布原生交互（不可被破坏的既有能力）

| # | 操作 | 期望（与改造前一致） |
|---|---|---|
| R-01 | 选中节点按 Tab | 新子节点即时出现并落库；Ctrl+Z 一次还原 |
| R-02 | 选中节点按 Enter | 同级新增；位置=选中正下方 |
| R-03 | 双击节点进入编辑 → 改文字 → 失焦 | title 落库（icon 前缀剥离正确）；F5 保持 |
| R-04 | 选中节点按 Del（确认由库直接删） | 节点消失、落库级联删除 |
| R-05 | 拖拽节点换父级/换顺序 | parentId/sortOrder/direction 全部落库；F5 位置一致 |
| R-06 | 节点展开/折叠（含 `/` 快捷键） | isCollapsed 落库（**取反**正确） |
| R-07 | 「层级」菜单展开全部/收起到第 N 级 | 单次 batchUpdate；一条 undo |
| R-08 | 添加/删除关联线、添加摘要、外框 | extraData 落库；F5 保持 |
| R-09 | 备注面板输入 | note 落库（node.setData 链路不变） |
| R-10 | 多选（框选/Control+点选） | activeNodeIds 正确；多选样式批量走 batchUpdate |

### 2.2 工具栏操作（本改造对象）

| # | 操作 | 期望 |
|---|---|---|
| T-01 | 工具栏新增子/同级/删除/改样式/改内容/粘贴 | 全部即时生效（无整树重绘、视口/缩放/选中态保持）；F5 全部一致 |
| T-02 | 上述每项 undo → redo | 每次一键还原/重做；后端 diff 正确 |
| T-03 | 移动端底部工具栏同套操作 | 与桌面一致 |

### 2.3 同步与容错

| # | 场景 | 期望 |
|---|---|---|
| S-01 | 正常保存 | 同步状态指示 saved → idle |
| S-02 | 断网操作 | 状态转 error；恢复网络后重试成功；`hasPendingWriteOps` 在关页时拦截（beforeunload） |
| S-03 | 离线快照模式 | 操作入重试队列，不静默丢失；恢复后一致 |
| S-04 | readonly 模式 | 一切写入口禁用；Ctrl+Z 拦截 |
| S-05 | 主题/明暗/模板切换 | 墨色补丁生效；**零**后端写请求 |
| S-06 | 版本回滚 | reloadMindMap 保留路径正常；回滚后画布与后端一致 |
| S-07 | 分享页打开 | 只读渲染正常（convertToMindMapData 链路未受影响） |

### 2.4 undo/redo 全矩阵

对以下每种操作，执行 → undo → F5 校验 → redo → F5 校验：

| 操作类型 | undo 后 | redo 后 |
|---|---|---|
| 工具栏新增子 | 节点消失（前后端） | 节点恢复且落库 |
| 工具栏删除 | 节点及子树恢复 | 再次删除 |
| 工具栏改样式（单/多选） | 样式还原 | 样式恢复 |
| 工具栏改内容 | title/content 还原 | 恢复 |
| 工具栏改 icon | icon+title 还原（无前缀污染） | 恢复 |
| 工具栏粘贴 | 副本删除 | 副本恢复 |
| Ctrl+Z 快捷键入口 | 与按钮入口行为一致 | 一致 |

## 3. 验证环境与数据准备

- **测试导图**（建议 3 份）：
  1. `小图`：5 节点，全默认样式；
  2. `样式图`：20 节点，覆盖自定义 color/backgroundColor/borderColor/shape/
     edgeColor/edgeStyle/icon/note/关联线/摘要/外框；
  3. `大图`：≥1000 节点（可用脚本批量生成），用于性能基准。
- **浏览器**：Chrome / Edge 最新版 + 移动端模拟（375px 宽）。
- **网络面板**：全程开着，核对每次操作的请求数与 payload 字段。
- **后端校验**：每个验证点 F5 后以 Network 响应（而非仅画布）确认落库字段。

## 4. 常见故障速查

| 现象 | 最可能原因 | 定位 |
|---|---|---|
| 样式改了但 400ms 后没有 PUT | handleUpdate 样式分支未生效 / 命令绕过了 execCommand | 检查是否用了 `renderer.setNodeStyles` 直调（阶段 2 §0.3） |
| 切主题后大量 PUT color | inkPatchActive 未包裹 / async 竞态（阶段 1 任务 1.3 注意事项 1） | 用 `processDataChangeDetail` 传参方案 |
| Ctrl+Z 行为诡异（多退一步/后端多记录） | 库栈 BACK 未被拦截 | `beforeShortcutRun` 是否返回 true / 放行列表是否误伤 |
| 新增节点落库位置与画布不符 | 兄弟重编（handleStructuralChangesBatch）未触发 | 确认 detail 里父节点 update diff 存在（children 数组变化） |
| 粘贴无效果（无 create 请求） | appointData 带了 uid/backendId（0.3 陷阱） | `dtoToNodeData` 的 withUid 必须为 false |
| 粘贴后 title 出现 `🎉 ` 前缀污染 | extractTitleFromText 未能剥离（icon 透传方案未接） | 阶段 4 任务 4.1b 注意事项 1 的替代方案 |
| 删除后偶发「保存失败」+ 重试风暴 | 删除节点上还有飞行中的 text/style 防抖 | 确认 `clearPerNodeTimers` 已覆盖 styleDebounceTimers（阶段 1 任务 1.4 登记表） |
| F5 后样式/位置错乱 | 落库请求未 flush 就关闭 | beforeunload → flushPendingUpdates 是否覆盖新 Map |

## 5. 性能基准方法与记录

### 5.1 测量方法

```ts
// 在 DevTools Console 中执行（改造前先跑一遍留基线）
const t0 = performance.now()
// —— 执行被测操作（工具栏按钮路径，非快捷键）——
// 用 MutationObserver 或 node_tree_render_end 事件确定渲染完成：
mindMap.on('node_tree_render_end', () => {
  console.log('render 完成，距操作起点', performance.now() - t0, 'ms')
})
```

同时记录：① 操作→视觉反馈耗时；② `PUT/POST` 请求数与发出时刻；
③ `node_tree_render_end` 触发次数（改造前每次操作应 ≥1 次全量，
改造后应为 0 次 —— 用 SET_NODE_STYLES 局部路径时该事件不触发）。

### 5.2 基线记录表（实施时填写）

| 操作 | 图规模 | 改造前耗时 | 改造后耗时 | 请求数（前→后） |
|---|---|---|---|---|
| 单节点改颜色 | 1000 节点 | ___ ms（RTT+全量 render） | ___ ms（预期 <50ms） | 1 → 1 |
| 多选 5 节点改颜色 | 1000 节点 | ___ | ___ | 1 → 1 |
| 新增子节点 | 1000 节点 | ___ | ___ | 1-2 → 1 |
| 删除节点 | 1000 节点 | ___ | ___ | 1 → 1 |
| 粘贴 | 1000 节点 | ___ | ___ | 1 → 1 |

### 5.3 已知成本注记

detail 通道每条命令的固定开销：全树 `JSON.stringify`×1（addHistory getCopyData）+
`transformTreeDataToObject`×2 + `simpleDeepClone`×2 + diff（Command.js:105-133,
200-256）。1000 节点量级约数毫秒，相对全量 render（数十至数百 ms）为小头；
**5000+ 节点**场景若出现命令延迟，后续优化方向：detail 消费侧按帧合并（
processDataChangeDetail 入口加 rAF 合批），不在本改造范围。

## 6. 收口验收（最终）

- [ ] `rg -n "reloadMindMap\(" frontend/src` 与 phase-4 任务 4.2 预期表一致
- [ ] README.md §6 总验收标准全绿
- [ ] 本手册 §2 全量回归矩阵全绿
- [ ] 性能基线表填写完毕，改善幅度留档
- [ ] `docs/performance-optimization-plan.html` 改造 3 条目标记完成状态
