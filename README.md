# 🍎 Port & Ping Tool

> Apple-style network tool for Windows 10/11 — TCP port listener, remote port test, and high-frequency ICMP ping.

Built for network operators who need to test whether a port on their public IP is reachable, and who want a clean, fast, no-bloat utility instead of juggling `nc`, `telnet`, and `ping -t` in three different terminals.

![banner](https://img.shields.io/badge/platform-Windows%2010%2F11-blue?style=flat-square)
![tech](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)
![ui](https://img.shields.io/badge/UI-WPF-0078D4?style=flat-square)
![license](https://img.shields.io/badge/license-MIT-green?style=flat-square)

---

## ✨ Features

### 左侧 · 端口

- **本机端口监听** — 自定义端口、1 个或多个并存
  - 每条连接记录来访 IP + 端口 + 精确到毫秒的时间戳
  - 状态指示灯:绿(运行中) / 灰(已停止)
- **远端端口测试** — 输入 `IP:Port`,秒级返回 OPEN/CLOSED + 延迟 + 失败原因
  - DNS 解析失败、连接超时、目标拒绝——错误信息可读

### 右侧 · Ping

- **频率可切**:1000 ms(标准)/ 100 ms(快)
- **次数可配**:持续(像 `ping -t`)/ 自定义次数
- **统计**:
  - 已发送 / 已接收 / 丢包率
  - 平均延迟 (ms)
- **结果实时滚动** — 失败行用红色高亮
- **底层 ICMP**——需要管理员权限监听不到系统级 ICMP 拒绝时,工具会直接显示拒绝原因

### 设计

- macOS Sonoma / iOS 17 视觉语言:圆角、留白、SF 字体栈
- 深色友好的卡片层级
- 完全单文件 EXE,无需安装 .NET 运行时

---

## 📦 下载

前往 [Releases](https://github.com/byby5555/port-ping-tool/releases) 页面下载对应架构的 EXE:

| 文件 | 适用 |
|---|---|
| `PortPingTool.exe` (win-x64) | 大多数 Windows 10/11 用户 |
| `PortPingTool.exe` (win-x86) | 32 位系统(罕见) |
| `PortPingTool.exe` (win-arm64) | Surface Pro X / Snapdragon 笔电 |

**SmartScreen 提示**:首次运行会弹"未识别的应用",点 **更多信息 → 仍要运行** 即可。后续可考虑加代码签名证书。

---

## 🚀 使用

1. **监听本机端口**
   - 左侧"本机端口监听"输入端口号(如 `8080`),点 **+ 添加**
   - 点 **启动** —— 状态灯变绿,开始接收连接
   - 公网访问你机器的 `IP:8080`,下面"连接记录"会显示来访 IP

2. **测试远端端口**
   - "远端端口测试"卡片输入目标 IP + 端口
   - 点 **测试** —— 立刻返回 OPEN/CLOSED 和延迟

3. **Ping**
   - 右侧输入目标(域名或 IP)
   - 选 1000ms / 100ms
   - 选持续 / 自定义次数
   - 点 **开始** —— 实时结果 + 顶部统计

---

## 🛠️ 本地构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
git clone https://github.com/byby5555/port-ping-tool.git
cd port-ping-tool
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

产物在 `bin/Release/net8.0-windows/win-x64/publish/PortPingTool.exe`。

---

## 🏗️ 技术栈

| 层 | 选型 | 理由 |
|---|---|---|
| UI | **WPF** | Apple 风好做、矢量渲染、控件丰富 |
| Runtime | **.NET 8** | 现代 C#、AOT 支持、性能好 |
| 发布 | **Single-file + Self-contained** | 双击即用 |
| 构建 | **GitHub Actions (windows-latest)** | 自动化 release |

> 体积:目前单文件 ~30-40 MB(未启用 NativeAOT)。后续会切 NativeAOT 压到 ~15 MB。

---

## 🗺️ Roadmap

- [ ] NativeAOT 切换(目标 ~15 MB EXE)
- [ ] 多目标批量 ping / 批量端口测试
- [ ] 导出结果 CSV
- [ ] IPv6 支持
- [ ] 代码签名证书(消除 SmartScreen 警告)
- [ ] i18n(英文 UI)

---

## 🤝 贡献

PR / Issue 都欢迎。

提交前:

```bash
dotnet build
dotnet run     # smoke test
```

---

## 📝 License

MIT © 2026 [byby5555](https://github.com/byby5555)
