import base64
import hmac
import json
import os
import secrets
import sqlite3
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path
from typing import Literal

from fastapi import Depends, FastAPI, Header, HTTPException, Request, status
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.security import HTTPBasic, HTTPBasicCredentials
from pydantic import BaseModel, Field, field_validator

APP_DIR = Path(os.getenv("IDLEMONITOR_APP_DIR", Path(__file__).parent))
DATA_DIR = Path(os.getenv("IDLEMONITOR_DATA_DIR", APP_DIR / "data"))
DATABASE_PATH = DATA_DIR / "idlemonitor.db"
PUBLIC_BASE_URL = os.getenv("IDLEMONITOR_PUBLIC_BASE_URL", "http://127.0.0.1:8080").rstrip("/")
API_TOKEN = os.getenv("IDLEMONITOR_API_TOKEN", "")
ADMIN_USERNAME = os.getenv("IDLEMONITOR_ADMIN_USERNAME", "admin")
ADMIN_PASSWORD = os.getenv("IDLEMONITOR_ADMIN_PASSWORD", "")

app = FastAPI(title="IdleMonitor Server", docs_url=None, redoc_url=None)
security = HTTPBasic(auto_error=False)


class StartupReport(BaseModel):
    deviceId: str = Field(min_length=1, max_length=128)
    localIP: str = Field(min_length=1, max_length=64)
    event: Literal["startup"]


class IdleReport(BaseModel):
    deviceId: str = Field(min_length=1, max_length=128)
    idleSeconds: int = Field(ge=0, le=86400)
    thresholdSeconds: int = Field(ge=1, le=86400)
    event: Literal["idle_exceeded"]


class ServiceConfig(BaseModel):
    idleThreshold: int = Field(ge=1, le=86400)
    configPollInterval: int = Field(ge=60, le=86400)


class AdminConfigUpdate(ServiceConfig):
    pass


def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def db() -> sqlite3.Connection:
    connection = sqlite3.connect(DATABASE_PATH)
    connection.row_factory = sqlite3.Row
    return connection


def initialize() -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    with closing(db()) as connection:
        connection.executescript(
            """
            CREATE TABLE IF NOT EXISTS settings (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS devices (
              device_id TEXT PRIMARY KEY,
              local_ip TEXT,
              first_seen TEXT NOT NULL,
              last_seen TEXT NOT NULL,
              last_event TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS events (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              device_id TEXT NOT NULL,
              event_type TEXT NOT NULL,
              idle_seconds INTEGER,
              threshold_seconds INTEGER,
              local_ip TEXT,
              created_at TEXT NOT NULL
            );
            """
        )
        connection.execute("INSERT OR IGNORE INTO settings(key, value) VALUES (?, ?)", ("idleThreshold", "300"))
        connection.execute("INSERT OR IGNORE INTO settings(key, value) VALUES (?, ?)", ("configPollInterval", "300"))
        connection.commit()


def get_config() -> dict:
    with closing(db()) as connection:
        values = {row["key"]: int(row["value"]) for row in connection.execute("SELECT key, value FROM settings")}
    return {"idleThreshold": values["idleThreshold"], "configPollInterval": values["configPollInterval"]}


def require_client_token(authorization: str | None = Header(default=None)) -> None:
    if not API_TOKEN:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail="客户端 API Token 未配置")
    expected = f"Bearer {API_TOKEN}"
    if authorization is None or not hmac.compare_digest(authorization, expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="客户端认证失败")


def require_admin(credentials: HTTPBasicCredentials | None = Depends(security)) -> None:
    if not ADMIN_PASSWORD:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail="管理员密码未配置")
    valid = credentials and hmac.compare_digest(credentials.username, ADMIN_USERNAME) and hmac.compare_digest(credentials.password, ADMIN_PASSWORD)
    if not valid:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="管理员认证失败", headers={"WWW-Authenticate": "Basic"})


@app.on_event("startup")
def on_startup() -> None:
    initialize()


@app.get("/healthz")
def healthz() -> dict:
    return {"status": "ok", "time": now()}


