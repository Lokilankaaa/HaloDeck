# HaloDeck 社区插件开发指南（Decky 风格 · Plugin SDK v1）

HaloDeck 从 **v0.6.0** 起提供类似 **Steam Deck Decky Loader** 的社区插件框架。
插件运行在 **WebView2 沙箱 `iframe` + 进程级隔离的 `stdio JSON-RPC 2.0` 独立后端** 中：
- 无需修改 HaloDeck 主程序源码，也不受编程语言限制（前端纯 HTML/JS/CSS，后端支持 `.exe` / `.ps1` / `.cmd` 等任意可执行程序）；
- 插件崩溃仅影响自身子进程，绝不干扰主控台的 TDP / 风扇 / OSD / 手柄导航；
- 可同时挂载到 **主界面插件中心（🔌）** 与 **QAM 快捷侧栏（🔌 第 5 页）**。

---

## 1. 目录结构与安装方式

每个插件是一个以 `id` 命名的文件夹（或打包为 `.zip` 后通过 HaloDeck 顶部 🔌 → **「导入 .zip 插件」** 一键安装）：

```text
%LOCALAPPDATA%\HaloDeck\plugins\<plugin-id>\
├── plugin.json         # 必需：插件清单
├── icon.svg            # 可选：图标（SVG / PNG）
├── ui\
│   └── index.html      # 必需：前端入口（支持 qam & console 响应式）
└── backend\            # 可选：独立后端进程
    └── worker.exe      # 或 worker.ps1 / worker.cmd
```

插件私有数据目录自动分配在 `%LOCALAPPDATA%\HaloDeck\plugin_data\<plugin-id>\`（卸载插件默认保留用户数据）。

---

## 2. `plugin.json` 清单规范

```json
{
  "schemaVersion": 1,
  "id": "author.my-plugin",
  "name": "我的掌机插件",
  "version": "1.0.0",
  "author": "yourname",
  "description": "一句话介绍插件用途",
  "homepage": "https://github.com/yourname/halodeck-my-plugin",
  "minHaloDeckVersion": "0.6.0",
  "icon": "icon.svg",
  "mount": ["console", "qam"],
  "activation": ["onView"],
  "entry": "ui/index.html",
  "backend": {
    "type": "stdio-jsonrpc",
    "executable": "backend/worker.exe",
    "args": [],
    "idleTimeoutSeconds": 300
  },
  "permissions": [
    "games:read",
    "events:game",
    "storage",
    "backend"
  ]
}
```

### 字段说明
- `id`：必填，3–64 位小写字母/数字/`. _ -`（正则 `^[a-z0-9][a-z0-9._-]{2,63}$`，禁止 `..`）。**目录名必须与 `id` 一致**。
- `mount`：支持 `"console"`（主界面大画布）、`"qam"`（游戏内快捷侧栏抽屉），可同时声明。
- `activation`：
  - `"onView"`（默认）：用户打开插件界面并调用后端时懒启动；
  - `"onGameLaunch"`：检测到游戏启动时自动唤醒后端并推送 `gameStarted` 通知（需同时声明 `events:game` 与 `backend`）。
- `backend`：可选；纯前端插件可直接省略。若配置了 `backend.executable`，必须在 `permissions` 中声明 `"backend"`。

---

## 3. 分级权限白名单（`permissions`）

| 权限 | 说明 | 风险等级 |
|---|---|---|
| `games:read` | 读取已扫描全平台游戏库（含 `installDirectory`、`targetRenderDirectory`、`engine`、`antiCheat.hasAntiCheat`、`isCurrentlyRunning`、`runningPid`） | Tier 1 |
| `telemetry:read` | 读取只读硬件遥测（FPS、APU 功耗、GPU/CPU 温度与负载、显存/内存、风扇、电量、分辨率与刷新率） | Tier 1 |
| `events:game` | 接收 `gameStarted` / `gameStopped` 游戏生命周期事件 | Tier 1 |
| `storage` | 读写本插件隔离 KV 存储（`plugin_data/<id>/kv.json`） | Tier 1 |
| `shell:openFolder` | 在资源管理器中打开本插件专属数据目录 | Tier 1 |
| `shell:openUrl` | 在系统默认浏览器中打开安全的 `https://` 外部网页（如攻略或项目主页） | Tier 1 |
| `ui:theme` | 安全覆写 HaloDeck 白名单主题 CSS 变量（实现 Decky CSS Loader 风格皮肤插件，停用/卸载自动还原） | Tier 1 |
| `backend` | 启动并调用插件自带的独立 `stdio JSON-RPC` 后端子进程 | Tier 2 |
| `input:sendKey` | 向当前前台游戏发送白名单功能键（`Insert` `0x2D`、`Home` `0x24`、`End` `0x23`、`F1–F12` `0x70–0x7B`） | Tier 3 |

