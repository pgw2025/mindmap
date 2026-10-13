# nodes 表死字段清理方案（评审稿）

> 状态：仅方案，未改任何代码。目标：把「派生值/已废弃」字段从 `nodes` 表与前后端链路中移除，保留「用户覆盖」类字段。
> 依据：2026-10-13 实测（nodes 198 行）+ 全链路代码走查。

---

## 一、结论速览

| 字段 | 现状 | 处置建议 | 依据 |
|---|---|---|---|
| `x` / `y` | 0 行非 NULL，前端 `dtoToNodeData` 不消费 | **删除** | 树形布局引擎自动算坐标，手动坐标反模式 |
| `width` / `height` | 0 行非 NULL，前端不消费 | **删除** | 尺寸是内容/字号/主题的派生值，存了必过期 |
| `color` | 9 行（空串，被墨色解算清空） | **删除** | 字色已由「底色解算 + 主题层级」覆盖，`color` 语义已塌陷 |
| `fontSize` | 0 行非 NULL | **保留** | 「用户单节点字号覆盖」正当字段，UI 能力未补上 |
| `fontFamily` | 0 行非 NULL | **保留** | 同上，价值低但删除收益≈0 |

**核心原则**：删「派生值字段」（坐标/尺寸/字色），留「用户覆盖字段」（字号/字体）。

---

## 二、关键发现（影响删除决策的代码事实）

1. **`x/y/width/height` 是真正的「零消费」死字段**：
   - 前端唯一映射函数 `useMindMapSync.ts` 的 `dtoToNodeData` 从头到尾不读这四个字段。
   - `ImportService.MaterializeNode` 连 `ParsedNode` 里都没有这四个字段（导入不写坐标/尺寸）。
   - 画布坐标/尺寸由 simple-mind-map 布局引擎运行时计算，且不回写。

2. **`color` 的语义已经塌陷**（这是它该删的关键证据）：
   - 实测 9 个非 NULL 的 `color` 全是**空字符串** `''`，且这 9 行恰好都带 `BackgroundColor = #f0a020`。
   - 即：用户设了底色 → 墨色解算机制（`themes/nodeInk.ts`）算出深色文字 → 前端用「清空 `color`」表达「不手动指定字色、交回主题/解算」。
   - 于是 `color` 字段现在存在三种状态混用：`NULL`（没设）、`''`（主动清空）、`'#xxxxxx'`（手动指定）——语义不清，是技术债。

3. **`color/fontSize/fontFamily` 并非完全无用**，删除前必须一并处理这些消费点：
   - `ImportService`：导入 XMind/JSON 时写入 `Color/FontSize/FontFamily`（`MaterializeNode`、`Parse*` 多处）。
   - `ExportService`：导出 FreeMind/JSON 时读 `node.Color`（`ExportService.cs:81-82`）。
   - `MindMapVersionService` + `VersionNodeData`：版本快照序列化/反序列化这三个字段。
   - `ShareService`：分享页节点 DTO 透传这三个字段。
   - `NodeService`：CRUD / 批量 / DTO 映射全链路。

---

## 三、分字段处置方案

### 3.1 删除 `x` / `y` / `width` / `height`（低风险）

**后端**：
- `Domain/Entities/Node.cs`：删 4 个属性。
- `Infrastructure/Data/Configurations/NodeConfiguration.cs`：删 4 行 `HasPrecision`。
- `Application/DTOs/Nodes/NodeDtos.cs`：`NodeCreateRequest` / `NodeUpdateRequest` / `NodeBatchItem` / `NodeDto` 删对应字段。
- `Application/Services/NodeService.cs`：删 `GetByMindMapAsync` / `CreateAsync` / `UpdateAsync` / `BatchUpdateAsync` / `ToDto` / `ToTreeNodeDto` 中的映射行。
- `Application/Services/ShareService.cs`、`MindMapService.cs`：删 DTO 映射行。
- `Application/DTOs/Versions/VersionDtos.cs` + `MindMapVersionService.cs`：删版本快照中的 4 字段。
- 新增 EF 迁移：`DropColumn X/Y/Width/Height`。

