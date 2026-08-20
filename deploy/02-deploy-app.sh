#!/usr/bin/env bash
# ============================================================================
# Mindmap 笔记系统 - 服务器端部署脚本（上传完文件后，在服务器执行它）
# 使用：bash 02-deploy-app.sh <SERVER_PUBLIC_IP>
#   SERVER_PUBLIC_IP 仅用于生成 JWT 示例密钥提示，不影响运行。
# ============================================================================
set -euo pipefail

echo -e "\e[32m==== 0. 检查目录 ====\e[0m"
[ -d /opt/mindmap/backend ] || { echo "/opt/mindmap/backend 不存在，请先执行 01-init-server.sh"; exit 1; }
[ -d /usr/share/nginx/mindmap ] || { echo "/usr/share/nginx/mindmap 不存在"; exit 1; }

echo -e "\e[32m==== 1. 准备 appsettings.Production.json ====\e[0m"
APPSETTINGS=/opt/mindmap/backend/appsettings.Production.json
if [ -f "$APPSETTINGS" ]; then
  echo "已存在 $APPSETTINGS，保留原有配置（删除后重新部署可覆盖）。"
else
  # 从模板生成（要求用户填入密码与密钥）
  if [ -f /root/.mindmap.env ]; then
    # shellcheck disable=SC1091
    . /root/.mindmap.env
  fi

  read -r -s -p "请输入 MySQL 专用账号 Mindmap 的密码 [从 01-init 记住的 MINDMAP_DB_PW]: " MINDMAP_DB_PW_USER
  if [ -n "$MINDMAP_DB_PW_USER" ]; then MINDMAP_DB_PW="$MINDMAP_DB_PW_USER"; fi
  while [ -z "${MINDMAP_DB_PW:-}" ]; do read -r -s -p "密码不能为空，重新输入: " MINDMAP_DB_PW; done
  echo

  # 生成随机 64 字节 JWT Key
  JWT_KEY="$(head -c 64 /dev/urandom | base64 -w0)"

  cp /opt/mindmap/_deploy_tmp/appsettings.Production.json "$APPSETTINGS" 2>/dev/null || {
    echo "请先上传 deploy/appsettings.Production.json 模板到 /opt/mindmap/_deploy_tmp/";
    exit 1;
  }
  sed -i "s|__DB_PASSWORD__|${MINDMAP_DB_PW}|g"       "$APPSETTINGS"
  sed -i "s|__JWT_SECRET_KEY__|${JWT_KEY}|g"        "$APPSETTINGS"
  echo "JWT Key 已随机生成并写入配置。如需备份请查看: $APPSETTINGS"
fi

echo -e "\e[32m==== 2. 停止旧服务 → 复制新文件 → 赋权 ====\e[0m"
systemctl stop mindmap 2>/dev/null || true

# backend 目录内容已在 /opt/mindmap/backend，只做赋权
chown -R nginx:nginx /opt/mindmap/backend
chmod -R u=rwX,g=rX,o=rX /opt/mindmap/backend
chown -R nginx:nginx /opt/mindmap/backend/Uploads 2>/dev/null || mkdir -p /opt/mindmap/backend/Uploads && chown -R nginx:nginx /opt/mindmap/backend/Uploads

# 前端 dist
if [ -d /opt/mindmap/_deploy_tmp/dist ]; then
  rm -rf /usr/share/nginx/mindmap/*
  cp -a /opt/mindmap/_deploy_tmp/dist/. /usr/share/nginx/mindmap/
  chown -R nginx:nginx /usr/share/nginx/mindmap
  chmod -R u=rwX,g=rX,o=rX /usr/share/nginx/mindmap
else
  echo -e "\e[33m未检测到 /opt/mindmap/_deploy_tmp/dist，跳过前端更新。\e[0m"
fi

echo -e "\e[32m==== 3. 复制 systemd 与 Nginx 配置 ====\e[0m"
if [ -f /opt/mindmap/_deploy_tmp/mindmap.service ]; then
  cp /opt/mindmap/_deploy_tmp/mindmap.service /etc/systemd/system/mindmap.service
  systemctl daemon-reload
  systemctl enable mindmap
fi
if [ -f /opt/mindmap/_deploy_tmp/nginx-mindmap.conf ]; then
  cp /opt/mindmap/_deploy_tmp/nginx-mindmap.conf /etc/nginx/conf.d/mindmap.conf
  # 默认移除 default.conf，避免端口冲突
  if [ -f /etc/nginx/conf.d/default.conf ]; then
    mv /etc/nginx/conf.d/default.conf /etc/nginx/conf.d/default.conf.disabled 2>/dev/null || true
  fi
  nginx -t && systemctl reload nginx
fi

echo -e "\e[32m==== 4. 启动 API 服务（自动执行 EF Core Migrations）====\e[0m"
systemctl start mindmap
sleep 3
systemctl status mindmap --no-pager || true

echo
echo -e "\e[32m==== 部署完成！健康检查 ====\e[0m"
echo "  后端：curl -s http://127.0.0.1:5000/api/auth/me  (应返回 401 Unauthorized)"
echo "  前端：浏览器打开 http://<服务器公网IP>"
echo "  日志：journalctl -u mindmap -f"
