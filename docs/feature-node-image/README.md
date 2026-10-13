# 思维导图节点图片功能 — 设计方案

> 状态：待评审
> 目标版本：P1 MVP 可先行实施
> 原则：复用现有基础设施，后端零 schema 迁移，最小改动面

---

## 1. 需求概述

为思维导图节点添加图片能力：用户可在一个节点上挂载一张图片，图片随节点保存、加载、导出，并支持在画布上调整大小。

---

## 2. 现状盘点（方案依据）

| 现状 | 位置 | 对图片功能的意义 |
|---|---|---|
| 节点实体已有 `ExtraData` JSON 字段（nvarchar 32768），注释明确"用于附件、链接等" | [Node.cs](../../backend/MindMap.Api/Domain/Entities/Node.cs#L19-L80) | 图片元数据的现成存储位 |
| `ExtraData` 已贯穿 Create/Update/返回 DTO，服务层直接写回 | [NodeDtos.cs](../../backend/MindMap.Api/Application/DTOs/Nodes/NodeDtos.cs)、[NodeService.cs](../../backend/MindMap.Api/Application/Services/NodeService.cs#L165-L190) | 后端可能零改动 |
| simple-mind-map 原生支持 `image/imageTitle/imageSize` 数据字段，导出 xmind 已处理 `node.image` | [constant.js](../../frontend/node_modules/simple-mind-map/src/constants/constant.js#L178-L179)、[xmind.js](../../frontend/node_modules/simple-mind-map/src/utils/xmind.js#L104) | 渲染层零开发 |
| 已配置上传目录 `UploadRoot = "wwwroot/uploads"`，但**尚未配置 UseStaticFiles，也没有上传 Controller** | [AppOptions.cs](../../backend/MindMap.Api/Common/Options/AppOptions.cs#L8-L11) | 后端唯一需要新增的部分 |
| IFormFile 上传模式已有先例（导入接口，10MB 限制） | [ImportsController.cs](../../backend/MindMap.Api/Controllers/ImportsController.cs#L29-L66) | 上传 API 的参考范式 |
| 前端同步层 `dtoToNodeData()` 目前只映射 text/icon/shape/note 等 | [useMindMapSync.ts](../../frontend/src/views/editor/composables/useMindMapSync.ts#L436-L513) | 需要加 image 字段映射 |
| NodeToolbar 已有 icon 选择交互模式 | [NodeToolbar.vue](../../frontend/src/views/editor/NodeToolbar.vue#L96-L127) | 图片按钮的交互参照 |

---

## 3. 核心设计决策

### 决策 1：图片文件存储 → 本地文件 + GUID 文件名（推荐）

| 方案 | 评价 |
|---|---|
| ❌ base64 内嵌节点数据 | 击穿 ExtraData 32KB 上限，DB 膨胀、同步 payload 巨大，直接排除 |
| ✅ 上传到 `wwwroot/uploads/nodes/`，节点只存 URL | 复用已有 `UploadRoot` 配置；GUID 文件名防猜防撞；同源访问使 PNG/SVG 导出无 Canvas 污染问题 |
| ⏸ 对象存储（OSS/MinIO） | 当前阶段过度设计，留作未来演进（URL 存储方式天然兼容迁移） |

### 决策 2：图片元数据存储 → 复用 `ExtraData`（推荐）

```json
// node.ExtraData
{
  "image": {
    "url": "/uploads/nodes/202610/a3f8...c9.png",
    "width": 360,
    "height": 240,
    "title": "架构图"   // 可选，作为 alt/imageTitle
  }
}
```

理由：
- **零迁移**：`ExtraData` 已贯通实体、DTO、服务层、前端 `NodeUpdatePayload`，后端无需任何 schema/API 改动，符合最小改动原则；
- **前瞻兼容**：未来若支持多图（simple-mind-map 当前只渲染单图），JSON 结构可平滑扩展为 `images: [...]`；
- 32KB 只存元数据绰绰有余（约 150 字符），但需在约定中**明确禁止 base64 落入该字段**。

> 备选：给 Node 加 `ImageUrl/ImageWidth/ImageHeight` 三个专列（EF 迁移 + 3 处 DTO + 服务层）。更类型化、可查询，但触碰点多 5~6 处。如果图片未来会成为一级查询维度（如"搜所有带图节点"）再升级。

### 决策 3：渲染层接入 → 完全交给 simple-mind-map 原生能力

- 节点数据映射：`ExtraData.image` → simple-mind-map 的 `image`(src) / `imageTitle` / `imageSize:{width,height,custom:true}`，改动集中在 `dtoToNodeData()` 和回传（diff→API）两处；
- 注册 `NodeImgAdjust` 插件（库自带），画布上直接拖拽调整图片尺寸；
- 不需要自定义节点渲染。

---

## 4. 后端改动（仅 2 件事）

### 4.1 新增 `UploadsController`

参考 ImportsController 模式：

```
POST /api/uploads/node-image   [Authorize]
  IFormFile file（≤5MB，白名单：jpg/png/gif/webp，校验魔数而非仅扩展名）
  → { url: "/uploads/nodes/202610/{guid}.png", width, height }
```

- 目录规则：`{UploadRoot}/nodes/{yyyyMM}/{guid}{ext}`，按月分桶便于清理；
- 宽高后端用 ImageSharp 探测后返回（前端也可在选中文件时用 `Image.onload` 预读，双保险）；
- 需登录用户才能调用（防匿名刷存储）。

### 4.2 Program.cs 配置静态文件服务

`app.UseStaticFiles()`（目前未配置）→ `/uploads/*` 直接可访问。GUID 文件名即"能力 URL"，不可枚举，安全性与常见图床一致；若导图内容涉密可后续升级为鉴权文件接口。

---

## 5. 前端改动（4 件事）

1. **NodeToolbar 加"图片"按钮** → 打开 `n-modal preset="card"` 弹窗（符合项目弹窗规范），内含：上传（点击选择）、预览、宽/高微调（锁定比例）、title 输入、移除按钮；
2. **上传 API 封装**：`src/api/uploads.ts`，返回 url + 尺寸，前端回填 extraData；
3. **同步层**：`dtoToNodeData()` 加 image 三字段映射；反向在 diff/保存链路把 `image/imageSize` 写回 `extraData`。注意 `icon` 目前不在 diff 检测里，**image 应纳入**，否则画布上改图不会触发保存；
4. **注册 NodeImgAdjust 插件**（一行），获得画布拖拽调尺寸能力。

---

## 6. 边界问题清单（实施时必查）

- [ ] **图片加载失败**：渲染占位块 + 重试，避免节点塌陷（simple-mind-map 图片 404 时节点尺寸计算异常）；
- [ ] **节点删除/替换图片**：旧文件成孤儿。MVP 先接受（GUID 不占索引），二期做定期 GC 任务（扫描 uploads 与 DB 引用比对）；不要在节点删除时同步删文件——撤销恢复场景会翻车；
- [ ] **导出**：xmind 导出库已支持 `node.image`；PNG/SVG 导出因图片同源（wwwroot）无 Canvas 污染，正常工作；导出 JSON 需确认 extraData 随节点导出；
- [ ] **性能**：限制 `imageSize.custom` 显示上限（如宽 ≤500px），大图上传前前端 canvas 压缩（Phase 2）；
- [ ] **移动端**：弹窗内按钮 28px、支持触摸长按删除/预览大图；画布拖拽调尺寸在触摸设备的可用性需实测，不可用则只保留弹窗内数值调整；
- [ ] **粘贴上传**（Phase 2 亮点）：监听 paste 事件，剪贴板为图片时直接上传到选中节点，体验最好；
- [ ] **深色模式**：图片无需适配，但占位块/边框用 `var(--app-border)`。

---

## 7. 分阶段落地

| 阶段 | 内容 | 改动量 |
|---|---|---|
| **P1 MVP** | 上传 API + 静态文件 + 弹窗上传/预览/移除 + 同步映射 + NodeImgAdjust | 后端 2 文件，前端 ~4 文件 |
| **P2 体验** | 剪贴板粘贴、拖拽文件入节点、前端压缩、加载失败占位 | 纯前端 |
| **P3 治理** | 孤儿文件 GC、用户配额、（可选）多图支持 | 独立任务 |

---

## 8. 推荐结论

元数据走 `ExtraData`、文件走本地上传、渲染走库原生能力，后端几乎零改动、前端只加一个弹窗和映射，是改动面最小且不堵死未来演进的方案。

---

## 9. 实施状态（2026-10-13）

### 已完成 P1 MVP

| 模块 | 文件 | 说明 |
|---|---|---|
| 后端上传接口 | [UploadsController.cs](../../backend/MindMap.Api/Controllers/UploadsController.cs) | `POST /api/uploads/node-image`：≤5MB、扩展名白名单 + 文件头魔数校验、按月分桶存储、返回 URL + 宽高 |
| 后端尺寸探测 | [ImageFileHelper.cs](../../backend/MindMap.Api/Common/Helpers/ImageFileHelper.cs) | 无第三方依赖解析 PNG/JPEG/GIF/WebP 文件头宽高 |
| 静态文件服务 | [Program.cs](../../backend/MindMap.Api/Program.cs) | 新增 `UseStaticFiles()`，`/uploads/*` 直接可访问 |
| 前端上传封装 | [uploads.ts](../../frontend/src/api/uploads.ts) | `uploadNodeImage` + `readImageSize`（浏览器兜底）|
| 图片编辑弹窗 | [NodeImageModal.vue](../../frontend/src/views/editor/components/NodeImageModal.vue) | 上传/预览/宽高（锁定比例）/标题/移除，符合 n-modal preset=card 规范 |
| 工具栏按钮 | [NodeToolbar.vue](../../frontend/src/views/editor/NodeToolbar.vue) | 「图片」按钮 + has-image 高亮 |
| 同步层映射 | [useMindMapSync.ts](../../frontend/src/views/editor/composables/useMindMapSync.ts) | `dtoToNodeData` 还原 image/imageTitle/imageSize；diff extraFields 加入图片字段，画布操作自动落库 |
| 插件注册 | [MindMapEditorView.vue](../../frontend/src/views/editor/MindMapEditorView.vue) | `NodeImgAdjust` 注册（画布拖拽调尺寸）、`SET_NODE_IMAGE` 画布优先写入 |
| dev 代理 | [vite.config.ts](../../frontend/vite.config.ts) | `/uploads` 代理到后端 |

### 已完成 P2 体验

- **剪贴板粘贴**：Ctrl+V 无复制节点时读取剪贴板图片，直接上传挂到选中节点（保留节点粘贴优先级）；
- **拖拽文件入节点**：图片拖入画布 → 上传挂到选中节点；
- **加载失败占位**：MindMap 构造 `defaultNodeImage` 占位图，图片 404 不塌陷。

### 已完成 P3 治理

- **孤儿文件 GC**：[NodeImageCleanupService.cs](../../backend/MindMap.Api/Application/Services/NodeImageCleanupService.cs) 周期扫描 `uploads/nodes/**`，与全库 `ExtraData` 引用比对，删除未被引用且超过最小文件年龄（24h）的文件并清理空目录；配置见 `App.ImageCleanupIntervalHours / ImageCleanupMinFileAgeHours`。

### 本阶段明确不做（避免过度设计）

- **用户配额**：文件以 GUID 命名、无归属记录，做配额需新增上传归属表 + 迁移 + 统计接口，收益低，留待有实际需求时再做；
- **多图支持**：simple-mind-map 当前只渲染单图，JSON 结构已预留扩展为 `images: [...]` 的空间。

### 验证记录

- 后端 `dotnet build` 通过（0 警告 0 错误）；
- 前端 `npm run build`（vue-tsc + vite）通过。

