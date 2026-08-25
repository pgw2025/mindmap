# MindMap PWA 改造方案（只评审，不改代码）

> 结论：**可行，且改造成本低**。项目已具备约 20% 的 PWA 基础（manifest 雏形、meta 标签、HTTPS），核心缺口是 **Service Worker** 和 **合格的图标**。后端（ASP.NET Core）**零改动**。

---

## 1. 现状盘点

| 检查项 | 现状 | 结论 |
|---|---|---|
| HTTPS | nginx 监听 2131 SSL（`deploy/nginx-mindmap.conf`） | ✅ 已满足（PWA 硬性条件） |
| Web App Manifest | `frontend/public/manifest.json` 已存在 | ⚠️ 有雏形但不合格（见 2.2） |
| PWA meta 标签 | `index.html` 已有 theme-color / apple-mobile-web-app-* | ✅ 基本齐全，缺 apple-touch-icon |
| Service Worker | 全项目无任何 `navigator.serviceWorker` 注册 | ❌ **核心缺失** |
| 构建集成 | 未使用 vite-plugin-pwa / workbox | ❌ 缺失 |
| SPA 路由 | `createWebHistory` + nginx `try_files` 回落 | ✅ 与 SW navigateFallback 兼容 |
| 静态资源缓存 | nginx 对 js/css 等设置 30d immutable | ⚠️ 利于预缓存，但会误伤 `sw.js`（见 2.4） |
| 数据存储 | 全部数据在后端 MySQL，JWT 鉴权（`src/api/http.ts`） | ⚠️ 离线只能"外壳可用"，离线编辑需二期 |

构建产物：`dist` 共 3.2MB，最大 chunk `MindMapEditorView` 约 1MB —— 体量适中，完全适合 SW 预缓存。

---

## 2. 差距清单（按优先级）

### 2.1 缺 Service Worker（P0）
没有 SW 就没有离线能力，也不满足 Chrome/Edge 的最佳安装条件。这是本次改造的主体。

### 2.2 manifest 图标不合格（P0）
`public/manifest.json` 目前只有一个条目：
```json
{ "src": "/favicon.svg", "sizes": "any", "type": "image/svg+xml", "purpose": "any maskable" }
```
问题：
- Chrome/Edge 安装要求 **192px 和 512px 的 PNG 图标**（SVG + `sizes: any` 在部分场景不被采纳）；
- `"purpose": "any maskable"` 合并在一个图标上是 Lighthouse 明确 discourge 的写法，应拆成两个条目；
- **iOS 不支持 SVG 作为 apple-touch-icon**，需要 180×180 PNG；
- 缺 `id` 字段（PWA 身份标识，避免换域名/路径后被识别成两个应用）。

### 2.3 未集成构建工具（P0）
Vite 项目标准做法是 `vite-plugin-pwa`（底层 Google Workbox），自动生成带内容哈希的预缓存清单，无需手写 SW。

### 2.4 nginx 会把 sw.js 缓存 30 天（P0 隐蔽坑）
`deploy/nginx-mindmap.conf` 中：
```nginx
if ($uri ~* \.(?:js|css|woff2?|...)$) { expires 30d; ... immutable; }
```
`/sw.js` 命中 `\.js` 规则 → 浏览器会把 SW 本体缓存 30 天 immutable → **发新版后客户端 SW 更新检测被严重延迟，甚至长期不更新**。必须为 `sw.js` 和 `workbox-*.js` 单独加 no-cache 规则。

### 2.5 离线时 API 全挂（P1，产品决策）
数据全在后端，断网后应用能打开但所有请求报"网络错误"，无任何提示。至少应做离线状态提示；离线编辑属于大工程（见阶段三）。

---

## 3. 分阶段改造方案

### 阶段一：可安装（Installable PWA）—— 主体工作，约 6 个文件

**目标**：浏览器出现"安装"按钮 / iOS 添加到主屏幕，全屏独立窗口运行。

| # | 文件 | 改动 |
|---|---|---|
| 1 | `frontend/public/icons/`（新增） | 生成 `icon-192.png`、`icon-512.png`、`icon-512-maskable.png`（安全区内构图）、`apple-touch-icon.png`(180×180)，可由现有 `favicon.svg` 导出 |
| 2 | `frontend/public/manifest.json` | 重写 icons（any 与 maskable 拆成独立条目）；补 `id: "/"`、`lang: "zh-CN"`、`scope: "/"`；可选补 `screenshots`（增强安装弹窗） |
| 3 | `frontend/index.html` | 加 `<link rel="apple-touch-icon" href="/icons/apple-touch-icon.png">`（其余 meta 已有，不动） |
| 4 | `frontend/package.json` | devDependencies 增加 `vite-plugin-pwa` |
| 5 | `frontend/vite.config.ts` | 引入 `VitePWA` 插件，关键配置见下 |
| 6 | `frontend/src/main.ts` | 调用 `registerSW()`（来自 `virtual:pwa-register`） |

