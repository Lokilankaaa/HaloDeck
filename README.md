# HaloDeck

**专为 GPD Win 5 (AMD Ryzen AI Max+ 388/395/385, Radeon 8060S / 8050S RDNA 3.5) 及 Windows 掌机打造的以掌机中控为核心的全功能前端（前身为 HandheldOpti，现版本 v0.3.0）。**

HaloDeck 以「掌机中控优先，超分插帧为专业子系统」的现代双形态架构（主控制台 + `440px` 右侧滑出 QAM 快捷侧栏）取代臃肿厂商工具与命令行调参，将以下四大核心模块整合为一体：

---

## 核心功能架构

### 1. 掌机总控（默认首页 `handheld`）
- **实时整机电源流**：借助 AMD ADL2 (`atiadlxx.dll`) 与 Windows 电池状态接口实时显示 **APU 封装功耗 (W)**、**电池充放电功率 (±W · 剩余容量 Wh · 电压 · 估算续航/充满时间)** 以及 **充电器输入功率估算 / AC 旁路直供状态**。
- **12W–85W Strix Halo TDP 总控与电源联动**：提供 **静音 / 均衡 / 狂暴** 三档可自定义功率预设、1W 精细步进滑动条以及 **插拔电自动切换 TDP**（分别设定离电/插电目标功耗并在 AC 变化时自动下发）。
- **双风扇智能曲线温控**：支持 `EC 自动 / 静音 / 均衡 / 狂暴 / 手动固定%`，实时读取风扇转速 (RPM) 与 PWM 占空比；内建 `≥ 82°C` 热安全保护底线，服务停止/15 秒曲线看门狗超时/程序退出时自动还原 EC 默认温控。
- **可选一次性免 UAC 底层硬件服务 (`HaloDeckHardwareSvc`)**：基于开源签名驱动 **PawnIO** (`RyzenSMU.bin` & `LpcIO.bin`，带 GPD DMI 主板白名单校验与 ISA Mutex 同步)。日常显示/音频/监控/工具箱/超分全功能均可在普通用户态无弹窗运行；仅当需要启用 TDP 与风扇直控时点击一次授权注册。
- **显示 · 音频 · CPU 调度与工具箱**：
  - 一键切换刷新率（`48 / 60 / 100 / 120 Hz`）、分辨率、Win11 窗口化 VRR、内置屏亮度、CoreAudio 音量与静音、`PowrProf` CPU 睿频策略及 **CPU 最大频率上限 (MHz)**。
  - **掌机快捷工具箱**：`游戏冰箱`（`NtSuspendProcess`/`NtResumeProcess` 挂起当前游戏省电并在应用退出时自动唤醒防丢档）、`手柄鼠标`、`返回桌面`、`切换窗口`、`屏幕键盘`、`区域截屏`、`任务管理器` 及 **二次确认防误触 `强关游戏`**。
  - **分游戏独立性能档案**：按游戏保存专属 TDP 与刷新率配置。

### 2. 性能监控与无钩子穿透 OSD（`monitor`）
- **5 档 Steam Deck 风格穿透式性能悬浮窗 (`0–4`)**：纯原生 WPF `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` 透明置顶窗口，不 Hook 游戏 DirectX 管线（零反作弊风险、不抢游戏焦点、鼠标点击完全穿透），支持停靠位置与透明度调节及实时样式预览。
- **6 项硬件 KPI 与 60 秒原生 SVG 趋势曲线**：实时采样 APU 功耗、GPU/CPU 利用率与频率、核心温度、VRAM/RAM 及电源状态，并绘制最近 60 秒双曲线趋势图。

