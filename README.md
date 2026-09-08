# IdleMonitor

IdleMonitor 是一个轻量级 Windows 空闲状态监控工具，以单文件可执行程序形式发布，无需安装。

## 下载

前往 [Releases](https://github.com/Felix666-ship-It/IdleMonitor/releases/latest) 下载最新版本的 `IdleMonitor.exe`。

当前版本：[v1.0.0](https://github.com/Felix666-ship-It/IdleMonitor/releases/tag/v1.0.0)

## 使用方法

1. 下载 `IdleMonitor.exe`。
2. 双击运行程序。
3. 如 Windows SmartScreen 显示提示，请先确认文件来源为本仓库的 Release 页面，再决定是否运行。

程序为便携版，可直接放在任意目录运行。删除可执行文件即可移除程序。

## 系统要求

- Windows 10 或 Windows 11
- x64 系统

## 文件校验

v1.0.0 的 SHA-256：

```text
IdleMonitor.exe  c60a3de7a5ba1e366ab8a68cedefae3018d30dca9aeaf878450a42f992fe77d5
app.ico         ac8f327b53e93269f73c4670e5a4fba95881dd946e52c19aead33280709ca852
```

可在 PowerShell 中校验下载文件：

```powershell
Get-FileHash .\IdleMonitor.exe -Algorithm SHA256
```

## 构建

需要 Windows、Visual Studio 2022（含 .NET Framework 4.8 Developer Pack）或兼容 MSBuild：

```powershell
msbuild .\src\IdleMonitor\IdleMonitor.csproj /p:Configuration=Release
```

## 安全与隐私

- 远程 API 地址必须使用 HTTPS；仅允许 localhost 使用 HTTP 进行本地开发。
- 默认不查询、不上报公网 IP，仅发送设备 ID、局域网 IPv4 和事件数据。
- 托盘菜单可暂停/恢复数据采集；暂停后不会发送启动或空闲事件。
- API 地址会在保存前进行格式和协议校验，请勿配置不受信任的服务器。
- 程序会尝试设置当前用户开机启动；可在注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 删除 `IdleMonitor` 项以关闭。
- 日志位于 `%LOCALAPPDATA%\IdleMonitor\idlemonitor.log`，不记录完整请求体或凭据。

> 生产部署仍应由服务端增加认证、请求签名、设备注册和访问控制。
