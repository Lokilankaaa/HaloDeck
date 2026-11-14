# HandheldOpti

**专为 GPD Win 5 (AMD Ryzen AI Max+ 395 / 385, Radeon 8060S / 8050S RDNA 3.5) 及 Windows 掌机打造的现代化自动 OptiScaler 注入与管理软件。**

HandheldOpti 融合了 **ClawTweaks 式「当前运行游戏」即时注入重启** 与 **OptiScaler Client 式多平台自动游戏库管理**，原生支持 **XInput 手柄导航**、**右侧滑出快捷菜单 (QAM)**、**无键盘 `Insert` 键扫描码直通注入** 以及 **事务级 SHA256 原文件备份还原**。

---

## 核心特性

### 1. 双模智能自动注入引擎
- **正在运行游戏实时锁定 + 一键注入并重启**：参考 ClawTweaks「Running Game」交互理念，后台实时追踪当前前台运行游戏，精准锁定渲染二进制目录，点击 **「一键注入并重启游戏」** 即可自动优雅关闭进程 -> 写入代理 DLL 与优化配置 -> 通过 Steam URI 或原路径无缝重启游戏。
- **全平台游戏库自动扫描 + 可选后台自动预注入**：
  - 自动发现 **Steam** (`libraryfolders.vdf` / `appmanifest_*.acf`)、**Epic Games**、**GOG**、**Xbox PC** 及手动导入游戏。
  - 智能定位 **Unreal Engine 4/5 (`*\Binaries\Win64\*-Shipping.exe`)**、**REDengine (`bin\x64`)**、**Unity** 等真实渲染目录，识别原生 DLSS / XeSS / FSR / Streamline DLL 与 64 位 PE 头。
  - **多代理 DLL 冲突自动避让**：`auto` 模式下若检测到 ReShade / Special K 等第三方 `dxgi.dll`，自动顺延选用 `winmm.dll` / `version.dll` / `dbghelp.dll` / `winhttp.dll`。
  - **事务级备份与一键还原**：完整记录新增文件与原文件 SHA256 清单至 `.handheldopti_backup/manifest.json`，一键无损卸载并恢复纯净目录。
- **反作弊安全熔断**：自动识别 EAC、BattlEye、Vanguard、Tencent ACE、Ricochet 等反作弊组件，后台自动注入模式硬性跳过，手动操作弹出明确风险确认。

### 2. GPD Win 5 / AMD Strix Halo (RDNA 3.5) 专属优化
- **GPD Win 5 无物理键盘 `Insert` 键痛点解决**：
  - 手柄组合键 `Select + Start` 或全局热键 `Ctrl + Alt + I` 向游戏发送带扩展键标志（`KEYEVENTF_EXTENDEDKEY`）与 65ms 脉冲保持的 `VK_INSERT (0x2D)` 扫描码，稳定呼出游戏内 OptiScaler ImGui 菜单。
  - 推荐在 **GPD WinControls** 中将掌机物理背键映射为：
    - **L4** → `Ctrl + Alt + O`（呼出/收起右侧 QAM 快捷菜单）
    - **R4** → `Ctrl + Alt + I`（向游戏注入 `Insert` 呼出 OptiScaler 菜单）
- **4 套 RDNA 3.5 专属预设**：均衡画质帧生 (`OptiPatcher + Fakenvapi + OptiFG`)、XeSS 稳定抗闪烁、低延迟纯超分 (`nofg`)、强力显卡伪装兼容 (`RTX 4090`)。
- **完整周边组件生态中心**：支持在线拉取与 `.7z` 固实包 / `.zip` 离线导入 **OptiScaler (Stable & Nightly)**、**OptiPatcher (`plugins/OptiPatcher.asi`)**、**Fakenvapi (`Reflex -> AMD Anti-Lag 2`)** 以及自定义扩展 DLL（如 FSR 4 / `dlssg-to-fsr3`）。

### 3. 双形态现代化掌机 UI
- **右侧滑出快捷菜单 (QAM Overlay)**：`Select + RB` 或 `Ctrl + Alt + O` 随时在游戏上方滑出（440px 宽），关闭后自动将焦点归还游戏窗口。
- **全屏/大屏控制台**：支持 2D 空间 XInput 手柄焦点导航（`LB/RB` 切换模块、十字键/左摇杆移动、`A` 确认、`X` 一键注入、`Y` 注入并重启、`B` 返回）及 7 英寸 1080p 触控屏直接点按。

---

## 快速编译与运行

### 环境要求
- Windows 10 / 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Microsoft Edge WebView2 Runtime（Windows 11 已自带）

### 本地构建与测试
```powershell
dotnet restore HandheldOpti.sln
dotnet build HandheldOpti.sln -c Release
dotnet test HandheldOpti.sln -c Release --no-build
dotnet run --project src/HandheldOpti.App -c Release
```

### 命令行参数
- `--minimized`：静默启动到系统托盘
- `--qam`：启动并直接展开右侧 QAM 快捷菜单
- `--verify-startup`：执行启动自检（验证 WebView2 初始化、游戏库扫描与输入总线状态）并退出

---

## 目录结构
- `src/HandheldOpti.Core`：多平台扫描、UE5/PE/反作弊分析、INI 无损编辑、`.7z`/`.zip` 解压与组件管理、事务注入与进程追踪引擎
- `src/HandheldOpti.App`：WPF + WebView2 双窗口宿主、Win32 热键与 `SendInput`、XInput 轮询器与现代化掌机前端
- `tests/HandheldOpti.Tests`：覆盖 VDF 解析、UE5 定位、反作弊拦截、冲突避让注入与 SHA256 还原闭环的 xUnit 测试套件