### 3. 按键与手柄中心（`controls`）
- **GPD 手柄硬件 V2 (`VID_2F24&PID_0137`) 直通 Flash 读写**：直接通过 Win32 HID 读写 **L4 / R4 背键映射**（推荐 `F13` 呼出 QAM 侧栏、`F14` 切换 OSD、`Insert`/`F15` 呼出插件菜单，同时兼容保留出厂多步组合宏）、马达震动强度 (`0/1/2`) 与左右摇杆死区 (`-10..10`)。
- **XInput 手柄桌面鼠标模式**：左摇杆控光标、右摇杆滚轮、`A` 左键、`B` 右键、`X` 中键、`LB/RB` 加减速。
- **全局后台手柄组合键与键盘热键**：独立配置侧栏、OSD、手柄鼠标与 `Insert` 菜单热键，并支持开机自启、默认全屏启动与后台静默自动注入设置。

### 4. OptiScaler 超分插帧子系统（`opti`：`调优与注入` / `游戏库` / `组件与扩展`）
- 支持 **FSR 4.1.2 INT8 / Lite**、**XeSS 2.0**、**FSR 3.1**，以及 **XeFG**（原生 DLSS-G 游戏可随游戏开关热启停）与 **OptiFG** 帧生成。
- 集成 **Fakenvapi (`Reflex -> AMD Anti-Lag 2`)**、**OptiPatcher**、10GB DXGI 伪装显存防爆优化、翻页防撕裂 V-Sync 控制、全平台游戏库海报墙、GitHub 组件在线/离线管理，以及 `.handheldopti_backup/manifest.json` 事务级备份还原。
- 内建 **EAC / BattlEye / Vanguard / ACE / Ricochet** 反作弊安全拦截。
- **QAM 超分切游戏实时同步**：在 `440px` 右侧栏中直接内嵌游戏切换胶囊与 `#qamStatusBanner` 实时状态横幅，切换游戏即刻反映其真实注入与配置状态。

---

## 环境要求与快速部署

### 环境要求
- Windows 10 / 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（编译与打包需要；发布后的自包含包可直接运行）
- Microsoft Edge WebView2 Runtime（Windows 11 已内置）
- **可选（仅直控 TDP 与双风扇需要）**：[PawnIO](https://pawnio.eu/) 签名驱动

### 一键本地安装
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-local.ps1
```
安装脚本会将自包含程序部署到 `%LOCALAPPDATA%\Programs\HaloDeck`（保留既有 `%LOCALAPPDATA%\HandheldOpti` 缓存、配置与游戏备份），并生成桌/开始菜单快捷方式及 `uninstall.ps1`。

### 编译、测试与 GUI 自检
```powershell
dotnet build HandheldOpti.sln -c Release
dotnet test HandheldOpti.sln -c Release --no-build
dotnet run --project src/HandheldOpti.App -c Release
```

### 命令行参数
- `--minimized`：静默启动到系统托盘
- `--qam`：启动并直接展开右侧 QAM 快捷菜单
- `--verify-startup`：执行基础启动自检并退出
- `--verify-gui [输出目录]`：执行完整端到端 GUI 自检（验证掌机首页、性能监控与穿透 OSD 窗口样式、手柄与控制分页、OptiScaler 三子标签、弹窗、全屏切换及 440px QAM 四分页并输出报告与 PNG 截图）
- `--install-hw-service` / `--uninstall-hw-service`：注册或卸载可选底层硬件服务 `HaloDeckHardwareSvc`

---

## 目录结构
- `src/HandheldOpti.Core`：核心模型（`AppSettings`、`GpdV2PacketCodec`）、多平台游戏扫描、UE5/PE/反作弊识别、INI 编辑、组件管理与注入/还原引擎
- `src/HandheldOpti.App`：WPF + WebView2 双窗口宿主、穿透式 `OverlayHudWindow`、`PawnIoHardwareDaemon`、GPD V2 HID 服务、工具箱服务与 `HaloDeck` 前端界面
- `tests/HandheldOpti.Tests`：覆盖 INI/预设、扫描、注入还原以及 v0.3.0 设置兼容与 GPD V2 报文编解码的 xUnit 单元测试