> 为保障硬件与系统安全，插件 API **不开放** 原始 PawnIO / EC 寄存器写权限、系统设置篡改或静默提权。

---

## 4. 前端 SDK（`HaloDeckPlugin`）

在你的 `ui/index.html` 中引入一行宿主提供的官方 SDK：

```html
<script src="https://app.halodeck.local/plugin-sdk.js"></script>
```

SDK 会自动同步 HaloDeck 的 **极光深色（`aurora`） / 凝霜浅色（`frost`）** 主题 CSS 变量到 `:root`（`--bg-app`、`--bg-card`、`--bg-well`、`--text-primary`、`--text-secondary`、`--text-muted`、`--border`、`--accent`、`--accent-soft`、`--success`、`--warn`、`--danger`）。

### 常用 API
```js
const ctx = await HaloDeckPlugin.ready();
// ctx: { pluginId, pluginName, pluginVersion, permissions, theme, layout, haloDeckVersion, isElevated }

// 读取游戏库与当前运行游戏（需 games:read）
const games = await HaloDeckPlugin.getGames();
const running = await HaloDeckPlugin.getRunningGame();
// 注意反作弊字段位于嵌套对象：game.antiCheat.hasAntiCheat / game.antiCheat.riskLevel

// 读取实时遥测（需 telemetry:read）
const tel = await HaloDeckPlugin.getTelemetry();

// 私有 KV 持久化（需 storage）
await HaloDeckPlugin.storage.set("notes", { "steam:1245620": "先打恶兆" });
const notes = await HaloDeckPlugin.storage.get("notes");

// 调用插件独立后端（需 backend）
const res = await HaloDeckPlugin.backend.call("ping", { hello: "world" });

// 弹出 HaloDeck 原生 Toast
HaloDeckPlugin.toast("操作完成", "success");

// 在默认浏览器打开安全 https:// 链接（需 shell:openUrl）
await HaloDeckPlugin.shell.openUrl("https://www.pcgamingwiki.com/");

// 应用/重置皮肤变量（需 ui:theme，仅限白名单 CSS 变量）
await HaloDeckPlugin.theme.applyVariables({ "--accent": "#ec4899", "--bg-app": "#090714" });
await HaloDeckPlugin.theme.reset();

// 监听事件（含 gameStarted / gameStopped 以及后端推送的自定义进度事件）
HaloDeckPlugin.onEvent((eventName, data) => {
  console.log(eventName, data);
});

// 监听掌机手柄方向键与 A 键（按 B 键会自动返回插件列表）
HaloDeckPlugin.onGamepad((action) => {
  // action: "UP" | "DOWN" | "LEFT" | "RIGHT" | "A"
});
```

---

## 5. 独立后端协议（`stdio JSON-RPC 2.0`）

HaloDeck 启动后端子进程时会注入以下环境变量：
- `HALODECK_PLUGIN_ID`：插件 ID
- `HALODECK_PLUGIN_DIR`：插件安装目录
- `HALODECK_PLUGIN_DATA_DIR`：插件专属数据目录（持久化文件请统一写在此目录）
- `HALODECK_PLUGIN_API`：`1`

通信采用 **UTF-8 单行 JSON**（逐行读写 `stdin` / `stdout`，调试日志输出到 `stderr` 可在 HaloDeck 插件「日志」弹窗直接查看）：

