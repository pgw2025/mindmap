@echo off
chcp 65001 >nul
title MindMap 一键启动
setlocal

REM ---- 子窗口分支（勿删）----
if "%~1"=="--backend" goto :run_backend
if "%~1"=="--frontend" goto :run_frontend

set "HEALTH_URL=http://localhost:5000/api/health"

echo.
echo ============================================
echo           MindMap 一键启动
echo ============================================
echo.

REM ---- 依赖检查 ----
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 dotnet，请先安装 .NET 8 SDK
    echo         下载地址: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
where node >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 node，请先安装 Node.js
    echo         下载地址: https://nodejs.org
    pause
    exit /b 1
)
where npm >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 npm
    pause
    exit /b 1
)

REM ---- 1. 启动后端（独立窗口，先 dotnet clean 清理再运行）----
echo [1/3] 启动后端：先清理项目，再 dotnet run ...
start "MindMap-Backend" cmd /k ""%~f0" --backend"

REM ---- 2. 等待后端健康检查通过（最多 60 秒）----
echo [2/3] 等待后端就绪: %HEALTH_URL%
set /a tries=0
:wait_loop
set /a tries+=1
if %tries% gtr 30 goto :wait_timeout
curl -s -f -o nul --connect-timeout 2 "%HEALTH_URL%"
if not errorlevel 1 goto :backend_ready
timeout /t 2 /nobreak >nul
goto :wait_loop

:wait_timeout
echo [警告] 后端 60 秒内未就绪，仍继续启动前端（请留意后端窗口报错）。
goto :after_wait

:backend_ready
echo [OK] 后端已就绪（第 %tries% 次检查通过）。

:after_wait
REM ---- 3. 启动前端（独立窗口）----
echo [3/3] 启动前端 Vite dev server ...
start "MindMap-Frontend" cmd /k ""%~f0" --frontend"

REM ---- 打开浏览器 ----
start "" http://localhost:5173

echo.
echo ============================================
echo   全部启动完成！
echo   前端: http://localhost:5173
echo   后端: http://localhost:5000 （Swagger: /swagger）
echo   停止: 直接关闭对应的两个黑色窗口即可
echo ============================================
pause
endlocal
exit /b

REM ===================== 后端 =====================
:run_backend
title MindMap-Backend
cd /d "%~dp0backend\MindMap.Api"
echo [后端] 清理项目 dotnet clean ...
dotnet clean -v q
echo [后端] 清理完成，开始启动 dotnet run ...
dotnet run --launch-profile http
exit /b

REM ===================== 前端 =====================
:run_frontend
title MindMap-Frontend
cd /d "%~dp0frontend"
if not exist node_modules (
    echo [前端] 首次运行，正在安装依赖 npm install ...
    call npm install
    if errorlevel 1 (
        echo [前端] npm install 失败，请检查网络后重试。
        pause
        exit /b 1
    )
)
echo [前端] 启动 Vite dev server npm run dev ...
call npm run dev
exit /b
