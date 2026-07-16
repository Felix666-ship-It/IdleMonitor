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

## 说明

当前仓库提供 Windows 可执行文件和应用图标。请从本仓库的 Release 页面下载程序，避免使用来源不明的副本。