@app.post("/api/report", dependencies=[Depends(require_client_token)])
def report_startup(payload: StartupReport) -> dict:
    timestamp = now()
    with closing(db()) as connection:
        connection.execute(
            """INSERT INTO devices(device_id, local_ip, first_seen, last_seen, last_event)
               VALUES (?, ?, ?, ?, ?)
               ON CONFLICT(device_id) DO UPDATE SET local_ip=excluded.local_ip, last_seen=excluded.last_seen, last_event=excluded.last_event""",
            (payload.deviceId, payload.localIP, timestamp, timestamp, payload.event),
        )
        connection.execute("INSERT INTO events(device_id, event_type, local_ip, created_at) VALUES (?, ?, ?, ?)", (payload.deviceId, payload.event, payload.localIP, timestamp))
        connection.commit()
    return {"accepted": True}


@app.post("/api/idle-report", dependencies=[Depends(require_client_token)])
def report_idle(payload: IdleReport) -> dict:
    timestamp = now()
    with closing(db()) as connection:
        connection.execute(
            """INSERT INTO devices(device_id, first_seen, last_seen, last_event)
               VALUES (?, ?, ?, ?)
               ON CONFLICT(device_id) DO UPDATE SET last_seen=excluded.last_seen, last_event=excluded.last_event""",
            (payload.deviceId, timestamp, timestamp, payload.event),
        )
        connection.execute(
            "INSERT INTO events(device_id, event_type, idle_seconds, threshold_seconds, created_at) VALUES (?, ?, ?, ?, ?)",
            (payload.deviceId, payload.event, payload.idleSeconds, payload.thresholdSeconds, timestamp),
        )
        connection.commit()
    return {"accepted": True}


@app.get("/api/config", dependencies=[Depends(require_client_token)])
def client_config(deviceId: str) -> dict:
    if not deviceId or len(deviceId) > 128:
        raise HTTPException(status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail="无效的 deviceId")
    return get_config()


@app.get("/admin/api/summary", dependencies=[Depends(require_admin)])
def admin_summary() -> dict:
    with closing(db()) as connection:
        devices = [dict(row) for row in connection.execute("SELECT * FROM devices ORDER BY last_seen DESC")]
        events = [dict(row) for row in connection.execute("SELECT * FROM events ORDER BY id DESC LIMIT 100")]
    return {"baseUrl": PUBLIC_BASE_URL, "config": get_config(), "devices": devices, "events": events}


@app.put("/admin/api/config", dependencies=[Depends(require_admin)])
def update_config(payload: AdminConfigUpdate) -> dict:
    with closing(db()) as connection:
        connection.execute("UPDATE settings SET value=? WHERE key='idleThreshold'", (str(payload.idleThreshold),))
        connection.execute("UPDATE settings SET value=? WHERE key='configPollInterval'", (str(payload.configPollInterval),))
        connection.commit()
    return get_config()


@app.get("/api/info")
def api_info() -> dict:
    return {
        "baseUrl": PUBLIC_BASE_URL,
        "healthUrl": PUBLIC_BASE_URL + "/healthz",
        "panelUrl": PUBLIC_BASE_URL + "/admin",
        "apiDocsUrl": PUBLIC_BASE_URL + "/api-docs",
        "clientEndpoints": {
            "startupReport": {"method": "POST", "url": PUBLIC_BASE_URL + "/api/report"},
            "config": {"method": "GET", "url": PUBLIC_BASE_URL + "/api/config"},
            "idleReport": {"method": "POST", "url": PUBLIC_BASE_URL + "/api/idle-report"},
        },
        "authentication": "Bearer token required for /api/* client endpoints",
    }


@app.get("/", response_class=HTMLResponse)
def public_home() -> str:
    return PUBLIC_HOME_HTML.replace("__BASE_URL__", PUBLIC_BASE_URL)


@app.get("/api-docs", response_class=HTMLResponse)
def public_api_docs() -> str:
    return API_DOCS_HTML.replace("__BASE_URL__", PUBLIC_BASE_URL)


@app.get("/admin", response_class=HTMLResponse, dependencies=[Depends(require_admin)])
def admin_page() -> str:
    return ADMIN_HTML


