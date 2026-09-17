# MindMap 思维导图系统

一个前后端分离的在线思维导图应用,支持富文本节点编辑、版本管理、分享、模板、管理后台,并具备 **PWA 可安装** 与 **离线查看** 能力。

- 前端:Vue 3 + TypeScript + Vite + Pinia + Naive UI + simple-mind-map
- 后端:ASP.NET Core 8 (.NET 8) + EF Core + MySQL 8
- 部署:Nginx (HTTPS) + systemd,支持一键脚本

---

## ✨ 功能特性

### 导图编辑
- 基于 [simple-mind-map](https://github.com/wanglin2/mind-map) 的完整导图编辑,支持节点拖拽、折叠、移动与排序
- **富文本节点** 编辑(TipTap):正文、备注、文本对齐、下划线、链接
- **节点外框** (OuterFrame) 与样式面板,支持自定义外框样式
- **关联线** 连接任意节点,自定义端点方向与曲线弧度
- **多节点摘要**:选中多个节点一键生成摘要
- 实时同步状态指示、最后保存时间、手动保存按钮
- 未保存写入拦截 + 失败重试队列,防止数据丢失

### 内容管理
- 思维导图 CRUD、搜索、文件夹 / 标签分类管理
- **版本管理**:保存历史版本,可随时回滚
- **分享**:生成只读分享链接(分享页只读模式,禁止拖动编辑)
- **举报** 与 **模板系统**(内置模板 + 管理端维护)

### 导入导出
- 支持导出(PDF、图片等,基于 pdf-lib / jszip)与导入(xml-js 解析 XMind 等格式)

### 账号与管理后台
- 注册 / 登录、JWT 鉴权、访问令牌过期 **静默刷新** 自动重发
- 管理后台:用户管理、导图管理、举报审核、模板管理、数据看板(需 admin 角色)

### PWA 与离线
- **PWA**:可安装到桌面 / 主屏幕,Service Worker 离线外壳,二次打开零网络加载
- **离线查看**:在线时全量同步导图到浏览器 IndexedDB,断网后所有导图(列表 / 详情 / 节点)可正常浏览,并标注「离线数据 · 最后同步时间」
- 版本更新提示(registerType: prompt,避免编辑中途被刷新)

---

## 🧰 技术栈

| 层 | 技术 |
|---|---|
| 前端 | Vue 3 (`<script setup>`) · TypeScript · Vite 8 · Pinia · Vue Router · Naive UI · simple-mind-map · TipTap · axios |
| 前端工程 | vite-plugin-pwa (Workbox) · sass · vue-tsc |
| 后端 | ASP.NET Core 8 · EF Core 8 · Pomelo.EntityFrameworkCore.MySql · JWT Bearer · Serilog · Swashbuckle |
| 数据库 | MySQL 8 (utf8mb4) |
| 部署 | Nginx (HTTPS) · systemd · bash 部署脚本 · Docker(可选) |

---

## 📁 目录结构

```
MindMap/
├── backend/                  # 后端 API(ASP.NET Core 8)
│   └── MindMap.Api/
│       ├── Controllers/      # 接口层(14 个控制器)
│       ├── Application/      # 应用服务 + DTO
│       │   ├── Services/     #   业务逻辑(12 个服务)
│       │   └── DTOs/
│       ├── Domain/           # 领域层
│       │   └── Entities/     #   实体(User/Folder/Tag/MindMap/Node/Version/Share/Report/Template...)
│       ├── Infrastructure/   # 基础设施(EF Core DbContext + Migrations)
│       ├── Common/           # 通用(异常过滤器/JSON 转换器/选项)
│       ├── Security/         # 密码哈希 / JWT / 当前用户
│       ├── Program.cs        # 启动入口(迁移 + 默认管理员种子)
│       └── appsettings.json  # 配置(连接串 / Jwt / Serilog / CORS)
├── frontend/                 # 前端(Vue 3 + Vite)
│   └── src/
│       ├── api/              # HTTP 封装(axios + JWT 拦截 + 401 刷新)
│       ├── stores/           # Pinia 状态(auth/mindmaps/nodes/folders/tags/versions...)
│       ├── views/            # 页面(Home/editor/share/auth/admin)
│       ├── offline/          # 离线快照(IndexedDB 封装 + 同步 + 兜底)
│       ├── layouts/          # 布局(Default/Admin)
│       ├── pwa.ts            # Service Worker 注册与更新提示
│       └── router/           # 路由(含 admin 权限守卫)
├── deploy/                   # 部署脚本与配置
│   ├── 01-init-server.sh     #   服务器初始化(MySQL 建库/建账号、目录)
│   ├── 02-deploy-app.sh      #   应用部署(systemd + 配置生成)
│   ├── deploy.ps1            #   Windows 端上传脚本
│   ├── mindmap.service       #   systemd 单元文件
│   └── nginx-mindmap.conf    #   Nginx HTTPS 站点配置
├── docs/                     # 设计文档
│   ├── offline-mindmap-design.md  # 离线查看实现方案
│   └── pwa-upgrade-plan.md        # PWA 改造方案
├── publish/                  # 发布产物(供部署上传)
└── node_modules/             # 前端依赖
```

---

## 🚀 快速开始(本地开发)

### 环境要求

- .NET 8 SDK
- Node.js ≥ 20
- MySQL 8(本地或远程)

### 1. 初始化数据库

```sql
CREATE DATABASE IF NOT EXISTS mindmap CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

### 2. 配置后端

编辑 `backend/MindMap.Api/appsettings.json`,修改连接串与 JWT 密钥:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=mindmap;User=root;Password=你的密码;CharSet=utf8mb4;AllowUserVariables=true;"
  },
  "Jwt": {
    "Issuer": "MindMap",
    "Audience": "MindMap",
    "SigningKey": "至少32位随机密钥"
  }
}
```

> 启动时后端会自动执行 EF Core 迁移建表,并创建默认管理员 `admin / Admin@2026`(首次登录后请立即修改密码)。

### 3. 启动后端

```bash
cd backend/MindMap.Api
dotnet run
```

- API 地址:`http://localhost:5000`
- Swagger 文档:`http://localhost:5000/swagger`
- 健康检查:`GET /api/health`

### 4. 启动前端

```bash
cd frontend
npm install
npm run dev
```

- 前端地址:`http://localhost:5173`(开发服务器已配置 `/api` 代理到 `http://localhost:5000`)
- 访问页面,使用默认管理员 `admin / Admin@2026` 登录

### 5. 生产构建预览(验证 PWA / 离线)

```bash
cd frontend
npm run build && npm run preview   # http://localhost:4173
```

> 注意:离线外壳 / Service Worker 只在 `build + preview` 下生效,`npm run dev` 刻意不挂载 SW。

---

## 📦 部署(生产)

部署脚本位于 `deploy/`,以 Nginx + systemd 方式运行,HTTPS 默认端口 **2131**。

```bash
# 1. 服务器端:初始化环境(安装 Nginx / ASP.NET Core 8 Runtime、创建 MySQL 库与账号)
bash 01-init-server.sh

# 2. Windows 本地:构建并上传产物
./deploy.ps1

# 3. 服务器端:部署应用(生成生产配置、安装 systemd 服务)
bash 02-deploy-app.sh <SERVER_PUBLIC_IP>
```

关键配置说明:

| 文件 | 用途 |
|---|---|
| `deploy/mindmap.service` | systemd 服务定义(守护 API 进程) |
| `deploy/nginx-mindmap.conf` | Nginx 站点:静态资源 + `/api` 反向代理 + HTTPS;`sw.js` / `workbox-*.js` 单独配置 `no-cache`,避免 SW 更新被缓存阻塞 |
| `deploy/appsettings.Production.json` | 生产配置模板(部署脚本会现场生成随机 JWT 密钥并回填数据库密码) |

---

## 🧩 核心设计

### 数据模型(后端)

`User` / `Folder` / `Tag` / `MindMap` / `Node` / `MindMapVersion` / `MindMapShare` / `MindMapReport` / `Template` / `RefreshToken`。

- 导图包含节点树,节点支持富文本内容、备注、外框、关联线等扩展数据
- 版本快照支持历史回溯
- 分享基于 token 的只读链接

### 同步与离线(前端)

- 在线编辑走串行同步队列,失败自动进入重试队列(按字段类型隔离去重)
- `src/offline/` 三件套:`db.ts`(IndexedDB 封装)、`sync.ts`(全量快照同步,并发 3)、`fallback.ts`(网络失败读本地兜底)
- 换号登录自动清库重同步(`meta.syncOwner` 校验),快照带 `syncedAt` 时间戳
- 鉴权数据刻意不进 Cache Storage,只读公开接口才配置 network-first 缓存

---

## 📚 文档

- [`docs/offline-mindmap-design.md`](docs/offline-mindmap-design.md) — 离线查看实现方案(IndexedDB 快照选型、数据流、文件级改动清单)
- [`docs/pwa-upgrade-plan.md`](docs/pwa-upgrade-plan.md) — PWA 改造方案(三阶段实施计划)

---

## 🔐 安全提示

- 部署生产环境时,务必修改默认管理员密码与 `appsettings.Production.json` 中的 JWT 密钥(部署脚本会自动生成随机密钥)
- JWT 访问令牌 120 分钟有效,刷新令牌 14 天;前端在 401 时静默刷新并重发请求
- 生产 CORS 白名单仅包含部署域名
