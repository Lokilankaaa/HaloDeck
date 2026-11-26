## 社区插件官方商店投稿清单（Decky 审核模式）

请确保您已阅读 [`plugins/submissions/README.md`](../../plugins/submissions/README.md) 并先在本地运行过校验脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -PluginDir <你的插件目录> -AuditOnly
```

### 基本信息
- **插件 ID**：`author.my-plugin`
- **插件名称与版本**：`我的插件 v1.0.0`
- **开源仓库地址**：`https://github.com/author/repo`
- **锁定 40 位 Commit SHA**：`0123456789abcdef0123456789abcdef01234567`
- **分类**：`theme` / `utility` / `monitor` / `developer`
- **简短说明（一句话）**：

### 合规与安全自检（请勾选）
- [ ] 插件完整源码已托管在公开 GitHub 仓库，并在 `plugins/submissions/<id>.json` 中锁定不可变的 40 位 `commit` SHA
- [ ] 源码仓库内**不含**任何预编译 `.exe` / `.dll` / `.sys` 等黑盒二进制文件（C# 后端由 GitHub Actions 从源码编译）
- [ ] 插件遵循最小权限原则，仅申请实际使用到的权限
- [ ] 插件不包含任何网游外挂、反作弊绕过、内存篡改或恶意持久化逻辑
- [ ] 已在 HaloDeck **主界面 + QAM 侧栏**、**Aurora 深色 + Frost 浅色**双主题以及**手柄方向键 / A / B** 操控下验收通过
