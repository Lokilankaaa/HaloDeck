(() => {
  const params = new URLSearchParams(window.location.search);
  const mode = params.get("mode") === "qam" ? "qam" : "console";
  document.body.classList.add(`mode-${mode}`);

  if (mode === "qam") {
    document.getElementById("consoleHeader").classList.add("hidden");
    document.getElementById("qamHeader").classList.remove("hidden");
  }

  const state = {
    mode,
    tab: "running",
    snapshot: null,
    selectedPresetId: "strix-halo-quality",
    draftConfig: null,
    filterPlatform: "ALL",
    searchQuery: "",
    componentProgress: {},
    recordingTarget: null, // 'qam' | 'insert'
    recordingHotkeyField: null, // 'qamHotkey' | 'insertHotkey' | 'consoleHotkey'
    busy: false
  };

  let rpcSeq = 0;
  const pendingRpc = new Map();

  function rpc(method, payload = {}) {
    return new Promise((resolve, reject) => {
      const id = `rpc_${++rpcSeq}`;
      pendingRpc.set(id, { resolve, reject });
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ id, method, payload });
      } else {
        reject(new Error("WebView2 bridge 不可用"));
      }
    });
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", (event) => {
      const msg = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
      if (msg.type === "response") {
        const entry = pendingRpc.get(msg.id);
        if (entry) {
          pendingRpc.delete(msg.id);
          if (msg.ok) entry.resolve(msg.result);
          else entry.reject(new Error(msg.error || "操作失败"));
        }
      } else if (msg.type === "event") {
        handleHostEvent(msg.eventName, msg.data);
      }
    });
  }

  function handleHostEvent(eventName, data) {
    if (eventName === "stateChanged") {
      applySnapshot(data);
    } else if (eventName === "gamepadStatus") {
      if (state.snapshot) state.snapshot.connectedControllers = data.connectedCount;
      updateControllerPill();
    } else if (eventName === "componentProgress") {
      state.componentProgress[data.slot] = data.percent;
      const bar = document.querySelector(`[data-progress-slot="${data.slot}"]`);
      const label = document.querySelector(`[data-progress-label="${data.slot}"]`);
      if (bar) bar.style.width = `${data.percent}%`;
      if (label) label.textContent = `下载解压中 ${data.percent}%`;
    } else if (eventName === "gamepadAction") {
      handleGamepadAction(data.action);
    } else if (eventName === "gamepadComboRecorded") {
      onGamepadComboRecorded(data.buttons);
    } else if (eventName === "toast") {
      showToast(data.message, data.kind === "err" ? "err" : "ok");
    }
  }

  function applySnapshot(snap) {
    state.snapshot = snap;
    if (!state.draftConfig && snap.presets && snap.presets.length > 0) {
      const defId = snap.settings?.defaultPresetId || snap.presets[0].id;
      const preset = snap.presets.find((p) => p.id === defId) || snap.presets[0];
      state.selectedPresetId = preset.id;
      state.draftConfig = { ...preset.config };
    }
    const libPill = document.getElementById("libCountPill");
    if (libPill) libPill.textContent = String(snap.games?.length || 0);
    updateControllerPill();
    render();
  }

  function updateControllerPill() {
    const count = state.snapshot?.connectedControllers || 0;
    const pill = document.getElementById("controllerPill");
    if (pill) {
      pill.textContent = count > 0 ? `🎮 手柄已就绪 (${count})` : "⌨️ 触控 / 键鼠模式";
      pill.classList.toggle("muted", count === 0);
    }
    const qamSub = document.getElementById("qamControllerSub");
    if (qamSub) {
      qamSub.textContent = count > 0 ? `GPD Win 5 · XInput 手柄在线` : `GPD Win 5 · 触控 / 快捷键`;
    }
  }

  function showToast(message, kind = "ok") {
    const stack = document.getElementById("toastStack");
    const el = document.createElement("div");
    el.className = `toast ${kind}`;
    el.textContent = message;
    stack.appendChild(el);
    setTimeout(() => el.remove(), 4200);
  }

  function escapeHtml(str) {
    return String(str ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function getActiveTargetGame() {
    if (!state.snapshot) return null;
    return state.snapshot.runningGame || state.snapshot.activeInspectGame || state.snapshot.games?.[0] || null;
  }

  // =========================================================================
  // Spatial 2D Gamepad Focus Navigator
  // =========================================================================
  let currentFocusedEl = null;

  function getFocusableElements() {
    const modal = document.getElementById("modalBackdrop");
    const scope = !modal.classList.contains("hidden") ? modal : document.getElementById("app");
    return Array.from(scope.querySelectorAll("[data-nav]")).filter((el) => {
      if (el.disabled) return false;
      const rect = el.getBoundingClientRect();
      return rect.width > 0 && rect.height > 0 && getComputedStyle(el).visibility !== "hidden";
    });
  }

  function setFocusedElement(el) {
    if (currentFocusedEl) currentFocusedEl.classList.remove("gp-focused");
    currentFocusedEl = el;
    if (el) {
      el.classList.add("gp-focused");
      el.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
    }
  }

  function ensureFocusValid() {
    const list = getFocusableElements();
    if (list.length === 0) return null;
    if (currentFocusedEl && document.contains(currentFocusedEl) && list.includes(currentFocusedEl)) {
      currentFocusedEl.classList.add("gp-focused");
      return currentFocusedEl;
    }
    const preferred = list.find((e) => e.closest("#viewport")) || list[0];
    setFocusedElement(preferred);
    return preferred;
  }

  function moveSpatialFocus(dir) {
    const list = getFocusableElements();
    if (list.length === 0) return;
    const active = ensureFocusValid();
    if (!active) return;

    const r1 = active.getBoundingClientRect();
    const cx1 = r1.left + r1.width / 2;
    const cy1 = r1.top + r1.height / 2;

    let best = null;
    let bestScore = Infinity;

    for (const cand of list) {
      if (cand === active) continue;
      const r2 = cand.getBoundingClientRect();
      const cx2 = r2.left + r2.width / 2;
      const cy2 = r2.top + r2.height / 2;
      const dx = cx2 - cx1;
      const dy = cy2 - cy1;

      if (dir === "UP" && dy >= -6) continue;
      if (dir === "DOWN" && dy <= 6) continue;
      if (dir === "LEFT" && dx >= -6) continue;
      if (dir === "RIGHT" && dx <= 6) continue;

      const primary = dir === "UP" || dir === "DOWN" ? Math.abs(dy) : Math.abs(dx);
      const secondary = dir === "UP" || dir === "DOWN" ? Math.abs(dx) : Math.abs(dy);
      const score = primary + secondary * 2.2;
      if (score < bestScore) {
        bestScore = score;
        best = cand;
      }
    }

    if (best) setFocusedElement(best);
  }

  function handleGamepadAction(action) {
    if (action === "UP" || action === "DOWN" || action === "LEFT" || action === "RIGHT") {
      moveSpatialFocus(action);
      return;
    }
    if (action === "A") {
      const el = ensureFocusValid();
      if (el) el.click();
      return;
    }
    if (action === "B") {
      const modal = document.getElementById("modalBackdrop");
      if (!modal.classList.contains("hidden")) {
        closeModal();
        return;
      }
      if (state.mode === "qam") {
        rpc("hideQam").catch(() => {});
      }
      return;
    }
    if (action === "LB" || action === "RB") {
      if (state.mode === "console") {
        const tabs = ["running", "library", "components", "settings"];
        const idx = tabs.indexOf(state.tab);
        const nextIdx = action === "RB" ? (idx + 1) % tabs.length : (idx - 1 + tabs.length) % tabs.length;
        switchTab(tabs[nextIdx]);
      }
      return;
    }
    if (action === "X") {
      const game = getActiveTargetGame();
      if (game) performInject(game, false);
      return;
    }
    if (action === "Y") {
      const game = getActiveTargetGame();
      if (game) performInject(game, true);
    }
  }

  window.addEventListener("keydown", (e) => {
    if (e.target && (e.target.tagName === "INPUT" || e.target.tagName === "SELECT")) {
      if (e.key === "Escape") e.target.blur();
      return;
    }
    if (e.key === "ArrowUp") { e.preventDefault(); handleGamepadAction("UP"); }
    else if (e.key === "ArrowDown") { e.preventDefault(); handleGamepadAction("DOWN"); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); handleGamepadAction("LEFT"); }
    else if (e.key === "ArrowRight") { e.preventDefault(); handleGamepadAction("RIGHT"); }
    else if (e.key === "Enter") { e.preventDefault(); handleGamepadAction("A"); }
    else if (e.key === "Escape") { e.preventDefault(); handleGamepadAction("B"); }
    else if (e.key === "[") { e.preventDefault(); handleGamepadAction("LB"); }
    else if (e.key === "]") { e.preventDefault(); handleGamepadAction("RB"); }
  });

  // =========================================================================
  // Core Actions
  // =========================================================================
  function switchTab(tabName) {
    state.tab = tabName;
    document.querySelectorAll(".tab-btn").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.tab === tabName);
    });
    currentFocusedEl = null;
    render();
  }

  function selectPreset(presetId) {
    const preset = state.snapshot?.presets?.find((p) => p.id === presetId);
    if (!preset) return;
    state.selectedPresetId = preset.id;
    state.draftConfig = { ...preset.config };
    render();
  }

  async function performInject(game, restartRunning, allowAntiCheatOverride = false) {
    if (state.busy) return;

    if (game.antiCheat?.hasAntiCheat && !allowAntiCheatOverride) {
      openConfirmModal(
        "⚠️ 反作弊安全警告",
        `检测到 <strong>${escapeHtml(game.title)}</strong> 包含反作弊组件：<br><br><span class="badge danger">${escapeHtml(game.antiCheat.detectedSystems.join(", "))}</span><br><br>在联机或反作弊游戏中注入代理 DLL 可能导致闪退或封号风险。仅建议在离线单机模式下测试，确认继续吗？`,
        () => performInject(game, restartRunning, true)
      );
      return;
    }

    state.busy = true;
    try {
      const method = restartRunning ? "injectAndRestartGame" : "injectGame";
      const res = await rpc(method, {
        gameId: game.id,
        presetId: state.selectedPresetId,
        config: state.draftConfig,
        allowAntiCheatOverride
      });
      if (res.success) {
        showToast(res.message, "ok");
        if (res.warnings?.length) {
          res.warnings.forEach((w) => showToast(`提示: ${w}`, "ok"));
        }
      } else {
        showToast(res.message || "注入失败", "err");
      }
    } catch (err) {
      showToast(err.message, "err");
    } finally {
      state.busy = false;
    }
  }

  async function performRestore(game) {
    if (state.busy) return;
    state.busy = true;
    try {
      const res = await rpc("restoreGame", { gameId: game.id });
      showToast(res.message, res.success ? "ok" : "err");
    } catch (err) {
      showToast(err.message, "err");
    } finally {
      state.busy = false;
    }
  }

  async function inspectGameLogModal(game) {
    try {
      const info = await rpc("readGameConfigAndLog", { gameId: game.id });
      const modal = document.getElementById("modalCard");
      modal.innerHTML = `
        <div class="section-title">
          <span>📄 ${escapeHtml(game.title)} · 注入诊断与日志</span>
          <button id="btnCloseModal" class="chip-btn" data-nav>关闭 [B]</button>
        </div>
        <div class="muted-text" style="margin-bottom:10px;">
          渲染注入目录：<code>${escapeHtml(game.targetRenderDirectory)}</code>
        </div>
        <div class="section-title" style="font-size:13px;">OptiScaler.log 最近输出</div>
        <pre class="log-pre">${escapeHtml(info.logSnippet || "（暂无运行日志，启动游戏后 OptiScaler 会生成 OptiScaler.log）")}</pre>
        <div class="section-title" style="font-size:13px; margin-top:12px;">OptiScaler.ini 核心生效配置</div>
        <pre class="log-pre">${escapeHtml(info.iniPreview || "（尚未生成 OptiScaler.ini）")}</pre>
      `;
      openModalBackdrop();
      modal.querySelector("#btnCloseModal").onclick = closeModal;
    } catch (err) {
      showToast(err.message, "err");
    }
  }

  function openConfirmModal(title, htmlBody, onConfirm) {
    const modal = document.getElementById("modalCard");
    modal.innerHTML = `
      <div class="section-title">${title}</div>
      <div class="muted-text">${htmlBody}</div>
      <div class="action-row" style="margin-top:16px;">
        <button id="btnModalCancel" class="btn" data-nav>取消 [B]</button>
        <button id="btnModalConfirm" class="btn danger" data-nav>已知晓风险，强制执行</button>
      </div>
    `;
    openModalBackdrop();
    modal.querySelector("#btnModalCancel").onclick = closeModal;
    modal.querySelector("#btnModalConfirm").onclick = () => {
      closeModal();
      onConfirm();
    };
  }

  function openModalBackdrop() {
    document.getElementById("modalBackdrop").classList.remove("hidden");
    currentFocusedEl = null;
    setTimeout(ensureFocusValid, 30);
  }

  function closeModal() {
    document.getElementById("modalBackdrop").classList.add("hidden");
    currentFocusedEl = null;
    setTimeout(ensureFocusValid, 30);
  }

  // =========================================================================
  // Render Views
  // =========================================================================
  function render() {
    if (!state.snapshot) return;
    renderLegend();

    const viewport = document.getElementById("viewport");
    if (state.mode === "qam") {
      viewport.innerHTML = renderQamView();
      bindRunningControls(viewport, getActiveTargetGame());
      setTimeout(ensureFocusValid, 20);
      return;
    }

    if (state.tab === "running") {
      viewport.innerHTML = renderRunningConsoleView();
      bindRunningControls(viewport, getActiveTargetGame());
    } else if (state.tab === "library") {
      viewport.innerHTML = renderLibraryView();
      bindLibraryControls(viewport);
    } else if (state.tab === "components") {
      viewport.innerHTML = renderComponentsView();
      bindComponentsControls(viewport);
    } else if (state.tab === "settings") {
      viewport.innerHTML = renderSettingsView();
      bindSettingsControls(viewport);
    }

    setTimeout(ensureFocusValid, 20);
  }

  function renderLegend() {
    const el = document.getElementById("legendItems");
    if (!el) return;
    if (state.mode === "qam") {
      el.innerHTML = `
        <span class="legend-item"><span class="keycap">A</span> 确认</span>
        <span class="legend-item"><span class="keycap">X</span> 写入注入</span>
        <span class="legend-item"><span class="keycap">Y</span> 注入并重启</span>
        <span class="legend-item"><span class="keycap">B</span> 返回游戏</span>
      `;
    } else {
      el.innerHTML = `
        <span class="legend-item"><span class="keycap">LB/RB</span> 切换页面</span>
        <span class="legend-item"><span class="keycap">十字键/摇杆</span> 导航</span>
        <span class="legend-item"><span class="keycap">A</span> 确认</span>
        <span class="legend-item"><span class="keycap">X</span> 一键注入</span>
        <span class="legend-item"><span class="keycap">Y</span> 注入并重启</span>
      `;
    }
    const notice = document.getElementById("globalNotice");
    if (notice && state.mode === "console") {
      const optiStable = state.snapshot?.components?.find((c) => c.slot === "OptiScalerStable");
      notice.textContent = optiStable?.isInstalled
        ? `核心就绪：OptiScaler ${optiStable.installedVersion}`
        : "提示：请先到「组件中心」下载或导入 OptiScaler";
    }
  }

  function renderGameSummaryCard(game) {
    if (!game) {
      return `
        <div class="card">
          <div class="section-title">⚡ 暂无检测到的运行游戏</div>
          <p class="muted-text">打开任意游戏后此处会自动锁定真实进程与渲染目录（如 UE5 <code>Binaries\\Win64</code>）。也可先前往「游戏库」添加或挑选游戏。</p>
          <button id="btnJumpLibrary" class="btn primary full" data-nav style="margin-top:12px;">前往游戏库选择游戏</button>
        </div>
      `;
    }

    const caps = game.nativeUpscalers || {};
    const statusBadge = game.injection?.isInjected
      ? `<span class="badge ok">已注入 · ${escapeHtml(game.injection.proxyDllName)}</span>`
      : `<span class="badge">未注入</span>`;
    const runBadge = game.isCurrentlyRunning
      ? `<span class="badge info">● 前台运行中 (PID ${game.runningPid})</span>`
      : `<span class="badge">当前选中目标</span>`;

    return `
      <div class="card">
        <div class="section-title">
          <span>${escapeHtml(game.title)}</span>
          ${runBadge}
        </div>
        <div class="badge-row">
          ${statusBadge}
          <span class="badge">${escapeHtml(game.platform)}</span>
          <span class="badge">${escapeHtml(game.engine)} · ${escapeHtml(game.architecture)}</span>
          ${caps.hasDlss ? '<span class="badge info">DLSS</span>' : ''}
          ${caps.hasXeSs ? '<span class="badge info">XeSS</span>' : ''}
          ${caps.hasFsr ? '<span class="badge info">FSR</span>' : ''}
        </div>
        <div class="muted-text" style="margin-top:12px;">
          <div><strong>渲染目录：</strong><code>${escapeHtml(game.targetRenderDirectory)}</code></div>
          ${game.injection?.isInjected ? `<div style="margin-top:4px;"><strong>已部署：</strong>OptiPatcher ${game.injection.hasOptiPatcher ? "✓" : "✕"} · Fakenvapi ${game.injection.hasFakenvapi ? "✓" : "✕"}</div>` : ""}
        </div>

        ${game.antiCheat?.hasAntiCheat ? `<div class="banner-danger">${escapeHtml(game.antiCheat.summary)}。为防止误封，仅限离线单机时手动确认。</div>` : ""}
        ${!game.isWritable ? `<div class="banner-warn">目标目录受 Windows 权限保护（例如 Xbox WindowsApps 默认目录），无法直接写入 DLL。</div>` : ""}

        <div class="action-stack" style="margin-top:14px;">
          <button id="btnInjectRestart" class="btn primary full" data-nav>${game.isCurrentlyRunning ? "⚡ 一键注入并重启游戏 [Y]" : "🚀 写入注入配置并启动游戏 [Y]"}</button>
          <div class="action-row">
            <button id="btnInjectOnly" class="btn success" data-nav>✍️ 仅写入配置 [X]</button>
            <button id="btnSendInsert" class="btn" data-nav>⌨️ 呼出游戏内菜单 (Insert)</button>
          </div>
          <div class="action-row">
            <button id="btnRestoreGame" class="btn danger" data-nav ${game.injection?.isInjected ? "" : "disabled"}>🧹 卸载并还原目录</button>
            <button id="btnInspectLog" class="btn" data-nav>📄 查看日志与 INI</button>
          </div>
          ${state.mode === "console" ? `
            <div class="action-row">
              <button id="btnOpenTargetFolder" class="btn" data-nav>📂 打开注入目录</button>
              <button id="btnSwitchRunningTarget" class="btn" data-nav>🔄 切换目标游戏</button>
            </div>
          ` : ""}
        </div>
      </div>
    `;
  }

  function renderConfigEditorPanel(compact = false) {
    const cfg = state.draftConfig || {};
    const presets = state.snapshot.presets || [];
    const proxyOptions = state.snapshot.proxyOptions || ["auto", "dxgi.dll", "winmm.dll", "version.dll"];

    return `
      <div class="card ${compact ? "card-compact" : ""}">
        <div class="section-title">
          <span>🎛️ GPD Win 5 (RDNA 3.5) 预设与参数</span>
          <span class="badge info">Strix Halo</span>
        </div>

        <div class="preset-grid">
          ${presets.map((p) => `
            <button class="preset-card ${state.selectedPresetId === p.id ? "active" : ""}" data-nav data-preset="${escapeHtml(p.id)}">
              <div class="preset-name">${escapeHtml(p.name)}</div>
              <div class="preset-sub">${escapeHtml(p.subtitle)}</div>
              ${compact ? "" : `<div class="preset-desc">${escapeHtml(p.description)}</div>`}
            </button>
          `).join("")}
        </div>

        <div class="control-grid" style="margin-bottom:10px;">
          <div class="field-box">
            <label class="field-label">代理注入 DLL (自动避让 ReShade 冲突)</label>
            <select id="cfgProxyDll" class="select-input" data-nav>
              ${proxyOptions.map((opt) => `<option value="${escapeHtml(opt)}" ${cfg.proxyDllName === opt ? "selected" : ""}>${opt === "auto" ? "auto (智能冲突避让)" : opt}</option>`).join("")}
            </select>
          </div>
          <div class="field-box">
            <label class="field-label">帧生成方式 (FrameGen Output)</label>
            <select id="cfgFgType" class="select-input" data-nav>
              <option value="optifg" ${cfg.fgType === "optifg" ? "selected" : ""}>optifg (OptiFG · FSR 帧生推荐)</option>
              <option value="xefg" ${cfg.fgType === "xefg" ? "selected" : ""}>xefg (Intel XeFG 帧生成)</option>
              <option value="nukems" ${cfg.fgType === "nukems" ? "selected" : ""}>nukems (DLSSG-to-FSR3 桥接)</option>
              <option value="auto" ${cfg.fgType === "auto" ? "selected" : ""}>auto</option>
              <option value="nofg" ${cfg.fgType === "nofg" ? "selected" : ""}>nofg (关闭插帧 · 极低延迟)</option>
            </select>
          </div>
          <div class="field-box">
            <label class="field-label">DX12 超分后端 (Dx12Upscaler)</label>
            <select id="cfgDx12" class="select-input" data-nav>
              <option value="auto" ${cfg.dx12Upscaler === "auto" ? "selected" : ""}>auto (默认 XeSS)</option>
              <option value="fsr31" ${cfg.dx12Upscaler === "fsr31" || cfg.dx12Upscaler === "ffx" ? "selected" : ""}>fsr31 (AMD FSR 3.1 / 4)</option>
              <option value="xess" ${cfg.dx12Upscaler === "xess" ? "selected" : ""}>xess (Intel XeSS 抗闪烁)</option>
              <option value="fsr22" ${cfg.dx12Upscaler === "fsr22" ? "selected" : ""}>fsr22</option>
              <option value="dlss" ${cfg.dx12Upscaler === "dlss" ? "selected" : ""}>dlss</option>
            </select>
          </div>
          ${compact ? "" : `
          <div class="field-box">
            <label class="field-label">DX11 超分后端 (Dx11Upscaler)</label>
            <select id="cfgDx11" class="select-input" data-nav>
              <option value="fsr31" ${cfg.dx11Upscaler === "fsr31" ? "selected" : ""}>fsr31 (原生 DX11 FSR 3.1)</option>
              <option value="auto" ${cfg.dx11Upscaler === "auto" ? "selected" : ""}>auto</option>
              <option value="fsr22" ${cfg.dx11Upscaler === "fsr22" ? "selected" : ""}>fsr22</option>
              <option value="xess_12" ${cfg.dx11Upscaler === "xess_12" ? "selected" : ""}>xess_12 (DX11on12)</option>
              <option value="fsr31_12" ${cfg.dx11Upscaler === "fsr31_12" || cfg.dx11Upscaler === "ffx_12" ? "selected" : ""}>fsr31_12 (DX11on12)</option>
            </select>
          </div>
          `}
        </div>

        <div class="action-stack">
          <button id="toggleOptiPatcher" class="toggle-row" data-nav>
            <div>
              <div class="toggle-title">启用 OptiPatcher (plugins/OptiPatcher.asi)</div>
              <div class="toggle-desc">免 DXGI 显卡伪装，直接解锁游戏菜单中的 DLSS 选项</div>
            </div>
            <span class="switch-pill ${cfg.enableOptiPatcher ? "on" : ""}"></span>
          </button>

          <button id="toggleFakenvapi" class="toggle-row" data-nav>
            <div>
              <div class="toggle-title">启用 Fakenvapi (OverrideNvapiDll=true)</div>
              <div class="toggle-desc">将 NVIDIA Reflex 实时转换为 AMD Anti-Lag 2</div>
            </div>
            <span class="switch-pill ${cfg.enableFakenvapi ? "on" : ""}"></span>
          </button>

          ${compact ? "" : `
          <button id="toggleSpoofDxgi" class="toggle-row" data-nav>
            <div>
              <div class="toggle-title">启用 DXGI 显卡伪装 (RTX 4090)</div>
              <div class="toggle-desc">仅当游戏未适配 OptiPatcher 且隐藏 DLSS 选项时启用</div>
            </div>
            <span class="switch-pill ${cfg.spoofDxgi ? "on" : ""}"></span>
          </button>
          `}
        </div>
      </div>
    `;
  }

  function renderRunningConsoleView() {
    const game = getActiveTargetGame();
    return `
      <div class="split-grid">
        <div>${renderGameSummaryCard(game)}</div>
        <div>${renderConfigEditorPanel(false)}</div>
      </div>
    `;
  }

  function renderQamView() {
    const game = getActiveTargetGame();
    return `
      <div class="action-stack">
        ${renderGameSummaryCard(game)}
        ${renderConfigEditorPanel(true)}
      </div>
    `;
  }

  function renderLibraryView() {
    const games = state.snapshot.games || [];
    const platforms = [
      { id: "ALL", label: "全部" },
      { id: "INJECTED", label: "已注入" },
      { id: "Steam", label: "Steam" },
      { id: "Epic", label: "Epic" },
      { id: "Gog", label: "GOG" },
      { id: "Xbox", label: "Xbox" },
      { id: "Custom", label: "自定义" }
    ];

    const q = state.searchQuery.trim().toLowerCase();
    const filtered = games.filter((g) => {
      if (state.filterPlatform === "INJECTED" && !g.injection?.isInjected) return false;
      if (state.filterPlatform !== "ALL" && state.filterPlatform !== "INJECTED" && g.platform !== state.filterPlatform) return false;
      if (q && !g.title.toLowerCase().includes(q) && !g.installDirectory.toLowerCase().includes(q)) return false;
      return true;
    });

    return `
      <div class="toolbar">
        <div class="filter-row">
          ${platforms.map((p) => `
            <button class="chip-btn ${state.filterPlatform === p.id ? "active" : ""}" data-nav data-filter="${p.id}">${p.label}</button>
          `).join("")}
        </div>
        <div class="filter-row">
          <input id="libSearch" class="text-input" style="width:200px;" placeholder="搜索游戏名或路径..." value="${escapeHtml(state.searchQuery)}" />
          <button id="btnScanGames" class="chip-btn" data-nav>🔄 重新扫描</button>
          <button id="btnAddExe" class="chip-btn active" data-nav>+ 添加 .exe</button>
          <button id="btnAddFolder" class="chip-btn" data-nav>+ 添加目录</button>
        </div>
      </div>

      ${filtered.length === 0 ? `
        <div class="card">
          <div class="section-title">未找到匹配游戏</div>
          <p class="muted-text">点击右上角「+ 添加 .exe」或「+ 添加目录」即可将绿色版 / 独立游戏纳入管理并一键识别 UE5/Unity 注入路径。</p>
        </div>
      ` : `
        <div class="library-grid">
          ${filtered.map((g) => `
            <div class="game-card" data-nav data-game-card="${escapeHtml(g.id)}">
              <div class="game-cover">
                ${g.coverImageUrl ? `<img src="${escapeHtml(g.coverImageUrl)}" onerror="this.remove()" />` : ""}
                <div class="game-cover-title">${escapeHtml(g.title)}</div>
              </div>
              <div class="game-card-body">
                <div class="badge-row" style="margin-top:0;">
                  ${g.injection?.isInjected ? `<span class="badge ok">已注入 · ${escapeHtml(g.injection.proxyDllName)}</span>` : `<span class="badge">未注入</span>`}
                  <span class="badge">${escapeHtml(g.platform)}</span>
                  <span class="badge">${escapeHtml(g.engine)}</span>
                  ${g.antiCheat?.hasAntiCheat ? `<span class="badge danger">反作弊</span>` : ""}
                </div>
                <div class="game-path" title="${escapeHtml(g.targetRenderDirectory)}">${escapeHtml(g.targetRenderDirectory)}</div>
                <div class="card-actions">
                  <button class="chip-btn active" data-nav data-quick-select="${escapeHtml(g.id)}">配置注入</button>
                  <button class="chip-btn" data-nav data-quick-launch="${escapeHtml(g.id)}">启动</button>
                </div>
              </div>
            </div>
          `).join("")}
        </div>
      `}
    `;
  }

  function renderComponentsView() {
    const comps = state.snapshot.components || [];
    const customList = state.snapshot.customExtensions || [];
    const activeSlot = state.snapshot.settings?.activeOptiScalerSlot || "OptiScalerStable";

    return `
      <div class="toolbar">
        <div>
          <div class="section-title" style="margin:0;">📦 OptiScaler 与周边组件生态中心</div>
          <div class="muted-text">自动从 GitHub Releases 拉取更新并支持固实 .7z 解压，或导入本地离线包。</div>
        </div>
        <button id="btnRefreshCatalog" class="btn primary" data-nav>☁️ 检查 GitHub 最新版本</button>
      </div>

      <div class="comp-grid">
        ${comps.map((c) => {
          const isOptiSlot = c.slot === "OptiScalerStable" || c.slot === "OptiScalerNightly";
          const isActiveKernel = activeSlot === c.slot;
          const pct = state.componentProgress[c.slot] || 0;
          return `
            <div class="card">
              <div class="section-title">
                <span>${escapeHtml(c.displayName)}</span>
                ${c.isInstalled ? `<span class="badge ok">已安装 · ${escapeHtml(c.installedVersion)}</span>` : `<span class="badge warn">未安装</span>`}
              </div>
              <div class="muted-text">${escapeHtml(c.description)}</div>
              <div class="badge-row">
                <span class="badge info">GitHub: ${escapeHtml(c.repoSlug)}</span>
                ${c.latestVersion ? `<span class="badge">最新: ${escapeHtml(c.latestVersion)}</span>` : ""}
                ${c.latestAsset ? `<span class="badge">${(c.latestAsset.sizeBytes / 1048576).toFixed(1)} MB</span>` : ""}
              </div>
              <div class="progress-track">
                <div class="progress-fill" data-progress-slot="${escapeHtml(c.slot)}" style="width:${pct}%"></div>
              </div>
              <div class="muted-text" data-progress-label="${escapeHtml(c.slot)}" style="font-size:11px; margin-top:4px;">
                ${pct > 0 && pct < 100 ? `处理中 ${pct}%` : (c.hasUpdate ? "发现新版本可更新" : "")}
              </div>
              <div class="action-row" style="margin-top:12px;">
                <button class="btn primary" data-nav data-install-comp="${escapeHtml(c.slot)}">${c.isInstalled ? "⬇️ 更新 / 重装" : "⬇️ 在线下载安装"}</button>
                <button class="btn" data-nav data-import-comp="${escapeHtml(c.slot)}">📁 导入离线包</button>
              </div>
              ${isOptiSlot ? `
                <button class="btn full ${isActiveKernel ? "success" : ""}" data-nav data-set-active-slot="${escapeHtml(c.slot)}" style="margin-top:8px;">
                  ${isActiveKernel ? "✓ 当前默认注入内核" : "设为此版本为默认注入内核"}
                </button>
              ` : ""}
            </div>
          `;
        }).join("")}
      </div>

      <div class="card" style="margin-top:16px;">
        <div class="section-title">
          <span>🧩 自定义扩展 DLL / ASI（如 FSR 4 DLL / dlssg-to-fsr3）</span>
          <button id="btnImportCustomExt" class="chip-btn active" data-nav>+ 导入 DLL / ASI</button>
        </div>
        ${customList.length === 0 ? `<p class="muted-text">暂未导入自定义扩展文件。导入后会在每次注入时自动附带部署（.asi 自动放入 plugins 目录）。</p>` : `
          <div class="action-stack">
            ${customList.map((f) => `
              <div class="toggle-row" style="cursor:default;">
                <div>
                  <div class="toggle-title">${escapeHtml(f.fileName)}</div>
                  <div class="toggle-desc">${(f.sizeBytes / 1024).toFixed(1)} KB</div>
                </div>
                <div style="display:flex; gap:8px;">
                  <button class="chip-btn ${f.enabled ? "active" : ""}" data-nav data-toggle-ext="${escapeHtml(f.fileName)}" data-ext-enabled="${f.enabled}">
                    ${f.enabled ? "已启用" : "已禁用"}
                  </button>
                  <button class="chip-btn danger" data-nav data-del-ext="${escapeHtml(f.fileName)}">删除</button>
                </div>
              </div>
            `).join("")}
          </div>
        `}
      </div>
    `;
  }

  function renderSettingsView() {
    const s = state.snapshot.settings;
    const presets = state.snapshot.presets || [];
    return `
      <div class="split-grid">
        <div class="card">
          <div class="section-title">🤖 自动注入与掌机启动策略</div>
          <div class="action-stack">
            <button id="setAutoInject" class="toggle-row" data-nav>
              <div>
                <div class="toggle-title">后台自动预注入扫描</div>
                <div class="toggle-desc">定期为未注入且包含超分库的安全游戏自动部署预设（硬性跳过反作弊游戏）</div>
              </div>
              <span class="switch-pill ${s.backgroundAutoInjectEnabled ? "on" : ""}"></span>
            </button>

            <button id="btnRunAutoInjectNow" class="btn primary full" data-nav>⚡ 立即执行一次安全自动预注入</button>

            <div class="field-box">
              <label class="field-label">默认 RDNA 3.5 注入预设</label>
              <select id="setDefaultPreset" class="select-input" data-nav>
                ${presets.map((p) => `<option value="${escapeHtml(p.id)}" ${s.defaultPresetId === p.id ? "selected" : ""}>${escapeHtml(p.name)}</option>`).join("")}
              </select>
            </div>

            <button id="setFullscreen" class="toggle-row" data-nav>
              <div>
                <div class="toggle-title">启动控制台时默认全屏大屏模式</div>
                <div class="toggle-desc">适合 GPD Win 5 全屏掌机体验</div>
              </div>
              <span class="switch-pill ${s.launchConsoleFullscreen ? "on" : ""}"></span>
            </button>

            <button id="setMinimizedTray" class="toggle-row" data-nav>
              <div>
                <div class="toggle-title">启动时静默最小化到系统托盘</div>
                <div class="toggle-desc">开机后后台静默待命，按手柄组合键随时呼出侧边栏</div>
              </div>
              <span class="switch-pill ${s.startMinimizedToTray ? "on" : ""}"></span>
            </button>

            <button id="setWinStartup" class="toggle-row" data-nav>
              <div>
                <div class="toggle-title">随 Windows 开机自启动</div>
                <div class="toggle-desc">写入当前用户开机自启项 (--minimized)</div>
              </div>
              <span class="switch-pill ${s.launchAtWindowsStartup ? "on" : ""}"></span>
            </button>
          </div>
        </div>

        <div class="card">
          <div class="section-title">🎮 GPD Win 5 手柄组合键与 L4/R4 背键配置</div>
          <div class="action-stack">
            <div class="field-box">
              <div class="toggle-title">呼出 / 收起右侧快捷菜单 (QAM) 手柄组合键</div>
              <div class="muted-text" style="margin:4px 0 8px;">当前：<span class="badge info">${escapeHtml(s.qamGamepadCombo?.buttons?.join(" + ") || "未绑定")}</span></div>
              <button id="btnRecQamCombo" class="btn full" data-nav>
                ${state.recordingTarget === "qam" ? "🔴 请在手柄上同时按下并松开组合键..." : "🎙️ 录制 QAM 手柄组合键"}
              </button>
            </div>

            <div class="field-box">
              <div class="toggle-title">呼出游戏内 OptiScaler 菜单 (Insert) 手柄组合键</div>
              <div class="muted-text" style="margin:4px 0 8px;">当前：<span class="badge info">${escapeHtml(s.insertGamepadCombo?.buttons?.join(" + ") || "未绑定")}</span></div>
              <button id="btnRecInsertCombo" class="btn full" data-nav>
                ${state.recordingTarget === "insert" ? "🔴 请在手柄上同时按下并松开组合键..." : "🎙️ 录制 Insert 手柄组合键"}
              </button>
            </div>

            <div class="field-box">
              <label class="field-label">Insert 按键脉冲保持时长 (毫秒，建议 50~90ms 防游戏掉帧漏检)</label>
              <input id="setInsertHoldMs" type="number" min="30" max="200" class="text-input" value="${s.insertHoldDurationMs || 65}" />
            </div>

            <div class="field-box">
              <div class="toggle-title">全局键盘 / GPD Win 5 背键热键录制</div>
              <div class="muted-text" style="margin:4px 0 8px;">点击按钮后直接按下目标组合键（推荐 L4 / R4 在 WinControls 中映射以下热键）</div>
              <div class="action-stack">
                <button id="btnHotkeyQam" class="btn full" data-nav>
                  ${state.recordingHotkeyField === "qamHotkey" ? "🔴 请按下新的 QAM 热键（如 Ctrl+Alt+O）..." : `QAM 快捷侧边栏：${escapeHtml(s.qamHotkey?.displayText || "Ctrl + Alt + O")}`}
                </button>
                <button id="btnHotkeyInsert" class="btn full" data-nav>
                  ${state.recordingHotkeyField === "insertHotkey" ? "🔴 请按下新的 Insert 热键（如 Ctrl+Alt+I）..." : `注入 Insert 菜单：${escapeHtml(s.insertHotkey?.displayText || "Ctrl + Alt + I")}`}
                </button>
                <button id="btnHotkeyConsole" class="btn full" data-nav>
                  ${state.recordingHotkeyField === "consoleHotkey" ? "🔴 请按下新的控制台热键（如 Ctrl+Shift+O）..." : `打开主控制台：${escapeHtml(s.consoleHotkey?.displayText || "Ctrl + Shift + O")}`}
                </button>
              </div>
            </div>

            <div class="banner-warn">
              <strong>GPD Win 5 物理背键 (L4 / R4) 推荐映射：</strong><br>
              • 全局热键 <code>Ctrl + Alt + O</code>：切换右侧 QAM 快捷菜单（可在 GPD WinControls 将 <strong>L4</strong> 设为该热键）<br>
              • 全局热键 <code>Ctrl + Alt + I</code>：向游戏注入 <code>Insert</code> 呼出 OptiScaler 菜单（可将 <strong>R4</strong> 设为该热键）<br>
              • 全局热键 <code>Ctrl + Shift + O</code>：唤起全屏主控制台
            </div>
          </div>
        </div>
      </div>
    `;
  }

  // =========================================================================
  // Event Wiring
  // =========================================================================
  function bindRunningControls(root, game) {
    const jumpLib = root.querySelector("#btnJumpLibrary");
    if (jumpLib) jumpLib.onclick = () => switchTab("library");

    root.querySelectorAll("[data-preset]").forEach((btn) => {
      btn.onclick = () => selectPreset(btn.dataset.preset);
    });

    const cfg = state.draftConfig;
    const bindSelect = (id, field) => {
      const el = root.querySelector(id);
      if (el && cfg) el.onchange = () => { cfg[field] = el.value; };
    };
    bindSelect("#cfgProxyDll", "proxyDllName");
    bindSelect("#cfgFgType", "fgType");
    bindSelect("#cfgDx12", "dx12Upscaler");
    bindSelect("#cfgDx11", "dx11Upscaler");

    const bindToggle = (id, field) => {
      const el = root.querySelector(id);
      if (el && cfg) el.onclick = () => { cfg[field] = !cfg[field]; render(); };
    };
    bindToggle("#toggleOptiPatcher", "enableOptiPatcher");
    bindToggle("#toggleFakenvapi", "enableFakenvapi");
    bindToggle("#toggleSpoofDxgi", "spoofDxgi");

    if (!game) return;

    const btnRestart = root.querySelector("#btnInjectRestart");
    if (btnRestart) btnRestart.onclick = () => performInject(game, true);

    const btnInjectOnly = root.querySelector("#btnInjectOnly");
    if (btnInjectOnly) btnInjectOnly.onclick = () => performInject(game, false);

    const btnInsert = root.querySelector("#btnSendInsert");
    if (btnInsert) btnInsert.onclick = () => rpc("sendInsertKey").catch((e) => showToast(e.message, "err"));

    const btnRestore = root.querySelector("#btnRestoreGame");
    if (btnRestore) btnRestore.onclick = () => performRestore(game);

    const btnLog = root.querySelector("#btnInspectLog");
    if (btnLog) btnLog.onclick = () => inspectGameLogModal(game);

    const btnOpenDir = root.querySelector("#btnOpenTargetFolder");
    if (btnOpenDir) btnOpenDir.onclick = () => rpc("openFolder", { path: game.targetRenderDirectory });

    const btnSwitchTarget = root.querySelector("#btnSwitchRunningTarget");
    if (btnSwitchTarget) btnSwitchTarget.onclick = () => switchTab("library");
  }

  function bindLibraryControls(root) {
    root.querySelectorAll("[data-filter]").forEach((b) => {
      b.onclick = () => { state.filterPlatform = b.dataset.filter; render(); };
    });

    const search = root.querySelector("#libSearch");
    if (search) {
      search.oninput = () => {
        state.searchQuery = search.value;
        render();
        const newSearch = document.getElementById("libSearch");
        if (newSearch) {
          newSearch.focus();
          newSearch.setSelectionRange(newSearch.value.length, newSearch.value.length);
        }
      };
    }

    root.querySelector("#btnScanGames").onclick = async () => {
      showToast("正在扫描多平台游戏库...", "ok");
      await rpc("scanGames");
    };
    root.querySelector("#btnAddExe").onclick = () => rpc("addCustomGameByBrowse", { pickFolder: false });
    root.querySelector("#btnAddFolder").onclick = () => rpc("addCustomGameByBrowse", { pickFolder: true });

    root.querySelectorAll("[data-quick-select]").forEach((btn) => {
      btn.onclick = async (e) => {
        e.stopPropagation();
        await rpc("selectRunningFallback", { gameId: btn.dataset.quickSelect });
        switchTab("running");
      };
    });

    root.querySelectorAll("[data-quick-launch]").forEach((btn) => {
      btn.onclick = async (e) => {
        e.stopPropagation();
        try {
          await rpc("launchGame", { gameId: btn.dataset.quickLaunch });
          showToast("已拉起游戏", "ok");
        } catch (err) {
          showToast(err.message, "err");
        }
      };
    });

    root.querySelectorAll("[data-game-card]").forEach((card) => {
      card.onclick = async () => {
        await rpc("selectRunningFallback", { gameId: card.dataset.gameCard });
        switchTab("running");
      };
    });
  }

  function bindComponentsControls(root) {
    root.querySelector("#btnRefreshCatalog").onclick = async () => {
      try {
        showToast("正在从 GitHub 获取组件最新发布信息...", "ok");
        const comps = await rpc("refreshComponents");
        state.snapshot.components = comps;
        render();
        showToast("已刷新组件列表", "ok");
      } catch (err) {
        showToast(err.message, "err");
      }
    };

    root.querySelectorAll("[data-install-comp]").forEach((btn) => {
      btn.onclick = async () => {
        try {
          await rpc("installComponent", { slot: btn.dataset.installComp });
          showToast("组件安装/更新完成！", "ok");
        } catch (err) {
          showToast(err.message, "err");
        }
      };
    });

    root.querySelectorAll("[data-import-comp]").forEach((btn) => {
      btn.onclick = async () => {
        try {
          const res = await rpc("importComponentFile", { slot: btn.dataset.importComp });
          if (res) showToast("本地组件导入成功！", "ok");
        } catch (err) {
          showToast(err.message, "err");
        }
      };
    });

    root.querySelectorAll("[data-set-active-slot]").forEach((btn) => {
      btn.onclick = async () => {
        const updated = { ...state.snapshot.settings, activeOptiScalerSlot: btn.dataset.setActiveSlot };
        await rpc("saveSettings", updated);
        showToast("已切换默认注入内核版本", "ok");
      };
    });

    root.querySelector("#btnImportCustomExt").onclick = async () => {
      try {
        const res = await rpc("importCustomExtension");
        if (res) showToast(`已导入扩展文件: ${res.fileName}`, "ok");
      } catch (err) {
        showToast(err.message, "err");
      }
    };

    root.querySelectorAll("[data-toggle-ext]").forEach((btn) => {
      btn.onclick = () => rpc("toggleCustomExtension", {
        fileName: btn.dataset.toggleExt,
        enabled: btn.dataset.extEnabled !== "true"
      });
    });

    root.querySelectorAll("[data-del-ext]").forEach((btn) => {
      btn.onclick = () => rpc("deleteCustomExtension", { fileName: btn.dataset.delExt });
    });
  }

  function bindSettingsControls(root) {
    const saveUpdatedSettings = async (patch) => {
      const updated = { ...state.snapshot.settings, ...patch };
      await rpc("saveSettings", updated);
      showToast("设置已保存", "ok");
    };

    root.querySelector("#setAutoInject").onclick = () =>
      saveUpdatedSettings({ backgroundAutoInjectEnabled: !state.snapshot.settings.backgroundAutoInjectEnabled });

    root.querySelector("#setFullscreen").onclick = async () => {
      const next = !state.snapshot.settings.launchConsoleFullscreen;
      await saveUpdatedSettings({ launchConsoleFullscreen: next });
      await rpc("toggleFullscreen");
    };

    root.querySelector("#setMinimizedTray").onclick = () =>
      saveUpdatedSettings({ startMinimizedToTray: !state.snapshot.settings.startMinimizedToTray });

    root.querySelector("#setWinStartup").onclick = () =>
      saveUpdatedSettings({ launchAtWindowsStartup: !state.snapshot.settings.launchAtWindowsStartup });

    const defPresetEl = root.querySelector("#setDefaultPreset");
    defPresetEl.onchange = () => saveUpdatedSettings({ defaultPresetId: defPresetEl.value });

    const holdEl = root.querySelector("#setInsertHoldMs");
    holdEl.onchange = () => saveUpdatedSettings({ insertHoldDurationMs: Number(holdEl.value) || 65 });

    root.querySelector("#btnRunAutoInjectNow").onclick = async () => {
      const events = await rpc("runAutoInjectNow");
      showToast(events.length > 0 ? `已为 ${events.filter((e) => e.success).length} 款安全游戏完成预注入` : "当前没有新增需要预注入的合规游戏", "ok");
    };

    root.querySelector("#btnRecQamCombo").onclick = async () => {
      state.recordingTarget = "qam";
      render();
      await rpc("beginRecordGamepadCombo");
    };

    root.querySelector("#btnRecInsertCombo").onclick = async () => {
      state.recordingTarget = "insert";
      render();
      await rpc("beginRecordGamepadCombo");
    };

    const startHotkeyRec = (fieldName) => {
      state.recordingHotkeyField = fieldName;
      render();
    };
    root.querySelector("#btnHotkeyQam").onclick = () => startHotkeyRec("qamHotkey");
    root.querySelector("#btnHotkeyInsert").onclick = () => startHotkeyRec("insertHotkey");
    root.querySelector("#btnHotkeyConsole").onclick = () => startHotkeyRec("consoleHotkey");
  }

  window.addEventListener("keydown", async (e) => {
    if (!state.recordingHotkeyField) return;
    if (e.key === "Control" || e.key === "Alt" || e.key === "Shift" || e.key === "Meta") return;

    e.preventDefault();
    e.stopPropagation();

    if (e.key === "Escape") {
      state.recordingHotkeyField = null;
      render();
      return;
    }

    let modifiers = 0;
    const parts = [];
    if (e.ctrlKey) { modifiers |= 0x0002; parts.push("Ctrl"); }
    if (e.altKey) { modifiers |= 0x0001; parts.push("Alt"); }
    if (e.shiftKey) { modifiers |= 0x0004; parts.push("Shift"); }

    const vk = e.keyCode || 0;
    if (vk === 0) return;

    const keyLabel = e.key.length === 1 ? e.key.toUpperCase() : e.key;
    parts.push(keyLabel);

    const field = state.recordingHotkeyField;
    state.recordingHotkeyField = null;
    const binding = {
      modifiers,
      virtualKey: vk,
      displayText: parts.join(" + ")
    };

    await rpc("saveSettings", { ...state.snapshot.settings, [field]: binding });
    showToast(`全局热键已更新为: ${binding.displayText}`, "ok");
  }, true);

  async function onGamepadComboRecorded(buttons) {
    const target = state.recordingTarget;
    state.recordingTarget = null;
    if (!target || !buttons || buttons.length === 0) {
      render();
      return;
    }

    const patch = target === "qam"
      ? { qamGamepadCombo: { buttons } }
      : { insertGamepadCombo: { buttons } };
    await rpc("saveSettings", { ...state.snapshot.settings, ...patch });
    showToast(`组合键已更新为: ${buttons.join(" + ")}`, "ok");
  }

  // Static Header Bindings
  document.querySelectorAll(".tab-btn").forEach((btn) => {
    btn.onclick = () => switchTab(btn.dataset.tab);
  });

  document.getElementById("btnToggleFullscreen").onclick = () => rpc("toggleFullscreen");
  document.getElementById("btnMinimizeTray").onclick = () => rpc("minimizeToTray");
  document.getElementById("btnQamOpenConsole").onclick = () => rpc("showConsole");
  document.getElementById("btnQamClose").onclick = () => rpc("hideQam");

  // Boot
  rpc("getSnapshot")
    .then(applySnapshot)
    .catch((err) => showToast(`初始化失败: ${err.message}`, "err"));
})();