**前端**：
- `api/nodes.ts`：`NodeDto` / `NodeCreatePayload` / `NodeUpdatePayload` / `NodeBatchItem` 删 4 字段。
- `stores/nodes.ts`：`batchUpdate` 回填、`applyUndo` 的 batch 分支删 `x/y` 相关行。
- 检查 `useMindMapSync.ts` 是否残留引用（结论：无，本就未消费）。

**风险**：极低。这 4 字段无任何写入口（导入不写、前端不写），历史数据全 NULL，删列无损。

### 3.2 删除 `color`（中风险，需先决策）

**前置决策**：删除前必须确认「字色完全交给底色解算 + 主题」这一方向成立。当前项目记忆已明确「墨色由生效底色解算，`color` 不在反向同步回写清单中」，方向是一致的。

**后端**：
- 同上路径删 `Color` 字段。
- **重点处理 `ImportService` / `ExportService`**：
  - 导入：XMind/JSON 里的字色信息 → 要么丢弃，要么**转为 `backgroundColor` 或直接忽略**（需产品决策）。
  - 导出：`ExportService.cs:81-82` 读 `node.Color` 写 `COLOR` 属性 → 改为不导出，或导出「解算后的颜色」（但解算逻辑在前端，后端拿不到，故只能省略）。

**前端**：
- `useMindMapSync.ts`：删 `dtoToNodeData` 的 `color` 映射 + `styleFieldPairs` 的 color 项 + `handleUpdate` 里「清空 color」的 hack。
- `NodeToolbar.vue`：删颜色选择入口（或改为「只改底色」，字色由解算自动跟随）。
- `stores/nodes.ts`：删 color 相关回填。

**风险**：中。影响导入/导出/版本快照/分享四处，且 `NodeToolbar` 当前颜色选择 UI 是「选字色」，删字段需同步改交互语义（选底色 or 移除）。**需产品确认**。

### 3.3 保留 `fontSize` / `fontFamily`（本次不动）

- 属于「用户手动覆盖」正当字段，后端/前端接线完整（`dtoToNodeData` 有正向映射、`handleUpdate` 有反向回写、导入导出都有）。
- 当前 0 行非 NULL，唯一问题是**前端没有真正触发写库的 UI 入口**（`NodeToolbar` 有字号选择但实测没落库）。
- 处置方向（本次不实施，仅记录）：补全「单节点字号/字体覆盖」的 UI 落地，让字段真正被使用；或确认不需要后一并清理。

---

## 四、推荐执行顺序（若决定动手）

1. **第一批（低风险，可独立交付）**：删 `x/y/width/height` 4 字段 —— 纯删除，无语义决策，可先做并验证回归。
2. **第二批（需产品决策）**：删 `color` —— 需先拍板「导入导出的字色如何降级」「工具栏颜色入口改成选底色还是移除」。
3. **第三批（可选）**：`fontSize/fontFamily` 补 UI 或清理，视后续规划。

---

## 五、不建议删除 / 保留的理由汇总

- `backgroundColor`、`borderColor`、`edgeColor`、`shape`、`icon`、`edgeStyle`、`direction`：均为「用户覆盖/语义明确」字段，与本文无关，勿动。
- `direction`（14 行非 NULL）是根节点生长方向，有真实消费，保留。

---

## 六、待你确认的决策点

1. 是否同意「先删 `x/y/width/height` 四个死字段」作为第一批独立交付？
2. `color` 删除方向：导入的字色信息是**丢弃**还是**降级为底色**？工具栏的颜色入口改为**选底色**还是**直接移除**？
3. `fontSize/fontFamily` 是**保留待用**还是**一并清理**？

确认后我再出具体改动 diff 供你评审，不擅自提交。
