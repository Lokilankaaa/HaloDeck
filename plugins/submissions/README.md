# HaloDeck 官方插件商店提交流程（Decky 模式）

为最大程度降低恶意代码风险，HaloDeck 官方纯净商店采用与 **Decky Loader** 一致的 **「强制开源 + 锁定 Commit + GitHub Actions 干净环境重编译 + SHA-256 锁包」** 审核机制：

## 准入规则
1. **必须开源**：插件源码托管在公开的 GitHub 仓库。
2. **禁止提交闭源黑盒二进制**：源码仓库内不得夹带预编译 `.exe` / `.dll` / `.sys` 等二进制文件；若插件包含 C# 等编译型后端，由 GitHub Actions 在干净环境直接从源码编译打包。
3. **锁定不可变 40 位 Commit SHA**：PR 中必须指定精确的 40 位 `commit` 哈希（禁止填 `main` / `latest`），防止过审后被事后替换投毒。
4. **合规纯净**：官方商店不收录网游外挂、反作弊绕过或敏感修改器类插件（此类社区扩展请由作者自行分发，用户通过「第三方商店源」或「链接直装」按需安装）。

## 如何提交新插件 / 更新版本
1. 先在本地运行官方校验与打包脚本自检（与 CI 规则 100% 一致）：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -PluginDir <你的插件目录> -AuditOnly
   ```
2. 在本目录下新增或更新 `<你的插件ID>.json`（例如 `author.my-plugin.json`）：
   ```json
   {
     "id": "author.my-plugin",
     "repo": "https://github.com/author/halodeck-my-plugin",
     "commit": "0123456789abcdef0123456789abcdef01234567",
     "subpath": ".",
     "category": "utility"
   }
   ```
3. 向 `Lokilankaaa/HaloDeck` 发起 Pull Request：
   - CI（`verify-plugin-store.yml`）会自动克隆你锁定的 `commit`、校验 `plugin.json`、扫描黑盒二进制与高危脚本、在干净环境编译生成 `.zip` 与 `sha256`，并自动在 PR 下发表结构化安全审核报告；
   - 维护者完成代码 diff 审查并合并至 `main` 后，`publish-plugin-store.yml` 将**全自动**在干净环境重编译、上传至 `plugin-store-builds` Release 并同步写入 `plugins/store.json`，通过 jsDelivr 推送到全球 HaloDeck 客户端。