`vite.config.ts` 中 VitePWA 的关键参数（评审用，届时再写实现）：
```
registerType: 'prompt'                 // 有新版本时弹窗询问，避免编辑中途被自动刷新
includeAssets: ['favicon.svg', 'icons.svg']
workbox:
  navigateFallback: '/index.html'      // SPA 路由离线回落
  navigateFallbackDenylist: [/^\/api\//]   // API 绝不回落到 index.html
  globPatterns: ['**/*.{js,css,svg,png,woff2}']
  maximumFileSizeToCacheInBytes: 3MB   // MindMapEditorView chunk 约 1MB，默认 2MB 上限需调高
  cleanupOutdatedCaches: true
devOptions.enabled: false              // 本地用 vite preview 验证，不在 dev 模式挂 SW
```

**阶段一完成后即可达到：可安装、二次打开零网络加载外壳、静态资源全部走 SW 缓存。**

### 阶段二：离线体验 + 版本更新提示 —— 约 3 个文件

| # | 改动点 | 说明 |
|---|---|---|
| 1 | 新增 `src/pwa.ts`（或组件） | `registerSW({ onNeedRefresh })` → naive-ui 的 dialog 提示"发现新版本 → 刷新" |
| 2 | 新增离线状态提示 | 监听 `online`/`offline` 事件，断网时全局提示"当前离线，编辑不会被保存"（防止用户白写） |
| 3 | `workbox.runtimeCaching`（可选） | 只读 GET 接口（如健康检查、公开模板列表）可配 network-first + 兜底缓存；**`/api/auth/**` 与所有写操作保持 network-only**（JWT 数据不落缓存，避免鉴权数据被缓存复用） |
| 4 | `deploy/nginx-mindmap.conf` | **必须**增加（这是阶段一部署时就要一起改的）：`location = /sw.json`/`location ~ ^/(sw\.js\|workbox-.*\.js)$` → `Cache-Control: no-cache`；`manifest.json` 也建议 no-cache |

### 阶段三（可选，不建议首期做）：离线编辑与同步

- 前端加 IndexedDB（`idb`）：导图本地快照 + 编辑操作队列，联网后回放同步；
- 冲突处理复用后端已有的 versions API（`src/api/versions.ts`）做乐观锁；
- 工作量大（编辑器目前是在线保存模式，需改造保存链路 + 冲突 UI），收益取决于真实使用场景。**建议先上线阶段一/二，观察是否有离线编辑的真实需求再立项。**

---

## 4. 后端改动

**无。** PWA 是纯前端 + 部署层（nginx）的改造，ASP.NET Core API 不需要任何变化。（若未来做阶段三，后端才需要提供批量同步/冲突检测接口。）

---

## 5. 验证步骤（改造完成后）

1. 本地：`npm run build && npm run preview` → 打开 `http://localhost:4173`
   - DevTools → Application → Manifest：图标、id、installability 无报错
   - DevTools → Application → Service Workers：activated
   - Lighthouse → PWA 审计通过
2. 安装验证：Chrome 地址栏出现安装图标，安装后独立窗口打开；
   iOS Safari → 分享 → 添加到主屏幕（iOS 无安装横幅，只能手动）。
3. 离线验证：DevTools → Network → Offline → 刷新页面，外壳应正常打开并出现离线提示。
4. 更新验证：改一行代码重新 build 部署 → 老页面出现"发现新版本"提示。
5. 线上检查 `curl -I https://<host>:2131/sw.js`，确认 `Cache-Control: no-cache`（**不检查这项，nginx 旧配置会让更新失效 30 天**）。

---

## 6. 风险与注意事项

| 风险 | 说明 | 对策 |
|---|---|---|
| SW 更新滞留 | nginx 30d immutable 规则误伤 sw.js | 阶段一部署时同步改 nginx（方案 2.4） |
| 鉴权数据落缓存 | runtimeCaching 若配错会缓存带 JWT 响应 | API 默认 network-only，白名单才开缓存 |
| iOS 差异 | 无 beforeinstallprompt、不支持 maskable、推送受限 | 提供 apple-touch-icon + 引导文案即可，不依赖安装横幅 |
| 编辑中断 | registerType 若用 autoUpdate 会在后台重载页面 | 统一用 `prompt` 模式 |
| 存储被回收 | 浏览器可能自动清理缓存 | 可选调用 `navigator.storage.persist()` 提高保留优先级 |

---

## 7. 工作量预估

| 阶段 | 范围 | 规模 |
|---|---|---|
| 阶段一 | 可安装 + SW 预缓存 + nginx 修正 | 约 6 个文件，小 |
| 阶段二 | 更新提示 + 离线提示 + 只读接口缓存 | 约 3 个文件，小 |
| 阶段三 | 离线编辑同步（可选） | 前后端联动，大 |