### 请求（HaloDeck → `stdin`）
```json
{"jsonrpc":"2.0","id":"p_1","method":"search","params":{"query":"Elden Ring"},"context":{"pluginId":"author.demo","pluginDataDir":"C:\\...\\plugin_data\\author.demo","runningGame":null}}
```

### 响应（`stdout` → HaloDeck）
```json
{"jsonrpc":"2.0","id":"p_1","result":{"ok":true,"items":[]}}
```

出错时返回：
```json
{"jsonrpc":"2.0","id":"p_1","error":{"code":-32000,"message":"具体错误原因"}}
```

### 主动推送进度/流式事件（`stdout` → HaloDeck → 插件前端 `onEvent`）
```json
{"jsonrpc":"2.0","method":"event","params":{"name":"progress","data":{"percent":60}}}
```

---

## 6. 官方示例模板

仓库 `examples/plugins/` 内置了三套可直接打包 `.zip` 测试的开箱示例：
1. **`examples/plugins/halodeck.skin-studio`** — 视觉主题工坊（Decky CSS Loader 风格，通过 `ui:theme` 权限提供 6 套掌机主题、3 种圆角风格与自定义主强调色调色板，停用插件自动还原原生皮肤）。
2. **`examples/plugins/halodeck.game-notes`** — 掌机游玩计时与游戏备忘（纯前端零后端，自动追踪每款游戏的累计游玩时长、启动次数与最近游玩时间，支持自定义下拉选择游戏、快捷备忘模板与双主题自适应）。
3. **`examples/plugins/halodeck.backend-demo`** — 硬件曲线与一键诊断报告（带 `stdio JSON-RPC 2.0` PowerShell 独立后端，实时绘制 APU 功耗/温度 SVG 曲线并一键导出硬件与游戏库诊断报告，同时演示后端 RPC 调用、`stderr` 调试日志与游戏生命周期事件）。

打包方法：推荐使用仓库自带的合规校验与打包脚本（与官方 Decky CI 审核规则一致）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -PluginDir .\examples\plugins\halodeck.skin-studio
```

调试技巧：在 HaloDeck **「插件扩展」** 顶部开启 **「🛠 开发者模式」**，即可直接 **「导入本地目录」**（无需反复打 `.zip`）、一键 **「重启后端」** 与 **「重载」** 前端界面。

> 💡 **掌机手柄与双主题提示**：`plugin-sdk.js` 已内置掌机手柄自动空间导航（方向键、`A` 确认、`B` 返回、左右调节 `<input type="range">` 滑块）以及深/浅双主题 CSS 变量自动同步；建议下拉框/颜色选择器使用纯 DOM 组件以确保在 QAM 无焦点侧栏窗口下稳定交互。

---

## 7. 发布与分发（官方商店 Decky 审核 / 社区独立源）

1. **提交上架官方纯净商店（推荐，Decky 审核模式）**：
   - 开源仓库 + 锁定 40 位不可变 `commit` SHA + 禁止源码夹带预编译二进制 + GitHub Actions 干净环境重编译与 `SHA-256` 核验；
   - 完整提交流程与 JSON 登记模板见 [`plugins/submissions/README.md`](../plugins/submissions/README.md)。
2. **GitHub 仓库直装（最快）**：
   - 将你的插件打包为 `.zip`（根目录或单层子目录含 `plugin.json`）上传至自己仓库的 GitHub Release；
   - 用户只需在 HaloDeck「插件中心 → **链接 / GitHub 直装**」输入你的 `owner/repo` 或 `.zip` 链接即可一键安装。
3. **搭建第三方社区商店源（`store.json`）**：
   - 在 GitHub / Gitee 托管一个静态 `store.json`（格式参考本仓库 [`plugins/store.json`](../plugins/store.json)），用户可在「插件中心 → **插件商店**」点击 **「+ 订阅源」** 粘入 `https://...` 地址，自动支持版本比对与一键更新（更新时保留 `plugin_data` 用户数据）。