PUBLIC_HOME_HTML = """<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>IdleMonitor 服务端</title><style>body{margin:0;background:#f6f7fb;color:#172033;font:15px/1.5 system-ui,-apple-system,Segoe UI,sans-serif}.wrap{max-width:980px;margin:auto;padding:48px 24px}h1{margin:0;font-size:30px}h2{font-size:18px;margin-top:0}.muted{color:#64748b}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:16px;margin:26px 0}.card{background:#fff;border:1px solid #dbe3ef;border-radius:8px;padding:20px}.endpoint{border-left:3px solid #2563eb;padding:10px 12px;background:#f8fafc;margin:10px 0}.endpoint code{display:block;overflow-wrap:anywhere;color:#0f3b80;margin-top:5px}a.button{display:inline-block;background:#2563eb;color:white;text-decoration:none;border-radius:5px;padding:9px 13px;margin-right:8px}.note{background:#fffbeb;border-left:4px solid #eab308;padding:12px}</style></head><body><main class='wrap'><h1>IdleMonitor 服务端</h1><p class='muted'>客户端接入地址、API 文档和设备管理入口。</p><div class='grid'><section class='card'><h2>客户端对接 URL</h2><div class='endpoint'><b>启动上报 · POST</b><code>__BASE_URL__/api/report</code></div><div class='endpoint'><b>配置读取 · GET</b><code>__BASE_URL__/api/config?deviceId=&lt;device-id&gt;</code></div><div class='endpoint'><b>空闲上报 · POST</b><code>__BASE_URL__/api/idle-report</code></div></section><section class='card'><h2>入口</h2><p>管理面板包含设备列表、事件记录和阈值配置。</p><a class='button' href='/admin'>进入管理面板</a><a class='button' href='/api-docs'>查看 API 文档</a><p class='muted'>管理面板会要求管理员账号和密码。</p></section></div><section class='note'><b>客户端认证：</b>三个 <code>/api/*</code> 接口都必须在请求头中提供 <code>Authorization: Bearer &lt;API Token&gt;</code>。Token 不会显示在网页中，请从服务器的受限环境配置中取得后填入 Windows 客户端设置。</section><p class='muted'>服务状态：<a href='/healthz'>/healthz</a> · 机器可读接入信息：<a href='/api/info'>/api/info</a></p></main></body></html>"""


API_DOCS_HTML = """<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>IdleMonitor API 文档</title><style>body{margin:0;background:#f6f7fb;color:#172033;font:14px/1.55 system-ui,-apple-system,Segoe UI,sans-serif}.wrap{max-width:1000px;margin:auto;padding:40px 24px}h1{font-size:28px}h2{font-size:18px;margin-top:28px}table{width:100%;border-collapse:collapse;background:#fff}th,td{padding:12px;border:1px solid #dbe3ef;text-align:left;vertical-align:top}th{background:#eff6ff}code,pre{font-family:ui-monospace,SFMono-Regular,Consolas,monospace}pre{white-space:pre-wrap;background:#0f172a;color:#e2e8f0;padding:14px;border-radius:6px}a{color:#2563eb}.note{padding:12px;background:#fffbeb;border-left:4px solid #eab308}</style></head><body><main class='wrap'><p><a href='/'>返回首页</a></p><h1>IdleMonitor 客户端 API</h1><p>公共基址：<code>__BASE_URL__</code></p><div class='note'>所有客户端接口都要求 <code>Authorization: Bearer &lt;API Token&gt;</code>。缺失或错误时返回 <code>401</code>。</div><h2>接口总览</h2><table><thead><tr><th>用途</th><th>方法</th><th>URL</th><th>响应</th></tr></thead><tbody><tr><td>启动上报</td><td>POST</td><td><code>__BASE_URL__/api/report</code></td><td><code>{&quot;accepted&quot;:true}</code></td></tr><tr><td>读取配置</td><td>GET</td><td><code>__BASE_URL__/api/config?deviceId=&lt;id&gt;</code></td><td><code>{&quot;idleThreshold&quot;:420,&quot;configPollInterval&quot;:120}</code></td></tr><tr><td>空闲上报</td><td>POST</td><td><code>__BASE_URL__/api/idle-report</code></td><td><code>{&quot;accepted&quot;:true}</code></td></tr></tbody></table><h2>启动上报请求体</h2><pre>{
  &quot;deviceId&quot;: &quot;uuid-or-device-id&quot;,
  &quot;localIP&quot;: &quot;192.168.1.20&quot;,
  &quot;event&quot;: &quot;startup&quot;
}</pre><h2>空闲上报请求体</h2><pre>{
  &quot;deviceId&quot;: &quot;uuid-or-device-id&quot;,
  &quot;idleSeconds&quot;: 420,
  &quot;thresholdSeconds&quot;: 420,
  &quot;event&quot;: &quot;idle_exceeded&quot;
}</pre><h2>管理接口</h2><p><code>/admin</code>、<code>/admin/api/summary</code> 和 <code>/admin/api/config</code> 仅供管理员使用，采用 HTTP Basic Authentication，不应嵌入到 Windows 客户端。</p></main></body></html>"""


