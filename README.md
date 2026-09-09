# IdleMonitor

IdleMonitor 是一个 Windows 用户空闲状态监控工具，包含：

- Windows 托盘客户端；
- FastAPI + SQLite 服务端；
- Web 管理面板；
- 客户端 API 文档和健康检查接口。

客户端通过 Windows `GetLastInputInfo` API 检测用户空闲时间，在达到配置阈值后向服务端上报事件。服务端保存设备与事件数据，并通过 API 向客户端下发监控配置。

> 当前文档对应版本：[`v1.1.0`](https://github.com/Felix666-ship-It/IdleMonitor/releases/tag/v1.1.0)

## 快速开始

### 1. 下载客户端

前往 [Releases](https://github.com/Felix666-ship-It/IdleMonitor/releases/latest) 下载：

```text
IdleMonitor.exe
```

客户端是便携版，不需要安装程序。将 `IdleMonitor.exe` 放到任意目录后双击运行即可。

### 2. 配置服务端地址

运行客户端后，在系统托盘找到 IdleMonitor，打开 **打开设置**，填写：

```text
启动上报： https://your-domain.example/api/report
配置接口： https://your-domain.example/api/config
空闲上报： https://your-domain.example/api/idle-report
API Token：服务端生成的客户端 Bearer Token
```

远程地址必须使用 HTTPS。HTTP 仅允许用于本机 `localhost` 开发测试。

### 3. 打开 Web 面板

浏览器访问：

```text
https://your-domain.example/admin
```

Web 面板使用管理员账号和密码登录，可以查看设备、事件以及修改监控配置。

## 在线服务端示例

本项目测试服务端地址：

- 首页：<https://12332131.935282.xyz:8443/>
- Web 管理面板：<https://12332131.935282.xyz:8443/admin>
- API 文档：<https://12332131.935282.xyz:8443/api-docs>
- 接入信息：<https://12332131.935282.xyz:8443/api/info>
- 健康检查：<https://12332131.935282.xyz:8443/healthz>

对应客户端 API：

```text
https://12332131.935282.xyz:8443/api/report
https://12332131.935282.xyz:8443/api/config
https://12332131.935282.xyz:8443/api/idle-report
```

> API Token 和 Web 管理员密码不在 README、网页或 Release 中公开。请从服务端受限配置文件中取得，并通过客户端设置页填写。

## API 接口

客户端接口都需要请求头：

```http
Authorization: Bearer <API_TOKEN>
```

### 启动上报

```http
POST /api/report
Content-Type: application/json
Authorization: Bearer <API_TOKEN>
```

请求体：

```json
{
  "deviceId": "uuid-or-device-id",
  "localIP": "192.168.1.20",
  "event": "startup"
}
```

成功响应：

```json
{
  "accepted": true
}
```

### 读取配置

```http
GET /api/config?deviceId=<DEVICE_ID>
Authorization: Bearer <API_TOKEN>
```

成功响应：

```json
{
  "idleThreshold": 420,
  "configPollInterval": 120
}
```

字段说明：

| 字段 | 类型 | 说明 | 范围 |
|---|---:|---|---:|
| `idleThreshold` | integer | 空闲触发阈值，单位为秒 | 1–86400 |
| `configPollInterval` | integer | 配置轮询间隔，单位为秒 | 60–86400 |

### 空闲事件上报

```http
POST /api/idle-report
Content-Type: application/json
Authorization: Bearer <API_TOKEN>
```

请求体：

```json
{
  "deviceId": "uuid-or-device-id",
  "idleSeconds": 420,
  "thresholdSeconds": 420,
  "event": "idle_exceeded"
}
```

成功响应：

```json
{
  "accepted": true
}
```

### 健康检查

```http
GET /healthz
```

成功响应：

```json
{
  "status": "ok",
  "time": "2026-09-08T13:52:14+00:00"
}
```

### 机器可读接入信息

```http
GET /api/info
```

该接口公开服务地址、页面地址和三个客户端 API 地址，但不会返回 API Token 或管理员密码。

## Web 管理面板

访问 `/admin` 并使用 HTTP Basic Authentication 登录。

面板提供：

- 客户端三个对接 URL；
- 当前服务端公共地址；
- 设备列表；
- 最近事件记录；
- 当前空闲阈值；
- 配置轮询间隔；
- 修改并保存服务端配置。

管理接口：

```text
GET  /admin/api/summary
PUT  /admin/api/config
```

管理接口只接受管理员 Basic Authentication，不应配置到 Windows 客户端中。

## 服务端部署

服务端文件位于 [`server/`](server/)：

```text
server/
├── app.py
├── requirements.txt
├── idlemonitor-server.service
└── .env.example
```

### 环境要求

- Debian/Ubuntu 或其他 Linux 发行版；
- Python 3.11 或更高版本；
- systemd；
- Nginx 或其他 HTTPS 反向代理；
- 一个已解析到服务器的域名；
- 有效的 TLS 证书。

### 手动运行

```bash
cd server
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt

export IDLEMONITOR_PUBLIC_BASE_URL="https://your-domain.example"
export IDLEMONITOR_API_TOKEN="replace-with-a-long-random-token"
export IDLEMONITOR_ADMIN_USERNAME="admin"
export IDLEMONITOR_ADMIN_PASSWORD="replace-with-a-strong-password"

uvicorn app:app --host 127.0.0.1 --port 8080
```

### systemd 运行

创建服务用户和目录：

```bash
sudo useradd --system --home-dir /opt/idlemonitor-server \
  --shell /usr/sbin/nologin idlemonitor
sudo mkdir -p /opt/idlemonitor-server/data
sudo chown -R idlemonitor:idlemonitor /opt/idlemonitor-server
```

复制服务端文件到 `/opt/idlemonitor-server`，创建环境文件：

```bash
sudo install -m 0640 -o root -g idlemonitor \
  server/.env.example /etc/idlemonitor-server.env
sudoedit /etc/idlemonitor-server.env
```

至少配置：

```dotenv
IDLEMONITOR_PUBLIC_BASE_URL=https://your-domain.example
IDLEMONITOR_API_TOKEN=use-a-random-client-token
IDLEMONITOR_ADMIN_USERNAME=admin
IDLEMONITOR_ADMIN_PASSWORD=use-a-strong-admin-password
```

安装服务单元：

```bash
sudo install -m 0644 server/idlemonitor-server.service \
  /etc/systemd/system/idlemonitor-server.service
sudo systemctl daemon-reload
sudo systemctl enable --now idlemonitor-server
sudo systemctl status idlemonitor-server
```

### Nginx 反向代理

Uvicorn 默认只监听 `127.0.0.1:8080`。生产环境应由 Nginx 负责公网监听和 HTTPS：

```nginx
server {
    listen 80;
    server_name your-domain.example;

    location /.well-known/acme-challenge/ {
        root /var/www/idlemonitor-acme;
    }

    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl http2;
    server_name your-domain.example;

    ssl_certificate     /etc/letsencrypt/live/your-domain.example/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/your-domain.example/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

配置完成后检查：

```bash
sudo nginx -t
sudo systemctl reload nginx
curl -fsS https://your-domain.example/healthz
```

可使用 Certbot 申请和自动续期证书：

```bash
sudo certbot --nginx -d your-domain.example
sudo certbot renew --dry-run
```

## 客户端本地数据

客户端数据存放在：

```text
%LOCALAPPDATA%\IdleMonitor\device.json
%LOCALAPPDATA%\IdleMonitor\config.json
%LOCALAPPDATA%\IdleMonitor\api_config.txt
%LOCALAPPDATA%\IdleMonitor\idlemonitor.log
```

其中 `api_config.txt` 保存 API 地址和客户端 Token，请限制本机用户访问权限，不要上传或提交该文件。

## 客户端功能

- Windows 托盘常驻；
- 单实例运行；
- 尝试设置当前用户开机启动；
- 空闲阈值动态更新；
- 空闲事件去重；
- 网络请求超时；
- 临时网络错误有限重试；
- 退出时停止检测线程和配置服务；
- 支持暂停/恢复数据采集；
- 默认不查询、不上报公网 IP；
- 本地滚动日志。

## 安全与隐私

- 生产环境必须使用 HTTPS。
- 客户端 API 必须启用 Bearer Token。
- Token 不应放在 URL 查询参数、日志、截图或公开文档中。
- Web 管理面板必须使用强管理员密码。
- 建议定期轮换客户端 Token 和管理员密码。
- 建议通过防火墙限制管理面板的访问来源。
- 默认不采集公网 IP；如业务需要，应单独评估隐私和合规要求。
- 服务端仅保存实现业务所需的设备和事件数据。
- 日志不得记录 Token、管理员密码或完整请求体。

> 当前客户端认证是 Bearer Token。若部署在不可信网络或大规模生产环境，建议进一步增加设备注册、Token 轮换、HMAC 请求签名、时间戳和防重放机制。

## 构建客户端

需要 Windows、Visual Studio 2022 Build Tools 或 Visual Studio，并安装：

- .NET Framework 4.8 Developer Pack；
- .NET desktop build tools；
- MSBuild。

执行：

```powershell
msbuild .\src\IdleMonitor\IdleMonitor.csproj /t:Rebuild /p:Configuration=Release
```

或使用已安装的 .NET SDK：

```powershell
dotnet restore .\src\IdleMonitor\IdleMonitor.csproj
dotnet build .\src\IdleMonitor\IdleMonitor.csproj --configuration Release --no-restore
```

构建产物：

```text
src\IdleMonitor\bin\Release\net48\IdleMonitor.exe
```

## 文件校验

下载 Release 后可使用 PowerShell 校验 SHA-256：

```powershell
Get-FileHash .\IdleMonitor.exe -Algorithm SHA256
```

请以对应 Release 页面公布的校验值为准。

## 故障排查

### 打开域名显示 404

确认访问的是：

```text
/
/admin
/api-docs
```

服务端根路径现在应显示公开首页，而不是直接访问不存在的路径。

### API 返回 401

检查客户端是否配置了正确的：

```http
Authorization: Bearer <API_TOKEN>
```

并确认服务端 `/etc/idlemonitor-server.env` 中的 Token 与客户端设置一致。

### API 返回 503

通常表示服务端没有配置：

```text
IDLEMONITOR_API_TOKEN
IDLEMONITOR_ADMIN_PASSWORD
```

检查：

```bash
sudo systemctl status idlemonitor-server
sudo journalctl -u idlemonitor-server -n 100 --no-pager
```

### 客户端没有数据

依次检查：

1. 客户端三个 URL 是否指向同一个 HTTPS 域名；
2. API Token 是否正确；
3. 服务端健康检查是否返回 `{"status":"ok"}`；
4. 客户端日志：

   ```text
   %LOCALAPPDATA%\IdleMonitor\idlemonitor.log
   ```

5. 服务端日志：

   ```bash
   sudo journalctl -u idlemonitor-server -f
   ```

### 管理面板无法登录

确认使用的是管理员账号，而不是客户端 API Token。两者用途不同：

- 客户端 API：Bearer Token；
- Web 管理面板：HTTP Basic Authentication。

## 版本发布

Release 页面：

<https://github.com/Felix666-ship-It/IdleMonitor/releases>

当前版本：

<https://github.com/Felix666-ship-It/IdleMonitor/releases/tag/v1.1.0>

## 许可证

请以仓库中的 [`LICENSE`](LICENSE) 文件为准。