ADMIN_HTML = """<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>IdleMonitor 控制台</title><style>
body{margin:0;background:#f6f7fb;color:#1f2937;font:14px/1.5 system-ui,-apple-system,Segoe UI,sans-serif}.wrap{max-width:1200px;margin:0 auto;padding:32px}h1{margin:0 0 4px;font-size:24px}h2{font-size:16px;margin:0 0 14px}.muted{color:#64748b}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:16px;margin:22px 0}.panel{background:#fff;border:1px solid #dde3ee;border-radius:8px;padding:18px;box-shadow:0 1px 2px #0f172a0d}.url{display:flex;gap:8px;align-items:center;margin:8px 0}.url code{background:#f1f5f9;overflow-wrap:anywhere;padding:6px;flex:1;border-radius:4px}button{border:0;background:#2563eb;color:#fff;padding:7px 11px;border-radius:4px;cursor:pointer}input{box-sizing:border-box;width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:4px;margin:5px 0 12px}table{width:100%;border-collapse:collapse;min-width:680px}th,td{text-align:left;padding:9px;border-bottom:1px solid #e5e7eb;overflow-wrap:anywhere}th{color:#475569;font-weight:600}.table{overflow:auto}.notice{border-left:4px solid #eab308;background:#fefce8;padding:10px 12px}.ok{color:#15803d}</style></head><body><main class='wrap'>
<h1>IdleMonitor 控制台</h1><p class='muted'>设备状态、事件与客户端对接地址</p><div id='notice' class='notice'>正在读取服务状态。</div>
<section class='grid'><div class='panel'><h2>客户端对接 URL</h2><div id='urls'></div></div><div class='panel'><h2>监控配置</h2><label>空闲阈值（秒）</label><input id='idleThreshold' type='number' min='1' max='86400'><label>轮询间隔（秒）</label><input id='configPollInterval' type='number' min='60' max='86400'><button onclick='saveConfig()'>保存配置</button><span id='saved' class='ok'></span></div></section>
<section class='panel'><h2>设备</h2><div class='table'><table><thead><tr><th>设备 ID</th><th>局域网 IP</th><th>首次发现</th><th>最后在线</th><th>最后事件</th></tr></thead><tbody id='devices'></tbody></table></div></section>
<section class='panel' style='margin-top:16px'><h2>最近事件</h2><div class='table'><table><thead><tr><th>时间</th><th>设备 ID</th><th>事件</th><th>空闲秒数</th><th>阈值</th><th>局域网 IP</th></tr></thead><tbody id='events'></tbody></table></div></section></main>
<script>const q=s=>document.querySelector(s);const esc=v=>String(v??'').replace(/[&<>\"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'}[c]));function copy(v){navigator.clipboard.writeText(v)}function rows(id,items,fields){q(id).innerHTML=items.map(x=>'<tr>'+fields.map(f=>'<td>'+esc(x[f])+'</td>').join('')+'</tr>').join('')||'<tr><td colspan="6" class="muted">暂无数据</td></tr>'}async function load(){const r=await fetch('/admin/api/summary');if(!r.ok){q('#notice').textContent='加载失败，请检查管理员认证。';return}const d=await r.json();q('#notice').innerHTML=d.baseUrl.startsWith('https://')?'<span class="ok">HTTPS 地址已配置。</span>':'当前为本机测试地址；生产客户端需要配置可信 HTTPS 域名。';const routes=[['启动上报',d.baseUrl+'/api/report'],['配置读取',d.baseUrl+'/api/config'],['空闲上报',d.baseUrl+'/api/idle-report']];q('#urls').innerHTML=routes.map(([n,u])=>'<div><b>'+n+'</b><div class="url"><code>'+esc(u)+'</code><button onclick="copy(\''+u+'\')">复制</button></div></div>').join('');q('#idleThreshold').value=d.config.idleThreshold;q('#configPollInterval').value=d.config.configPollInterval;rows('#devices',d.devices,['device_id','local_ip','first_seen','last_seen','last_event']);rows('#events',d.events,['created_at','device_id','event_type','idle_seconds','threshold_seconds','local_ip'])}async function saveConfig(){const payload={idleThreshold:+q('#idleThreshold').value,configPollInterval:+q('#configPollInterval').value};const r=await fetch('/admin/api/config',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});q('#saved').textContent=r.ok?'已保存':'保存失败';if(r.ok)load()}load()</script></body></html>"""
