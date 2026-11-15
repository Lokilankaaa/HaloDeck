(() => {
  "use strict";

  // Global error tracking for GUI self-verify diagnostics
  window.__hoErrors = [];
  window.addEventListener("error", (e) => window.__hoErrors.push(e.message || String(e)));
  window.addEventListener("unhandledrejection", (e) => window.__hoErrors.push(String(e.reason || e)));

  const IS_QAM = new URLSearchParams(window.location.search).get("mode") === "qam";
  document.body.classList.toggle("mode-qam", IS_QAM);
  document.body.classList.toggle("mode-console", !IS_QAM);

  const CONSOLE_TABS = ["tune", "system", "library", "components", "shortcuts"];
  const QAM_TABS = ["tune", "system", "compat", "games"];

  const DEFAULT_CONFIG = {
    proxyDllName: "auto",
    srMode: "fsr4_int8",
    dx12Upscaler: "ffx",
    dx11Upscaler: "ffx_12",
    vulkanUpscaler: "ffx_12",
    fgType: "xefg",
    fgInputMode: "auto",
    enableOptiPatcher: true,
    enableFakenvapi: true,
    enableCustomDlls: true,
    spoofDxgi: true,
    dxgiVramGb: 10,
    spoofedVendorId: "auto",
    spoofedDeviceId: "auto",
    spoofedGpuName: "",
    streamlineSpoofing: true,
    shortcutKeyHex: "0x2D",
    enableFsr4Int8Hint: true,
    fsr4Watermark: false,
    framerateLimit: 0,
    vsyncMode: "on"
  };

  const state = {
    tab: "tune",
    qamTab: "tune",
    snapshot: null,
    activeGameId: null,
    selectedPresetId: "strix-halo-quality",
    _userModifiedConfig: false,
    config: { ...DEFAULT_CONFIG },
    advancedOpen: false,
    searchQuery: "",
    libraryFilter: "all",
    focusIndex: 0,
    recordingTarget: null,
    progressBySlot: {},
    busy: false
  };

  // ---------------- WebView2 JSON-RPC ----------------
  let reqSeq = 0;
  const pendingRpc = new Map();

  function rpc(method, payload = {}) {
    return new Promise((resolve, reject) => {
      if (!window.chrome || !window.chrome.webview) {
        reject(new Error("WebView2 通道尚未就绪"));
        return;
      }
      const id = `r_${++reqSeq}`;
      pendingRpc.set(id, { resolve, reject });
      window.chrome.webview.postMessage({ id, method, payload });
    });
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", (event) => {
      const msg = event.data;
      if (!msg) return;
      if (msg.type === "response" && msg.id && pendingRpc.has(msg.id)) {
        const { resolve, reject } = pendingRpc.get(msg.id);
        pendingRpc.delete(msg.id);
        if (msg.ok) resolve(msg.result);
        else reject(new Error(msg.error || "操作失败"));
      } else if (msg.type === "event") {
        handleHostEvent(msg.eventName, msg.data);
      }
    });
  }

  function handleHostEvent(name, data) {
    if (name === "stateChanged") {
      applySnapshot(data);
    } else if (name === "componentProgress" && data?.slot) {
      state.progressBySlot[data.slot] = data.percent;
      if (state.tab === "components") render();
    } else if (name === "gamepadStatus") {
      const count = typeof data?.connectedCount === "number" ? data.connectedCount : data?.connected;
      if (typeof count === "number") updateControllerChip(count);
    } else if (name === "gamepadComboRecorded" && Array.isArray(data?.buttons)) {
      finishGamepadComboRecord(data.buttons);
    } else if (name === "gamepadAction" && data?.action) {
      const expectedTarget = IS_QAM ? "qam" : "console";
      if (!data.target || data.target === expectedTarget) {
        handleGamepadAction(data.action);
      }
    } else if (name === "systemTelemetry" && data) {
      if (state.snapshot) state.snapshot.systemControl = data;
      patchLiveTelemetryDom(data);
    } else if (name === "toast" && data?.message) {
      showToast(data.message, data.kind || "info");
    }
  }

  function formatPowerBadge(sys) {
    if (!sys) return "--%";
    if (!sys.hasBattery) return "AC 直连 (无电池)";
    const watts = typeof sys.batteryPowerWatts === "number" && sys.batteryPowerWatts > 0
      ? ` ${sys.batteryPowerWatts.toFixed(1)}W`
      : "";
    if (sys.isAcOnline) {
      return sys.isCharging ? `${sys.batteryPercent}% 充电${watts}` : `${sys.batteryPercent}% AC`;
    }
    return `${sys.batteryPercent}%${watts ? ` ·${watts}` : " 电池"}`;
  }

  function formatResShort(resStr) {
    if (!resStr) return "1080p";
    if (resStr.startsWith("1920x")) return "1080p";
    if (resStr.startsWith("1600x")) return "900p";
    if (resStr.startsWith("1280x")) return "720p";
    return resStr;
  }

  function patchLiveTelemetryDom(sys) {
    if (!sys) return;
    const topLabel = document.getElementById("topSysLabel");
    if (topLabel) {
      topLabel.textContent = `${formatResShort(`${sys.screenWidth}x${sys.screenHeight}`)} · ${sys.refreshRateHz || "--"}Hz · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`;
    }

    const qamQuick = document.getElementById("btnQamJumpSys");
    if (qamQuick) {
      qamQuick.textContent = `${sys.refreshRateHz || "--"}Hz · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`;
      qamQuick.classList.toggle("warn", (sys.refreshRateHz || 60) <= 60);
    }

    const powerVal = document.getElementById("sysPowerSummary");
    if (powerVal) powerVal.textContent = formatPowerBadge(sys);

    const powerSub = document.getElementById("sysPowerSub");
    if (powerSub) {
      powerSub.textContent = sys.hasBattery && sys.batteryRemainingWh > 0
        ? `剩余容量 ${sys.batteryRemainingWh.toFixed(1)} Wh`
        : (sys.isAcOnline ? "外接电源供电中" : "便携电池供电");
    }

    const cpuVal = document.getElementById("sysCpuLoadVal");
    if (cpuVal) cpuVal.textContent = `${sys.cpuUsagePercent ?? 0}%`;

    const ramVal = document.getElementById("sysRamVal");
    if (ramVal && sys.totalRamGb > 0) ramVal.textContent = `${sys.usedRamGb} / ${sys.totalRamGb} GB`;

    const dispVal = document.getElementById("sysDisplaySummary");
    if (dispVal && sys.screenWidth > 0) {
      dispVal.textContent = `${sys.screenWidth}×${sys.screenHeight} @ ${sys.refreshRateHz}Hz`;
    }
  }

  function formatConfigSummary(cfg, compact = false) {
    if (!cfg) return compact ? "已注入" : "OptiScaler 已生效";
    const srLabels = compact
      ? { fsr4_int8: "FSR4", fsr4_int8_lite: "4 Lite", xess: "XeSS", fsr31: "FSR3.1" }
      : { fsr4_int8: "FSR 4.1.2", fsr4_int8_lite: "FSR 4.1.2 Lite", xess: "XeSS 2.0", fsr31: "FSR 3.1" };
    const fgLabels = { xefg: "XeFG", optifg: "OptiFG", nukems: "Nukem", nofg: "无插帧" };
    const sr = srLabels[cfg.srMode] || cfg.srMode || "FSR";
    const fg = fgLabels[cfg.fgType] || cfg.fgType || "XeFG";
    if (compact) {
      const cap = cfg.framerateLimit > 0 ? `·${cfg.framerateLimit}` : "";
      return `${sr}+${fg}${cap}`;
    }
    const parts = [sr, fg];
    if (cfg.framerateLimit > 0) parts.push(`${cfg.framerateLimit}帧`);
    if (cfg.vsyncMode === "on") parts.push("防撕裂");
    else if (cfg.vsyncMode === "off") parts.push("关垂直同步");
    return parts.join(" · ");
  }

  function syncConfigFromActiveGame(snap) {
    const active = snap?.runningGame || snap?.activeInspectGame || (snap?.games || [])[0] || null;
    const nextGameId = active?.id || null;
    const gameChanged = nextGameId !== state.activeGameId;

    if (gameChanged) {
      state.activeGameId = nextGameId;
      state._userModifiedConfig = false;
    }

    if (!gameChanged && state._userModifiedConfig) {
      return;
    }

    if (active?.injection?.isInjected && active.injection.appliedConfig) {
      state.config = { ...DEFAULT_CONFIG, ...active.injection.appliedConfig };
      state.selectedPresetId = active.injection.presetId || "custom";
    } else {
      const presets = snap?.presets || [];
      const defaultId = snap?.settings?.defaultPresetId;
      const preset = presets.find((x) => x.id === defaultId) || presets[0];
      if (preset) {
        state.selectedPresetId = preset.id;
        state.config = { ...DEFAULT_CONFIG, ...preset.config };
      } else {
        state.selectedPresetId = "strix-halo-quality";
        state.config = { ...DEFAULT_CONFIG };
      }
    }
  }

  function applySnapshot(snap) {
    state.snapshot = snap;
    syncConfigFromActiveGame(snap);
    updateControllerChip(snap?.connectedControllers || 0);
    patchLiveTelemetryDom(snap?.systemControl);
    if (snap?.hardwareProfile?.deviceName) {
      const el = document.getElementById("deviceTag");
      if (el) el.textContent = "GPD WIN 5 · 8060S";
    }
    render();
  }

  function updateControllerChip(count) {
    const chip = document.getElementById("controllerChip");
    const label = document.getElementById("controllerLabel");
    if (!chip || !label) return;
    chip.classList.toggle("connected", count > 0);
    label.textContent = count > 0 ? `已连接手柄 (${count})` : "键鼠 / 触摸";
  }

  function showToast(message, kind = "info") {
    const stack = document.getElementById("toastStack");
    if (!stack) return;
    const el = document.createElement("div");
    el.className = `toast-item ${kind}`;
    el.textContent = message;
    stack.appendChild(el);
    setTimeout(() => el.remove(), 3400);
  }

  // ---------------- Helpers ----------------
  function esc(str) {
    return String(str ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function getActiveGame() {
    const snap = state.snapshot;
    if (!snap) return null;
    if (snap.runningGame) return snap.runningGame;
    if (snap.activeInspectGame) return snap.activeInspectGame;
    return (snap.games || [])[0] || null;
  }

  function syncAmbientBackdrop(game) {
    const img = document.getElementById("ambientHeroImg");
    if (!img) return;
    const src = game?.heroBlurImageUrl || game?.heroImageUrl || game?.coverImageUrl || "";
    if (!src) {
      img.classList.add("is-empty");
      img.removeAttribute("src");
      return;
    }
    if (img.getAttribute("src") !== src) {
      img.src = src;
    }
    img.classList.remove("is-empty");
  }

  function getActiveKernelInfo() {
    const slot = state.snapshot?.settings?.activeOptiScalerSlot || "OptiScalerNightly";
    const comps = state.snapshot?.components || [];
    return comps.find((c) => c.slot === slot) || { slot, displayName: slot, installedVersion: "" };
  }

  function syncSrModeConfig(srMode) {
    state.selectedPresetId = "custom";
    state._userModifiedConfig = true;
    state.config.srMode = srMode;
    switch (srMode) {
      case "fsr4_int8":
        state.config.dx12Upscaler = "ffx";
        state.config.dx11Upscaler = "ffx_12";
        state.config.vulkanUpscaler = "ffx_12";
        state.config.enableFsr4Int8Hint = true;
        break;
      case "fsr4_int8_lite":
        state.config.dx12Upscaler = "ffx";
        state.config.dx11Upscaler = "ffx_12";
        state.config.vulkanUpscaler = "ffx_12";
        state.config.enableFsr4Int8Hint = true;
        break;
      case "xess":
        state.config.dx12Upscaler = "xess";
        state.config.dx11Upscaler = "xess_12";
        state.config.vulkanUpscaler = "xess_12";
        state.config.enableFsr4Int8Hint = false;
        break;
      case "fsr31":
        state.config.dx12Upscaler = "ffx";
        state.config.dx11Upscaler = "ffx_12";
        state.config.vulkanUpscaler = "ffx_12";
        state.config.enableFsr4Int8Hint = false;
        break;
    }
  }

  function applyPreset(presetId) {
    const preset = (state.snapshot?.presets || []).find((p) => p.id === presetId);
    if (!preset) return;
    state.selectedPresetId = preset.id;
    state._userModifiedConfig = true;
    state.config = { ...state.config, ...preset.config };
    render();
  }

  function svgIcon(kind) {
    switch (kind) {
      case "bolt":
        return `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon></svg>`;
      case "play":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><polygon points="6 4 20 12 6 20 6 4"></polygon></svg>`;
      case "restart":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"></polyline><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"></path></svg>`;
      case "undo":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7v6h6"></path><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"></path></svg>`;
      case "folder":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path></svg>`;
      case "doc":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="8" y1="13" x2="16" y2="13"></line><line x1="8" y1="17" x2="13" y2="17"></line></svg>`;
      case "search":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>`;
      case "menu":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="16" rx="2"></rect><line x1="7" y1="9" x2="17" y2="9"></line><line x1="7" y1="14" x2="13" y2="14"></line></svg>`;
      case "chevron":
        return `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>`;
      default:
        return "";
    }
  }

  function renderCapabilities(game) {
    const caps = game?.nativeUpscalers || {};
    const tags = [];
    if (caps.hasDlss) tags.push(`<span class="pill-badge blue">DLSS</span>`);
    if (caps.hasDlssG) tags.push(`<span class="pill-badge blue">DLSS-G</span>`);
    if (caps.hasXeSs) tags.push(`<span class="pill-badge">XeSS</span>`);
    if (caps.hasFsr) tags.push(`<span class="pill-badge">FSR</span>`);
    const ac = game?.antiCheat;
    if (ac && ac.riskLevel === "HighRisk") {
      tags.push(`<span class="pill-badge danger">反作弊拦截 · ${esc(ac.detectedSystems?.[0] || "AC")}</span>`);
    } else if (ac && ac.riskLevel === "Warning") {
      tags.push(`<span class="pill-badge warn">存在风控提示</span>`);
    } else {
      tags.push(`<span class="pill-badge">离线可注入</span>`);
    }
    return tags.join("");
  }

  function renderSegmented(attrName, options, currentVal, extraClass = "") {
    return `
      <div class="seg-group ${extraClass}">
        ${options.map((opt) => `
          <button class="seg-btn ${currentVal === opt.value ? "active" : ""}"
                  ${attrName}="${esc(opt.value)}"
                  data-focusable="true">${esc(opt.label)}</button>
        `).join("")}
      </div>
    `;
  }

  function renderSwitch(id, checked) {
    return `
      <button id="${id}" class="cw-switch ${checked ? "on" : ""}" role="switch" aria-checked="${checked}" data-focusable="true">
        <span class="cw-switch-thumb"></span>
      </button>
    `;
  }

  function renderStepSlider({ idPrefix, value, displayText = null, disabled = false, muted = false, muteBtnId = null }) {
    const pct = Math.max(0, Math.min(100, Number(value) || 0));
    const text = displayText || (muted ? "静音" : `${pct}%`);
    return `
      <div class="step-slider">
        <button id="${idPrefix}Dec" class="step-btn" data-focusable="true" ${disabled ? "disabled" : ""} aria-label="减小">−</button>
        <div class="step-track" data-slider-track="${esc(idPrefix)}" role="slider" aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100">
          <div class="step-fill" style="width:${muted ? 0 : pct}%;"></div>
          <span class="step-val">${esc(text)}</span>
        </div>
        <button id="${idPrefix}Inc" class="step-btn" data-focusable="true" ${disabled ? "disabled" : ""} aria-label="增大">+</button>
        ${muteBtnId ? `<button id="${muteBtnId}" class="step-btn ${muted ? "active" : ""}" data-focusable="true" title="静音切换">${muted ? "静音" : "🔊"}</button>` : ""}
      </div>
    `;
  }

  function renderKeycaps(items) {
    if (!items || !items.length) return `<span class="keycap-pill">未设置</span>`;
    return `<div class="keycap-combo">${items.map((k) => `<span class="keycap-pill">${esc(k)}</span>`).join(`<span class="keycap-plus">+</span>`)}</div>`;
  }

  // ---------------- Console View Renderers ----------------
  function renderTuneView() {
    const snap = state.snapshot;
    const games = snap?.games || [];
    const active = getActiveGame();
    const presets = snap?.presets || [];
    const kernel = getActiveKernelInfo();
    const cfg = state.config;

    const inj = active?.injection;
    const isUninjectedPreview = !inj?.isInjected && !state._userModifiedConfig;
    const injBadge = inj?.isInjected
      ? `<span class="pill-badge blue">已注入 · ${esc(formatConfigSummary(inj.appliedConfig || cfg, true))}</span>`
      : `<span class="pill-badge">原生未注入</span>`;

    const quickTargets = games.slice(0, 4);
    const hasCustomAdvanced = cfg.fgInputMode !== "auto" || cfg.proxyDllName !== "auto" || cfg.fsr4Watermark;
    const kernelShort = kernel.slot === "OptiScalerNightly" ? "Nightly v10+" : "Stable v0.9.4";

    return `
      <div class="tune-layout">
        <!-- Left: Active Game Hero & Action Dock -->
        <div class="tune-left">
          <div class="hero-card">
            <div class="hero-banner">
              ${active && (active.heroImageUrl || active.coverImageUrl)
                ? `<img class="hero-banner-img" src="${esc(active.heroImageUrl || active.coverImageUrl)}" alt="${esc(active.title)}" />`
                : `<div class="hero-banner-img" style="background:linear-gradient(135deg,#1c2330,#101319)"></div>`}
              <div class="hero-banner-scrim"></div>
              <div class="hero-top-pills">
                ${active?.isCurrentlyRunning
                  ? `<span class="pill-badge live">正在运行</span>`
                  : `<span class="pill-badge">${active ? esc(active.platform) : "未发现目标"}</span>`}
                ${injBadge}
              </div>
              <div class="hero-title-row">
                ${active?.logoImageUrl
                  ? `<img class="hero-logo" src="${esc(active.logoImageUrl)}" alt="${esc(active.title)}" onerror="this.style.display='none';this.nextElementSibling.style.display='block';" /><h2 class="hero-title" style="display:none">${esc(active?.title)}</h2>`
                  : `<h2 class="hero-title">${esc(active?.title || "请扫描或添加游戏目录")}</h2>`}
              </div>
            </div>

            <div class="hero-body">
              <div class="hero-meta-line" title="${esc(active?.targetRenderDirectory || "")}">
                ${active ? `${esc(active.engine)} · ${esc(active.architecture)} · ${esc(active.targetRenderDirectory)}` : "点击右侧游戏库选择目标或导入本地 EXE"}
              </div>
              <div class="cap-strip">
                ${active ? renderCapabilities(active) : ""}
              </div>

              <div class="target-switcher">
                <div class="target-switcher-head">
                  <span>快速切换注入目标</span>
                  <button id="btnAddExeInline" class="btn btn-sm" data-focusable="true">+ 添加 EXE</button>
                </div>
                <div class="target-chips">
                  ${quickTargets.map((g) => `
                    <button class="target-chip ${active && g.id === active.id ? "active" : ""}"
                            data-select-game="${esc(g.id)}"
                            title="${esc(g.title)}"
                            data-focusable="true">${esc(g.title)}</button>
                  `).join("")}
                  ${games.length > 4 ? `<button id="btnMoreGames" class="target-chip" data-focusable="true">全部 (${games.length})</button>` : ""}
                </div>
              </div>
            </div>
          </div>

          <div class="action-dock">
            <button id="btnInject" class="btn btn-primary" data-focusable="true" ${!active || state.busy ? "disabled" : ""}>
              ${svgIcon("bolt")}
              <span>${inj?.isInjected ? "应用当前设置并重新注入" : "一键注入推荐配置"}</span>
            </button>

            <div class="action-row-2">
              ${active?.isCurrentlyRunning
                ? `<button id="btnInjectRestart" class="btn" data-focusable="true" ${state.busy ? "disabled" : ""}>${svgIcon("restart")}<span>注入并重开</span></button>`
                : `<button id="btnLaunchGame" class="btn" data-focusable="true" ${!active || state.busy ? "disabled" : ""}>${svgIcon("play")}<span>启动游戏</span></button>`}
              <button id="btnRestore" class="btn btn-danger-soft" data-focusable="true" ${!inj?.isInjected || state.busy ? "disabled" : ""}>
                ${svgIcon("undo")}
                <span>还原纯净原版</span>
              </button>
            </div>

            <div class="action-row-3">
              <button id="btnSendInsert" class="btn btn-sm" data-focusable="true">${svgIcon("menu")}<span>游戏内菜单</span></button>
              <button id="btnInspectLog" class="btn btn-sm" data-focusable="true" ${!active ? "disabled" : ""}>${svgIcon("doc")}<span>查看 INI</span></button>
              <button id="btnOpenFolder" class="btn btn-sm" data-focusable="true" ${!active ? "disabled" : ""}>${svgIcon("folder")}<span>游戏目录</span></button>
            </div>
          </div>
        </div>

        <!-- Right: ClawTweaks Segmented Controls -->
        <div class="tune-right">
          ${state.snapshot?.memoryHealth?.lowCommitHeadroom ? `
            <div class="mem-alert-card">
              <div class="mem-alert-copy">
                <div class="mem-alert-title">检测到高 UMA 显存拆分：可用内存 ${esc(state.snapshot.memoryHealth.totalRamGb)} GB · 提交上限 ${esc(state.snapshot.memoryHealth.commitLimitGb)} GB</div>
                <div class="mem-alert-sub">大型 DX12 游戏（如仁王3）易因虚拟内存耗尽报 Insufficient Memory。已默认将伪装显存限制为 10GB，建议扩容系统分页文件。</div>
              </div>
              <button class="mem-alert-btn" id="btnExpandPagefile" data-nav="action">扩容至 24-40GB</button>
            </div>` : ""}
          <div>
            <div class="section-label">
              <span>主流预设方案 (${esc(kernelShort)})</span>
              <span>点击自动填入下方参数</span>
            </div>
            <div class="preset-grid ${isUninjectedPreview ? "uninjected-preview" : ""}">
              ${presets.map((p) => `
                <button class="preset-card ${state.selectedPresetId === p.id ? "active" : ""}"
                        data-preset="${esc(p.id)}"
                        data-focusable="true">
                  <div class="preset-card-head">
                    <span class="preset-name">${esc(p.name)}</span>
                    <span class="preset-tag">${esc(p.subtitle.split("·")[0].trim())}</span>
                  </div>
                  <div class="preset-sub">${esc(p.recommendedFor)}</div>
                </button>
              `).join("")}
            </div>
          </div>

          <div>
            <div class="section-label">
              <span>超分辨率与帧生成设置</span>
              <span>${isUninjectedPreview ? "当前原生未注入 · 虚线框为待注入默认预览" : `当前生效：${esc(formatConfigSummary(inj?.appliedConfig || cfg))}`}</span>
            </div>
            <div class="cw-group ${isUninjectedPreview ? "uninjected-preview" : ""}">
              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">超分辨率模型 (SR)</span>
                  <span class="cw-row-sub">GPD Win 5 推荐 FSR 4.1.2 INT8；需要更高帧率可选 Lite</span>
                </div>
                ${renderSegmented("data-sr-mode", [
                  { value: "fsr4_int8", label: "FSR 4.1.2" },
                  { value: "fsr4_int8_lite", label: "4.1.2 Lite" },
                  { value: "xess", label: "XeSS 2.0" },
                  { value: "fsr31", label: "FSR 3.1" }
                ], cfg.srMode)}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">帧生成技术 (Frame Gen)</span>
                  <span class="cw-row-sub">仁王3 等原生 DLSS-G 游戏请选 XeFG（随游戏内帧生成开关热启停）；无 DLSS-G 游戏可选 OptiFG；配合小黄鸭请选关闭插帧</span>
                </div>
                ${renderSegmented("data-fg-type", [
                  { value: "xefg", label: "XeFG 帧生成" },
                  { value: "optifg", label: "OptiFG" },
                  { value: "nofg", label: "关闭插帧" }
                ], cfg.fgType)}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">平滑锁帧省电 (Reflex / XeLL)</span>
                  <span class="cw-row-sub">120Hz 屏开窗口化 VRR 推荐 90/116；未开 VRR 建议不限帧或 60 整除防抖动</span>
                </div>
                ${renderSegmented("data-fps-limit", [
                  { value: "0", label: "不限帧" },
                  { value: "60", label: "60 帧" },
                  { value: "90", label: "90 帧" },
                  { value: "116", label: "116 (VRR)" }
                ], String(cfg.framerateLimit || 0))}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">AMD Anti-Lag 2 低延迟</span>
                  <span class="cw-row-sub">通过 Fakenvapi 自动将游戏内 Reflex 映射为 RDNA Anti-Lag 2</span>
                </div>
                ${renderSwitch("swFakenvapi", !!cfg.enableFakenvapi)}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">画面防撕裂同步 (V-Sync / VRR)</span>
                  <span class="cw-row-sub">${snap?.vrrWindowedEnabled ? "已启用翻转防撕裂（同小黄鸭无撕裂）· Win11 窗口化 VRR 已开启" : "消除插帧撕裂（同小黄鸭）；检测到 Win11 窗口化 VRR 未开启，无边框易卡顿"}</span>
                </div>
                <div class="comp-actions">
                  ${!snap?.vrrWindowedEnabled ? `<button id="btnEnableVrr" class="btn btn-sm" data-focusable="true">开启 VRR</button>` : ""}
                  ${renderSegmented("data-vsync-mode", [
                    { value: "on", label: "防撕裂" },
                    { value: "auto", label: "跟随游戏" },
                    { value: "off", label: "强制关" }
                  ], cfg.vsyncMode || "on", "compact")}
                </div>
              </div>
            </div>
          </div>

          <!-- Collapsible Compatibility & Advanced Drawer -->
          <div class="cw-group">
            <button id="btnToggleAdvanced" class="cw-disclosure-btn" data-focusable="true">
              <span>兼容与底层注入参数 ${hasCustomAdvanced ? "(已自定义)" : ""}</span>
              <span style="display:inline-flex;align-items:center;gap:8px;">
                <span class="cw-disclosure-summary">
                  FGInput: ${esc(cfg.fgInputMode)} · Proxy: ${esc(cfg.proxyDllName)} · ${cfg.spoofDxgi ? "DXGI伪装" : "原生GPU"}
                </span>
                ${svgIcon("chevron")}
              </span>
            </button>
            ${state.advancedOpen ? `
              <div class="cw-disclosure-body">
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">插帧运动源 (FGInput)</span>
                    <span class="cw-row-sub">自动模式下若游戏原生含 DLSS-G，则优先接管减少 UI 鬼影</span>
                  </div>
                  ${renderSegmented("data-fg-input", [
                    { value: "auto", label: "智能自动" },
                    { value: "dlssg", label: "DLSS-G" },
                    { value: "upscaler", label: "通用光流" }
                  ], cfg.fgInputMode, "compact")}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">DLL 注入代理名</span>
                    <span class="cw-row-sub">通常保持 auto (dxgi.dll)；若冲突可改 version.dll 或 winmm.dll</span>
                  </div>
                  ${renderSegmented("data-proxy-dll", [
                    { value: "auto", label: "auto" },
                    { value: "dxgi.dll", label: "dxgi.dll" },
                    { value: "version.dll", label: "version.dll" },
                    { value: "winmm.dll", label: "winmm.dll" }
                  ], cfg.proxyDllName, "compact")}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">FSR 4.1.2 左上角验证水印</span>
                    <span class="cw-row-sub">调试开关：开启后可在游戏画面直观确认 FSR 4 模型版本</span>
                  </div>
                  ${renderSwitch("swWatermark", !!cfg.fsr4Watermark)}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">OptiPatcher ASI 运行时解锁</span>
                    <span class="cw-row-sub">在虚幻 4/5 与常见引擎中直接开放游戏内 DLSS 选项</span>
                  </div>
                  ${renderSwitch("swOptiPatcher", !!cfg.enableOptiPatcher)}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">DXGI 显卡身份伪装 (Dxgi=auto · 上限 10GB)</span>
                    <span class="cw-row-sub">隐藏 AMD RDNA 身份检查，配合 Streamline Spoofing 启用 XeFG/FSR4（已限制伪装显存 10GB；若游戏如仁王3仍报显存不足可尝试关闭）</span>
                  </div>
                  ${renderSwitch("swSpoofDxgi", !!cfg.spoofDxgi)}
                </div>
              </div>
            ` : ""}
          </div>
        </div>
      </div>
    `;
  }

  function renderSystemView() {
    const snap = state.snapshot;
    const sys = snap?.systemControl || {
      cpuName: "AMD RYZEN AI MAX+ 388 w/ Radeon 8060S",
      cpuUsagePercent: 0,
      totalRamGb: snap?.memoryHealth?.totalRamGb || 0,
      usedRamGb: snap?.memoryHealth?.totalRamGb
        ? Math.max(0, Number((snap.memoryHealth.totalRamGb - (snap.memoryHealth.availRamGb || 0)).toFixed(1)))
        : 0,
      screenWidth: 1920,
      screenHeight: 1080,
      refreshRateHz: 60,
      supportedRefreshRates: [60, 120],
      supportedResolutions: ["1920x1080", "1600x900", "1280x720"],
      brightnessSupported: false,
      brightnessPercent: 70,
      volumePercent: 50,
      isMuted: false,
      cpuBoostMode: "aggressive",
      isAcOnline: true,
      hasBattery: true,
      isCharging: false,
      batteryPercent: 100,
      batteryPowerWatts: null,
      batteryRemainingWh: 0
    };

    const hzOptions = (sys.supportedRefreshRates?.length ? sys.supportedRefreshRates : [60, 120])
      .map((hz) => ({ value: String(hz), label: `${hz}Hz` }));
    const resOptions = (sys.supportedResolutions?.length ? sys.supportedResolutions : ["1920x1080", "1600x900", "1280x720"])
      .map((r) => ({ value: r, label: formatResShort(r) }));
    const currentRes = `${sys.screenWidth || 1920}x${sys.screenHeight || 1080}`;

    return `
      <div class="tune-layout">
        <!-- Left Telemetry Column -->
        <aside class="tune-left">
          <div class="target-card" style="padding:14px;">
            <div class="section-label" style="margin-bottom:4px;">
              <span>Strix Halo 硬件遥测</span>
              <span class="pill-badge blue">${sys.isAcOnline ? "AC 外接电源" : "电池模式"}</span>
            </div>
            <div style="font-size:12.5px;font-weight:700;color:var(--text-primary);margin-bottom:2px;">${esc(sys.cpuName)}</div>
            <div class="sys-telemetry-grid">
              <div class="sys-stat-box">
                <span class="sys-stat-label">电源与电池</span>
                <span id="sysPowerSummary" class="sys-stat-value">${esc(formatPowerBadge(sys))}</span>
                <span id="sysPowerSub" class="sys-stat-sub">${sys.hasBattery && sys.batteryRemainingWh > 0 ? `剩余容量 ${sys.batteryRemainingWh.toFixed(1)} Wh` : (sys.isAcOnline ? "外接供电" : "电池供电")}</span>
              </div>
              <div class="sys-stat-box">
                <span class="sys-stat-label">屏幕输出</span>
                <span id="sysDisplaySummary" class="sys-stat-value">${sys.screenWidth}×${sys.screenHeight} @ ${sys.refreshRateHz}Hz</span>
                <span class="sys-stat-sub">${sys.refreshRateHz <= 60 ? "建议 120Hz 匹配 VRR 插帧" : "高刷顺滑状态"}</span>
              </div>
              <div class="sys-stat-box">
                <span class="sys-stat-label">CPU 占用率</span>
                <span id="sysCpuLoadVal" class="sys-stat-value">${sys.cpuUsagePercent ?? 0}%</span>
                <span class="sys-stat-sub">Zen 5 · 8060S APU</span>
              </div>
              <div class="sys-stat-box">
                <span class="sys-stat-label">统一内存已用</span>
                <span id="sysRamVal" class="sys-stat-value">${sys.usedRamGb} / ${sys.totalRamGb} GB</span>
                <span class="sys-stat-sub">含显存共享池</span>
              </div>
            </div>
          </div>

          <div class="target-card" style="padding:12px;">
            <div class="section-label" style="margin-bottom:4px;">
              <span>显示同步与背键提示</span>
            </div>
            <div class="cw-row" style="padding:6px 0;min-height:36px;">
              <div class="cw-row-copy">
                <span class="cw-row-title">Win11 窗口化 VRR 优化</span>
                <span class="cw-row-sub">${snap?.vrrWindowedEnabled ? "已开启 DXGI 翻转升级与 VRR 优化" : "建议开启以确保无边框 120Hz VRR 生效"}</span>
              </div>
              ${snap?.vrrWindowedEnabled
                ? `<span class="pill-badge blue">已开启</span>`
                : `<button id="btnSysEnableVrr" class="btn btn-sm" data-focusable="true">一键开启</button>`}
            </div>
            <div class="cw-row-sub" style="margin-top:4px;line-height:1.45;">
              L4/R4 物理背键映射会写入手柄板载芯片：用官方键鼠助手写一次组合键（如 <strong style="color:var(--text-primary);">Ctrl+Alt+O</strong> 呼出侧栏）后即可彻底关闭后台助手。
            </div>
          </div>

          <div style="margin-top:auto;display:flex;gap:8px;">
            <button id="btnSysGoTune" class="btn btn-primary" style="flex:1;" data-focusable="true">返回超分调优注入</button>
            <button id="btnSysRefresh" class="btn btn-sm" data-focusable="true">刷新遥测</button>
          </div>
        </aside>

        <!-- Right System Controls Column -->
        <section class="tune-right">
          <div>
            <div class="section-label">
              <span>显示器刷新率与掌机分辨率</span>
              <span>切换即生效，侧边栏自动贴合新分辨率</span>
            </div>
            <div class="cw-group">
              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">屏幕刷新率 (Refresh Rate)</span>
                  <span class="cw-row-sub">${sys.refreshRateHz <= 60 ? "当前处于 60Hz，开 XeFG 120 帧建议先切 120Hz" : `当前 ${sys.refreshRateHz}Hz 高刷新率输出`}</span>
                </div>
                ${renderSegmented("data-refresh-hz", hzOptions, String(sys.refreshRateHz), "compact")}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">系统全局分辨率</span>
                  <span class="cw-row-sub">900p / 720p 可显著降低 GPU 像素负载并延长续航</span>
                </div>
                ${renderSegmented("data-res-mode", resOptions, currentRes, "compact")}
              </div>
            </div>
          </div>

          <div>
            <div class="section-label">
              <span>亮度、音量与 Zen 5 处理器策略</span>
              <span>零内核驱动依赖 · Windows 原生直控</span>
            </div>
            <div class="cw-group">
              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">内置屏幕亮度</span>
                  <span class="cw-row-sub">${sys.brightnessSupported ? "支持 WMI 面板背光直控（步进 10%）" : "当前外接显示器或驱动未上报内置背光接口"}</span>
                </div>
                ${renderStepSlider({
                  idPrefix: "bright",
                  value: sys.brightnessPercent,
                  displayText: sys.brightnessSupported ? `${sys.brightnessPercent}%` : "外接屏",
                  disabled: !sys.brightnessSupported
                })}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">系统主音量与静音</span>
                  <span class="cw-row-sub">直连 Windows CoreAudio 默认输出设备</span>
                </div>
                ${renderStepSlider({
                  idPrefix: "vol",
                  value: sys.volumePercent,
                  muted: !!sys.isMuted,
                  muteBtnId: "btnToggleMute"
                })}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">Zen 5 CPU 睿频调度 (AC &amp; 电池双写)</span>
                  <span class="cw-row-sub">大型 GPU 瓶颈游戏建议关睿频或选高效平衡，省电并把功耗让给 8060S</span>
                </div>
                ${renderSegmented("data-cpu-boost", [
                  { value: "off", label: "关睿频省电" },
                  { value: "efficient", label: "高效平衡" },
                  { value: "aggressive", label: "满血积极" }
                ], sys.cpuBoostMode || "aggressive", "compact")}
              </div>
            </div>
          </div>
        </section>
      </div>
    `;
  }

  function renderLibraryView() {
    const games = state.snapshot?.games || [];
    const active = getActiveGame();
    const q = state.searchQuery.trim().toLowerCase();

    const filtered = games.filter((g) => {
      if (q && !g.title.toLowerCase().includes(q) && !g.targetRenderDirectory.toLowerCase().includes(q)) {
        return false;
      }
      if (state.libraryFilter === "upscaler") return !!g.nativeUpscalers?.hasAnyUpscaler;
      if (state.libraryFilter === "injected") return !!g.injection?.isInjected;
      if (state.libraryFilter === "running") return !!g.isCurrentlyRunning;
      return true;
    });

    return `
      <div class="library-shell">
        <div class="library-toolbar">
          <div class="toolbar-left">
            <div class="search-box">
              ${svgIcon("search")}
              <input id="libSearchInput" type="text" placeholder="搜索已安装游戏..." value="${esc(state.searchQuery)}" />
            </div>
            ${renderSegmented("data-lib-filter", [
              { value: "all", label: `全部 (${games.length})` },
              { value: "upscaler", label: "支持超分" },
              { value: "injected", label: "已注入" },
              { value: "running", label: "运行中" }
            ], state.libraryFilter, "compact")}
          </div>

          <div class="toolbar-left">
            <button id="btnScanGames" class="btn btn-sm" data-focusable="true">重新扫描目录</button>
            <button id="btnAddExeLib" class="btn btn-sm btn-primary" data-focusable="true">+ 添加本地 EXE</button>
          </div>
        </div>

        <div class="poster-grid">
          ${filtered.length === 0 ? `
            <div class="cw-group" style="grid-column: 1 / -1; padding: 28px; text-align:center; color:var(--text-secondary);">
              未找到匹配的游戏。点击右上角「重新扫描目录」或手动添加游戏主程序 (.exe)。
            </div>
          ` : filtered.map((g) => {
            const caps = g.nativeUpscalers || {};
            const capTag = caps.hasDlssG ? "DLSS-G" : caps.hasDlss ? "DLSS" : caps.hasXeSs ? "XeSS" : "常规";
            const statusText = g.injection?.isInjected ? "已注入 · 可配置" : g.isCurrentlyRunning ? "运行中" : `${g.engine}`;
            return `
              <button class="poster-card ${active && active.id === g.id ? "active" : ""}"
                      data-open-game="${esc(g.id)}"
                      data-focusable="true">
                ${g.coverImageUrl
                  ? `<img class="poster-img" src="${esc(g.coverImageUrl)}" alt="${esc(g.title)}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex';" />
                     <div class="poster-fallback" style="display:none"><span style="font-weight:700;color:#c7d2e4">${esc(g.title)}</span></div>`
                  : `<div class="poster-fallback"><span style="font-weight:700;color:#c7d2e4">${esc(g.title)}</span></div>`}
                <div class="poster-scrim"></div>
                <div class="poster-top">
                  <span class="pill-badge ${caps.hasDlss ? "blue" : ""}">${capTag}</span>
                  ${g.injection?.isInjected
                    ? `<span class="pill-badge blue">已注入</span>`
                    : g.isCurrentlyRunning ? `<span class="pill-badge live">运行中</span>` : ""}
                </div>
                <div class="poster-bottom">
                  <div class="poster-title">${esc(g.title)}</div>
                  <div class="poster-sub">${esc(statusText)}</div>
                </div>
              </button>
            `;
          }).join("")}
        </div>
      </div>
    `;
  }

  function renderComponentsView() {
    const snap = state.snapshot;
    const comps = snap?.components || [];
    const custom = snap?.customExtensions || [];
    const activeSlot = snap?.settings?.activeOptiScalerSlot || "OptiScalerNightly";

    return `
      <div class="page-scroll">
        <div>
          <div class="section-label"><span>默认注入内核版本</span></div>
          <div class="cw-group">
            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">OptiScaler 核心版本选择</span>
                <span class="cw-row-sub">Nightly v10+ 原生支持 FSR 4.1.2 INT8 / Lite 与新版 XeFG 参数；Stable 为 v0.9.4 保守版本</span>
              </div>
              ${renderSegmented("data-active-slot", [
                { value: "OptiScalerNightly", label: "Nightly v10+ (推荐)" },
                { value: "OptiScalerStable", label: "Stable v0.9.4" }
              ], activeSlot)}
            </div>
          </div>
        </div>

        <div>
          <div class="section-label">
            <span>官方核心组件缓存 (来自 GitHub Releases)</span>
            <button id="btnRefreshComps" class="btn btn-sm" data-focusable="true">检查最新版本</button>
          </div>
          <div class="cw-group">
            ${comps.map((c) => {
              const pct = state.progressBySlot[c.slot];
              const statusTag = c.isInstalled
                ? (c.hasUpdate ? `<span class="pill-badge warn">可更新 ${esc(c.latestVersion || "")}</span>` : `<span class="pill-badge live">已就绪</span>`)
                : `<span class="pill-badge">未安装</span>`;
              return `
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <div style="display:flex;align-items:center;gap:8px;">
                      <span class="cw-row-title">${esc(c.displayName)}</span>
                      ${statusTag}
                      ${c.slot === activeSlot ? `<span class="pill-badge blue">默认使用</span>` : ""}
                    </div>
                    <span class="cw-row-sub">
                      本地版本：${esc(c.installedVersion || "未安装")} · 在线版本：${esc(c.latestVersion || "待查询")}
                    </span>
                  </div>
                  <div class="comp-actions">
                    ${typeof pct === "number" && pct > 0 && pct < 100
                      ? `<div class="comp-progress"><div class="comp-progress-fill" style="width:${pct}%"></div></div>`
                      : ""}
                    <button class="btn btn-sm" data-import-comp="${esc(c.slot)}" data-focusable="true">导入离线包</button>
                    <button class="btn btn-sm btn-primary" data-install-comp="${esc(c.slot)}" data-focusable="true">
                      ${c.isInstalled ? (c.hasUpdate ? "更新" : "重装") : "下载安装"}
                    </button>
                  </div>
                </div>
              `;
            }).join("")}
          </div>
        </div>

        <div>
          <div class="section-label">
            <span>自定义附加 DLL / ASI 扩展</span>
            <button id="btnImportExt" class="btn btn-sm" data-focusable="true">+ 导入扩展文件</button>
          </div>
          <div class="cw-group">
            ${custom.length === 0 ? `
              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">暂无自定义扩展</span>
                  <span class="cw-row-sub">可导入第三方帧生成模组或自定义 ASI 插件，与主内核同步注入</span>
                </div>
              </div>
            ` : custom.map((ext) => `
              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">${esc(ext.fileName)}</span>
                  <span class="cw-row-sub">${Math.round((ext.sizeBytes || 0) / 1024)} KB</span>
                </div>
                <div class="comp-actions">
                  ${renderSwitch(`ext_${esc(ext.fileName)}`, !!ext.enabled)}
                  <button class="btn btn-sm btn-danger-soft" data-delete-ext="${esc(ext.fileName)}" data-focusable="true">删除</button>
                </div>
              </div>
            `).join("")}
          </div>
        </div>
      </div>
    `;
  }

  function renderShortcutsView() {
    const s = state.snapshot?.settings || {};
    const qamButtons = s.qamGamepadCombo?.buttons || ["BACK", "RB"];
    const insButtons = s.insertGamepadCombo?.buttons || ["BACK", "START"];
    const qamKeys = (s.qamHotkey?.displayText || "Ctrl + Alt + O").split("+").map((x) => x.trim()).filter(Boolean);
    const insKeys = (s.insertHotkey?.displayText || "Ctrl + Alt + I").split("+").map((x) => x.trim()).filter(Boolean);

    return `
      <div class="page-scroll">
        <div>
          <div class="section-label">
            <span>掌机手柄与按键映射</span>
            <span>可在 WinControls / SteamInput 中将背键 (L4/R4) 绑定至下列键鼠热键</span>
          </div>
          <div class="cw-group">
            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">手柄呼出 / 收起 快捷侧栏 (QAM)</span>
                <span class="cw-row-sub">游戏全屏运行时也可直接按下组合键展开右侧悬浮控制条</span>
              </div>
              <div class="comp-actions">
                ${renderKeycaps(qamButtons)}
                <button id="btnRecordQamCombo" class="btn btn-sm" data-focusable="true">
                  ${state.recordingTarget === "qam" ? "请按下手柄组合..." : "录制组合"}
                </button>
              </div>
            </div>

            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">手柄向游戏内发送 OptiScaler 菜单 (Insert)</span>
                <span class="cw-row-sub">无需外接键盘即可直接在游戏界面内呼出 OptiScaler 原生菜单</span>
              </div>
              <div class="comp-actions">
                ${renderKeycaps(insButtons)}
                <button id="btnRecordInsCombo" class="btn btn-sm" data-focusable="true">
                  ${state.recordingTarget === "insert" ? "请按下手柄组合..." : "录制组合"}
                </button>
              </div>
            </div>

            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">全局键盘热键 · 快捷侧栏 / 游戏菜单</span>
                <span class="cw-row-sub">一键呼出 QAM 与模拟 Insert，支持在掌机快捷键软件中映射给 L4/R4</span>
              </div>
              <div class="comp-actions">
                <span class="cw-row-sub">QAM</span>
                <button id="btnHotkeyQam" class="btn btn-sm" data-focusable="true">${renderKeycaps(qamKeys)}</button>
                <span class="cw-row-sub" style="margin-left:6px;">INS</span>
                <button id="btnHotkeyIns" class="btn btn-sm" data-focusable="true">${renderKeycaps(insKeys)}</button>
              </div>
            </div>
          </div>
        </div>

        <div>
          <div class="section-label"><span>后台驻留与自动化行为</span></div>
          <div class="cw-group">
            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">启动主界面时默认进入掌机全屏模式</span>
                <span class="cw-row-sub">适合配合 Steam 大屏幕或开机直达掌机桌面使用</span>
              </div>
              ${renderSwitch("setFullscreenBoot", !!s.launchConsoleFullscreen)}
            </div>

            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">全局自动后台扫描并注入安全游戏</span>
                <span class="cw-row-sub">仅对离线无反作弊且可写目录生效，默认遵循主流 FSR 4.1.2 + XeFG 预设</span>
              </div>
              <div class="comp-actions">
                <button id="btnRunAutoInject" class="btn btn-sm" data-focusable="true">立即执行一次</button>
                ${renderSwitch("setAutoInject", !!s.backgroundAutoInjectEnabled)}
              </div>
            </div>

            <div class="cw-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">开机自启动并静默最小化至系统托盘</span>
                <span class="cw-row-sub">后台常驻极低功耗，随时响应掌机背键与手柄热键</span>
              </div>
              ${renderSwitch("setStartup", !!s.launchAtWindowsStartup)}
            </div>
          </div>
        </div>
      </div>
    `;
  }

  // ---------------- QAM View Renderer (440px Split Shell) ----------------
  function renderQamView() {
    const active = getActiveGame();
    const games = state.snapshot?.games || [];
    const presets = state.snapshot?.presets || [];
    const cfg = state.config;
    const inj = active?.injection;
    const sys = state.snapshot?.systemControl;

    const isUninjectedPreview = !inj?.isInjected && !state._userModifiedConfig;

    const headerPill = `
      <div class="qam-game-pill">
        ${active?.coverImageUrl
          ? `<img class="qam-thumb" src="${esc(active.coverImageUrl)}" alt="${esc(active.title)}" />`
          : `<div class="qam-thumb"></div>`}
        <div class="qam-game-meta">
          <div class="qam-game-title">${esc(active?.title || "未选定目标游戏")}</div>
          <div class="qam-game-sub">
            ${active?.isCurrentlyRunning ? "运行中" : "已就绪"} · ${inj?.isInjected ? `已注入 (${esc(formatConfigSummary(inj.appliedConfig || cfg, true))})` : "原生未注入"}
          </div>
        </div>
        ${sys ? `<button id="btnQamJumpSys" class="qam-quick-sys-chip ${(sys.refreshRateHz || 60) <= 60 ? "warn" : ""}" title="点击切到掌机中控">${sys.refreshRateHz}Hz · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}</button>` : ""}
        <button id="btnQamOpenGames" class="btn btn-sm" data-focusable="true">切换</button>
      </div>
    `;

    const statusBanner = inj?.isInjected
      ? `<div id="qamStatusBanner" class="qam-status-banner injected"><span class="qam-status-dot"></span><span>当前已注入生效：${esc(formatConfigSummary(inj.appliedConfig || cfg))}${state._userModifiedConfig ? "（已改待应用）" : ""}</span></div>`
      : `<div id="qamStatusBanner" class="qam-status-banner native"><span class="qam-status-dot"></span><span>${state._userModifiedConfig ? "已选参数（待点击注入生效）" : "当前原生未注入 · 下方虚线为默认待注入方案"}</span></div>`;

    let bodyHtml = "";
    if (state.qamTab === "tune") {
      bodyHtml = `
        ${headerPill}
        ${statusBanner}
        <div class="preset-grid ${isUninjectedPreview ? "uninjected-preview" : ""}" style="grid-template-columns:1fr 1fr;gap:6px;">
          ${presets.map((p) => `
            <button class="preset-card ${state.selectedPresetId === p.id ? "active" : ""}"
                    style="padding:7px 9px;"
                    data-preset="${esc(p.id)}"
                    data-focusable="true">
              <span class="preset-name" style="font-size:11.5px;">${esc(p.name)}</span>
              <span class="preset-sub" style="font-size:10px;">${esc(p.subtitle.split("·")[0].trim())}</span>
            </button>
          `).join("")}
        </div>

        <div class="cw-group ${isUninjectedPreview ? "uninjected-preview" : ""}">
          <div class="qam-field">
            <div class="qam-field-head">
              <span>超分辨率 (SR)</span>
              <span class="qam-field-hint">推荐 FSR 4.1.2</span>
            </div>
            ${renderSegmented("data-sr-mode", [
              { value: "fsr4_int8", label: "FSR4" },
              { value: "fsr4_int8_lite", label: "4 Lite" },
              { value: "xess", label: "XeSS" },
              { value: "fsr31", label: "FSR3.1" }
            ], cfg.srMode, "compact")}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>帧生成 (FG)</span>
              <span class="qam-field-hint">推荐 XeFG</span>
            </div>
            ${renderSegmented("data-fg-type", [
              { value: "xefg", label: "XeFG" },
              { value: "optifg", label: "OptiFG" },
              { value: "nofg", label: "关闭" }
            ], cfg.fgType, "compact")}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>平滑锁帧省电</span>
              <span class="qam-field-hint">开VRR选90/116 · 关VRR选60</span>
            </div>
            ${renderSegmented("data-fps-limit", [
              { value: "0", label: "不限" },
              { value: "60", label: "60" },
              { value: "90", label: "90" },
              { value: "116", label: "116" }
            ], String(cfg.framerateLimit || 0), "compact")}
          </div>

          <div class="cw-row" style="padding:8px 11px;min-height:40px;">
            <span class="cw-row-title" style="font-size:12px;">Anti-Lag 2 低延迟</span>
            ${renderSwitch("swFakenvapi", !!cfg.enableFakenvapi)}
          </div>
          <div class="cw-row" style="padding:8px 11px;min-height:40px;">
            <span class="cw-row-title" style="font-size:12px;">FSR 4.1.2 验证水印</span>
            ${renderSwitch("swWatermark", !!cfg.fsr4Watermark)}
          </div>
        </div>

        <button id="btnInject" class="btn btn-primary" data-focusable="true" ${!active || state.busy ? "disabled" : ""}>
          ${svgIcon("bolt")}
          <span>${active?.isCurrentlyRunning ? "应用配置 (重开生效)" : "立即应用并注入"}</span>
        </button>

        <div class="action-row-2">
          <button id="btnSendInsert" class="btn btn-sm" data-focusable="true">呼出菜单 [INS]</button>
          <button id="btnRestore" class="btn btn-sm btn-danger-soft" data-focusable="true" ${!inj?.isInjected || state.busy ? "disabled" : ""}>还原纯净原版</button>
        </div>
      `;
    } else if (state.qamTab === "system") {
      const sCtrl = sys || {
        cpuUsagePercent: 0,
        totalRamGb: 0,
        usedRamGb: 0,
        screenWidth: 1920,
        screenHeight: 1080,
        refreshRateHz: 60,
        supportedRefreshRates: [60, 120],
        supportedResolutions: ["1920x1080", "1600x900", "1280x720"],
        brightnessSupported: false,
        brightnessPercent: 70,
        volumePercent: 50,
        isMuted: false,
        cpuBoostMode: "aggressive",
        isAcOnline: true,
        hasBattery: true,
        batteryPercent: 100
      };
      const hzOpts = (sCtrl.supportedRefreshRates?.length ? sCtrl.supportedRefreshRates : [60, 120])
        .map((hz) => ({ value: String(hz), label: `${hz}Hz` }));
      const resOpts = (sCtrl.supportedResolutions?.length ? sCtrl.supportedResolutions : ["1920x1080", "1600x900", "1280x720"])
        .map((r) => ({ value: r, label: formatResShort(r) }));
      const curRes = `${sCtrl.screenWidth || 1920}x${sCtrl.screenHeight || 1080}`;

      bodyHtml = `
        <div class="sys-telemetry-grid">
          <div class="sys-stat-box">
            <span class="sys-stat-label">电源 / 电池</span>
            <span id="sysPowerSummary" class="sys-stat-value" style="font-size:13px;">${esc(formatPowerBadge(sCtrl))}</span>
          </div>
          <div class="sys-stat-box">
            <span class="sys-stat-label">CPU · 内存</span>
            <span class="sys-stat-value" style="font-size:13px;"><span id="sysCpuLoadVal">${sCtrl.cpuUsagePercent ?? 0}%</span> · <span id="sysRamVal">${sCtrl.usedRamGb}/${sCtrl.totalRamGb}G</span></span>
          </div>
        </div>

        <div class="cw-group">
          <div class="qam-field">
            <div class="qam-field-head">
              <span>屏幕刷新率</span>
              <span class="qam-field-hint">${sCtrl.refreshRateHz <= 60 ? "锁60Hz · 推荐切120Hz" : `当前 ${sCtrl.refreshRateHz}Hz`}</span>
            </div>
            ${renderSegmented("data-refresh-hz", hzOpts, String(sCtrl.refreshRateHz), "compact")}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>掌机分辨率</span>
              <span class="qam-field-hint">${esc(curRes)}</span>
            </div>
            ${renderSegmented("data-res-mode", resOpts, curRes, "compact")}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>屏幕亮度</span>
              <span class="qam-field-hint">${sCtrl.brightnessSupported ? `${sCtrl.brightnessPercent}%` : "外接屏不支持"}</span>
            </div>
            ${renderStepSlider({
              idPrefix: "bright",
              value: sCtrl.brightnessPercent,
              displayText: sCtrl.brightnessSupported ? `${sCtrl.brightnessPercent}%` : "不支持",
              disabled: !sCtrl.brightnessSupported
            })}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>系统音量</span>
              <span class="qam-field-hint">${sCtrl.isMuted ? "已静音" : `${sCtrl.volumePercent}%`}</span>
            </div>
            ${renderStepSlider({
              idPrefix: "vol",
              value: sCtrl.volumePercent,
              muted: !!sCtrl.isMuted,
              muteBtnId: "btnToggleMute"
            })}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>Zen 5 CPU 睿频</span>
              <span class="qam-field-hint">关睿频更省电</span>
            </div>
            ${renderSegmented("data-cpu-boost", [
              { value: "off", label: "关睿频" },
              { value: "efficient", label: "高效平衡" },
              { value: "aggressive", label: "满血积极" }
            ], sCtrl.cpuBoostMode || "aggressive", "compact")}
          </div>
        </div>
      `;
    } else if (state.qamTab === "compat") {
      bodyHtml = `
        ${headerPill}
        ${statusBanner}
        <div class="cw-group ${isUninjectedPreview ? "uninjected-preview" : ""}">
          <div class="qam-field">
            <div class="qam-field-head">
              <span>画面防撕裂 (V-Sync)</span>
              <span class="qam-field-hint">防撕裂 = SyncInterval 1</span>
            </div>
            ${renderSegmented("data-vsync-mode", [
              { value: "on", label: "防撕裂" },
              { value: "auto", label: "跟随" },
              { value: "off", label: "强制关" }
            ], cfg.vsyncMode || "on", "compact")}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>插帧运动源 (FGInput)</span>
              <span class="qam-field-hint">DLSS-G 无鬼影</span>
            </div>
            ${renderSegmented("data-fg-input", [
              { value: "auto", label: "自动" },
              { value: "dlssg", label: "DLSS-G" },
              { value: "upscaler", label: "光流" }
            ], cfg.fgInputMode, "compact")}
          </div>

          <div class="qam-field">
            <div class="qam-field-head">
              <span>代理 DLL</span>
              <span class="qam-field-hint">${esc(cfg.proxyDllName)}</span>
            </div>
            ${renderSegmented("data-proxy-dll", [
              { value: "auto", label: "auto" },
              { value: "dxgi", label: "dxgi" },
              { value: "version", label: "version" }
            ], cfg.proxyDllName === "dxgi.dll" ? "dxgi" : cfg.proxyDllName === "version.dll" ? "version" : cfg.proxyDllName, "compact")}
          </div>

          <div class="cw-row" style="padding:8px 11px;min-height:40px;">
            <span class="cw-row-title" style="font-size:12px;">OptiPatcher ASI 解锁</span>
            ${renderSwitch("swOptiPatcher", !!cfg.enableOptiPatcher)}
          </div>
          <div class="cw-row" style="padding:8px 11px;min-height:40px;">
            <span class="cw-row-title" style="font-size:12px;">DXGI 显卡伪装</span>
            ${renderSwitch("swSpoofDxgi", !!cfg.spoofDxgi)}
          </div>
        </div>

        <button id="btnInspectLog" class="btn btn-sm" data-focusable="true" ${!active ? "disabled" : ""}>查看当前 INI 与注入日志</button>
      `;
    } else {
      bodyHtml = `
        <div class="section-label"><span>切换目标游戏 (${games.length})</span></div>
        <div class="cw-group">
          ${games.map((g) => `
            <button class="qam-game-row ${active && active.id === g.id ? "active" : ""}"
                    data-select-game="${esc(g.id)}"
                    data-focusable="true">
              <div style="min-width:0;flex:1;">
                <div class="qam-game-title">${esc(g.title)}</div>
                <div class="qam-game-sub">${g.isCurrentlyRunning ? "正在运行" : esc(g.engine)} · ${g.injection?.isInjected ? esc(formatConfigSummary(g.injection.appliedConfig || DEFAULT_CONFIG, true)) : "原生未注入"}</div>
              </div>
              ${g.injection?.isInjected ? `<span class="pill-badge blue">${esc(formatConfigSummary(g.injection.appliedConfig || DEFAULT_CONFIG, true))}</span>` : `<span class="pill-badge">原生</span>`}
            </button>
          `).join("")}
        </div>
      `;
    }

    return `<div class="qam-stack">${bodyHtml}</div>`;
  }

  function renderFooterLegend() {
    const legend = document.getElementById("gamepadLegend");
    if (!legend) return;

    if (IS_QAM) {
      legend.innerHTML = `
        <div class="legend-group" style="gap:10px;">
          <span class="legend-item"><span class="gp-btn-glyph a">A</span>确认</span>
          <span class="legend-item"><span class="gp-btn-glyph b">B</span>关闭</span>
          <span class="legend-item"><span class="gp-btn-glyph x">X</span>注入</span>
        </div>
        <div class="legend-group" style="gap:8px;">
          <button id="btnQamShowConsole" class="btn btn-sm" style="padding:3px 8px;font-size:10.5px;">[START] 控制台</button>
          <button id="btnQamClose" class="win-btn" style="width:24px;height:24px;" aria-label="关闭侧栏">
            <svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="currentColor" stroke-width="1.8"><line x1="4" y1="4" x2="12" y2="12"></line><line x1="12" y1="4" x2="4" y2="12"></line></svg>
          </button>
        </div>
      `;
      return;
    }

    const active = getActiveGame();
    const kernel = getActiveKernelInfo();
    legend.innerHTML = `
      <div class="legend-group">
        <span class="legend-item"><span class="gp-btn-glyph a">A</span>确认 / 选择</span>
        <span class="legend-item"><span class="gp-btn-glyph b">B</span>返回 / 折叠</span>
        <span class="legend-item"><span class="gp-btn-glyph x">X</span>一键注入</span>
        <span class="legend-item"><span class="gp-btn-glyph y">Y</span>游戏内菜单</span>
        <span class="legend-item"><span class="keycap-pill" style="height:18px;font-size:10px;">LB/RB</span>切换分页</span>
      </div>
      <div class="legend-group" style="color:var(--text-muted);font-size:11px;">
        <span>内核: ${esc(kernel.displayName)} ${esc(kernel.installedVersion || "")}</span>
        ${active ? `<span>目标: ${esc(active.title)}</span>` : ""}
      </div>
    `;
  }

  // ---------------- Main Render & Event Wiring ----------------
  function render() {
    const active = getActiveGame();
    syncAmbientBackdrop(active);

    const rail = document.getElementById("qamRail");
    if (rail) {
      rail.classList.toggle("hidden", !IS_QAM);
      rail.querySelectorAll(".qam-rail-btn").forEach((btn) => {
        btn.classList.toggle("active", btn.dataset.qamTab === state.qamTab);
      });
    }

    if (!IS_QAM) {
      document.querySelectorAll(".tab-btn").forEach((btn) => {
        btn.classList.toggle("active", btn.dataset.tab === state.tab);
      });
    }

    const mainView = document.getElementById("mainView");
    if (mainView) {
      if (IS_QAM) {
        mainView.innerHTML = renderQamView();
      } else if (state.tab === "tune") {
        mainView.innerHTML = renderTuneView();
      } else if (state.tab === "system") {
        mainView.innerHTML = renderSystemView();
      } else if (state.tab === "library") {
        mainView.innerHTML = renderLibraryView();
      } else if (state.tab === "components") {
        mainView.innerHTML = renderComponentsView();
      } else {
        mainView.innerHTML = renderShortcutsView();
      }
    }

    renderFooterLegend();
    bindViewEvents();
    applySpatialFocus();
  }

  function bindViewEvents() {
    // Preset cards
    document.querySelectorAll("[data-preset]").forEach((el) => {
      el.addEventListener("click", () => applyPreset(el.dataset.preset));
    });

    // Segmented SR / FG / FGInput / Proxy / Kernel / QAM Side / Library filter
    document.querySelectorAll("[data-sr-mode]").forEach((el) => {
      el.addEventListener("click", () => {
        syncSrModeConfig(el.dataset.srMode);
        render();
      });
    });

    document.querySelectorAll("[data-fg-type]").forEach((el) => {
      el.addEventListener("click", () => {
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        state.config.fgType = el.dataset.fgType;
        render();
      });
    });

    document.querySelectorAll("[data-fps-limit]").forEach((el) => {
      el.addEventListener("click", () => {
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        state.config.framerateLimit = parseInt(el.dataset.fpsLimit, 10) || 0;
        render();
      });
    });

    document.querySelectorAll("[data-vsync-mode]").forEach((el) => {
      el.addEventListener("click", () => {
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        state.config.vsyncMode = el.dataset.vsyncMode;
        render();
      });
    });

    document.getElementById("btnEnableVrr")?.addEventListener("click", async () => {
      try {
        const res = await rpc("enableWindowedVrr", {});
        if (state.snapshot) {
          state.snapshot.vrrWindowedEnabled = true;
        }
        showToast(res?.message || "已开启窗口化 VRR", "success");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-fg-input]").forEach((el) => {
      el.addEventListener("click", () => {
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        state.config.fgInputMode = el.dataset.fgInput;
        render();
      });
    });

    document.querySelectorAll("[data-proxy-dll]").forEach((el) => {
      el.addEventListener("click", () => {
        const val = el.dataset.proxyDll;
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        state.config.proxyDllName = val === "dxgi" ? "dxgi.dll" : val === "version" ? "version.dll" : val;
        render();
      });
    });

    document.getElementById("swFakenvapi")?.addEventListener("click", () => {
      state.selectedPresetId = "custom";
      state._userModifiedConfig = true;
      state.config.enableFakenvapi = !state.config.enableFakenvapi;
      render();
    });

    document.getElementById("swWatermark")?.addEventListener("click", () => {
      state.selectedPresetId = "custom";
      state._userModifiedConfig = true;
      state.config.fsr4Watermark = !state.config.fsr4Watermark;
      render();
    });

    document.getElementById("swOptiPatcher")?.addEventListener("click", () => {
      state.selectedPresetId = "custom";
      state._userModifiedConfig = true;
      state.config.enableOptiPatcher = !state.config.enableOptiPatcher;
      render();
    });

    document.getElementById("swSpoofDxgi")?.addEventListener("click", () => {
      state.selectedPresetId = "custom";
      state._userModifiedConfig = true;
      state.config.spoofDxgi = !state.config.spoofDxgi;
      render();
    });

    document.getElementById("btnToggleAdvanced")?.addEventListener("click", () => {
      state.advancedOpen = !state.advancedOpen;
      render();
    });
    document.getElementById("btnExpandPagefile")?.addEventListener("click", async () => {
      try {
        const res = await rpc("expandPagefile", {});
        if (res?.memoryHealth && state.snapshot) {
          state.snapshot.memoryHealth = res.memoryHealth;
        }
        showToast(res?.message || "已扩容虚拟内存分页文件，请重启后生效", "success");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    // Quick target switching
    document.querySelectorAll("[data-select-game]").forEach((el) => {
      el.addEventListener("click", async () => {
        state._userModifiedConfig = false;
        await rpc("selectRunningFallback", { gameId: el.dataset.selectGame });
        if (IS_QAM) {
          state.qamTab = "tune";
          state.focusIndex = 0;
        }
        syncConfigFromActiveGame(state.snapshot);
        render();
      });
    });

    document.getElementById("btnMoreGames")?.addEventListener("click", () => {
      state.tab = "library";
      state.focusIndex = 0;
      render();
    });

    document.getElementById("btnQamOpenGames")?.addEventListener("click", () => {
      state.qamTab = "games";
      state.focusIndex = 0;
      render();
    });

    // Primary Actions
    document.getElementById("btnInject")?.addEventListener("click", () => executeInject(false));
    document.getElementById("btnInjectRestart")?.addEventListener("click", () => executeInject(true));
    document.getElementById("btnRestore")?.addEventListener("click", () => executeRestore());
    document.getElementById("btnSendInsert")?.addEventListener("click", () => rpc("sendInsertKey"));

    document.getElementById("btnLaunchGame")?.addEventListener("click", async () => {
      const g = getActiveGame();
      if (!g) return;
      try {
        await rpc("launchGame", { gameId: g.id });
        showToast(`正在启动 ${g.title}`, "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnOpenFolder")?.addEventListener("click", () => {
      const g = getActiveGame();
      if (g) rpc("openFolder", { path: g.targetRenderDirectory });
    });

    document.getElementById("btnInspectLog")?.addEventListener("click", openDiagnosticsModal);

    // Library events
    const searchInput = document.getElementById("libSearchInput");
    searchInput?.addEventListener("input", (e) => {
      state.searchQuery = e.target.value;
      const cursor = searchInput.selectionStart;
      render();
      const nextInput = document.getElementById("libSearchInput");
      if (nextInput) {
        nextInput.focus();
        nextInput.setSelectionRange(cursor, cursor);
      }
    });

    document.querySelectorAll("[data-lib-filter]").forEach((el) => {
      el.addEventListener("click", () => {
        state.libraryFilter = el.dataset.libFilter;
        state.focusIndex = 0;
        render();
      });
    });

    document.querySelectorAll("[data-open-game]").forEach((el) => {
      el.addEventListener("click", async () => {
        state._userModifiedConfig = false;
        await rpc("selectRunningFallback", { gameId: el.dataset.openGame });
        state.tab = "tune";
        state.focusIndex = 0;
        syncConfigFromActiveGame(state.snapshot);
        render();
      });
    });

    document.getElementById("btnScanGames")?.addEventListener("click", async () => {
      showToast("正在扫描 Steam 与本地游戏目录...");
      await rpc("scanGames");
      showToast("游戏库扫描完成", "success");
    });

    const addExeHandler = async () => {
      const entry = await rpc("addCustomGameByBrowse", { pickFolder: false });
      if (entry?.id) {
        await rpc("selectRunningFallback", { gameId: entry.id });
        state.tab = "tune";
        render();
        showToast(`已添加 ${entry.title}`, "success");
      }
    };
    document.getElementById("btnAddExeInline")?.addEventListener("click", addExeHandler);
    document.getElementById("btnAddExeLib")?.addEventListener("click", addExeHandler);

    // Components events
    document.querySelectorAll("[data-active-slot]").forEach((el) => {
      el.addEventListener("click", async () => {
        const nextSettings = { ...state.snapshot.settings, activeOptiScalerSlot: el.dataset.activeSlot };
        await rpc("saveSettings", nextSettings);
        showToast(`默认核心已切换为 ${el.dataset.activeSlot}`, "success");
      });
    });

    document.getElementById("btnRefreshComps")?.addEventListener("click", async () => {
      showToast("正在从 GitHub 获取最新组件版本...");
      await rpc("refreshComponents");
      const snap = await rpc("getSnapshot");
      applySnapshot(snap);
      showToast("组件目录已刷新", "success");
    });

    document.querySelectorAll("[data-install-comp]").forEach((el) => {
      el.addEventListener("click", async () => {
        const slot = el.dataset.installComp;
        try {
          showToast(`正在下载并校验 ${slot}...`);
          await rpc("installComponent", { slot });
          showToast(`${slot} 安装更新完成`, "success");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.querySelectorAll("[data-import-comp]").forEach((el) => {
      el.addEventListener("click", async () => {
        const slot = el.dataset.importComp;
        const info = await rpc("importComponentFile", { slot });
        if (info) showToast(`已导入 ${slot} 离线包`, "success");
      });
    });

    document.getElementById("btnImportExt")?.addEventListener("click", async () => {
      const ext = await rpc("importCustomExtension");
      if (ext) showToast(`已导入扩展 ${ext.fileName}`, "success");
    });

    (state.snapshot?.customExtensions || []).forEach((ext) => {
      document.getElementById(`ext_${ext.fileName}`)?.addEventListener("click", async () => {
        await rpc("toggleCustomExtension", { fileName: ext.fileName, enabled: !ext.enabled });
      });
    });

    document.querySelectorAll("[data-delete-ext]").forEach((el) => {
      el.addEventListener("click", async () => {
        await rpc("deleteCustomExtension", { fileName: el.dataset.deleteExt });
        showToast("已移除自定义扩展");
      });
    });

    // Shortcuts & Settings events
    document.getElementById("btnRecordQamCombo")?.addEventListener("click", () => startRecordCombo("qam"));
    document.getElementById("btnRecordInsCombo")?.addEventListener("click", () => startRecordCombo("insert"));

    document.getElementById("btnHotkeyQam")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 3, virtualKey: 0x4F, displayText: "Ctrl + Alt + O" },
        { modifiers: 6, virtualKey: 0x4F, displayText: "Ctrl + Shift + O" },
        { modifiers: 3, virtualKey: 0x51, displayText: "Ctrl + Alt + Q" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.qamHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, qamHotkey: next });
      showToast(`QAM 键盘热键已切换为 ${next.displayText}`, "success");
    });

    document.getElementById("btnHotkeyIns")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 3, virtualKey: 0x49, displayText: "Ctrl + Alt + I" },
        { modifiers: 6, virtualKey: 0x49, displayText: "Ctrl + Shift + I" },
        { modifiers: 0, virtualKey: 0x7A, displayText: "F11" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.insertHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, insertHotkey: next });
      showToast(`菜单键盘热键已切换为 ${next.displayText}`, "success");
    });

    document.getElementById("setFullscreenBoot")?.addEventListener("click", async () => {
      const s = state.snapshot.settings;
      await rpc("saveSettings", { ...s, launchConsoleFullscreen: !s.launchConsoleFullscreen });
    });

    document.getElementById("setAutoInject")?.addEventListener("click", async () => {
      const s = state.snapshot.settings;
      await rpc("saveSettings", { ...s, backgroundAutoInjectEnabled: !s.backgroundAutoInjectEnabled });
    });

    document.getElementById("setStartup")?.addEventListener("click", async () => {
      const s = state.snapshot.settings;
      await rpc("saveSettings", { ...s, launchAtWindowsStartup: !s.launchAtWindowsStartup });
    });

    document.getElementById("btnRunAutoInject")?.addEventListener("click", async () => {
      const ev = await rpc("runAutoInjectNow");
      showToast(`后台自动注入检查完成（处理 ${ev?.length || 0} 项）`, "success");
    });

    // Phase 1 Handheld System Control Center events
    const applySys = async (method, payload, okMsg = null) => {
      try {
        const updated = await rpc(method, payload);
        if (state.snapshot && updated) {
          state.snapshot.systemControl = updated;
        }
        render();
        if (okMsg) showToast(okMsg, "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    };

    document.querySelectorAll("[data-refresh-hz]").forEach((el) => {
      el.addEventListener("click", () => {
        const hz = Number(el.dataset.refreshHz);
        applySys("setRefreshRate", { hz }, `屏幕刷新率已切换为 ${hz}Hz`);
      });
    });

    document.querySelectorAll("[data-res-mode]").forEach((el) => {
      el.addEventListener("click", () => {
        const resolution = el.dataset.resMode;
        applySys("setResolution", { resolution }, `系统分辨率已切换为 ${formatResShort(resolution)}`);
      });
    });

    document.querySelectorAll("[data-cpu-boost]").forEach((el) => {
      el.addEventListener("click", () => {
        const mode = el.dataset.cpuBoost;
        const label = mode === "off" ? "关睿频省电" : mode === "efficient" ? "高效平衡" : "满血积极";
        applySys("setCpuBoost", { mode }, `CPU 睿频已切换为 ${label}`);
      });
    });

    document.getElementById("brightDec")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.brightnessPercent ?? 70;
      applySys("setBrightness", { percent: Math.max(5, cur - 10) });
    });
    document.getElementById("brightInc")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.brightnessPercent ?? 70;
      applySys("setBrightness", { percent: Math.min(100, cur + 10) });
    });
    document.querySelector('[data-slider-track="bright"]')?.addEventListener("click", (e) => {
      if (!state.snapshot?.systemControl?.brightnessSupported) return;
      const rect = e.currentTarget.getBoundingClientRect();
      const pct = Math.max(5, Math.min(100, Math.round(((e.clientX - rect.left) / rect.width) * 100)));
      applySys("setBrightness", { percent: pct });
    });

    document.getElementById("volDec")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.volumePercent ?? 50;
      applySys("setVolume", { percent: Math.max(0, cur - 10) });
    });
    document.getElementById("volInc")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.volumePercent ?? 50;
      applySys("setVolume", { percent: Math.min(100, cur + 10) });
    });
    document.querySelector('[data-slider-track="vol"]')?.addEventListener("click", (e) => {
      const rect = e.currentTarget.getBoundingClientRect();
      const pct = Math.max(0, Math.min(100, Math.round(((e.clientX - rect.left) / rect.width) * 100)));
      applySys("setVolume", { percent: pct });
    });
    document.getElementById("btnToggleMute")?.addEventListener("click", () => {
      const muted = !!state.snapshot?.systemControl?.isMuted;
      applySys("setMute", { muted: !muted });
    });

    document.getElementById("btnSysEnableVrr")?.addEventListener("click", async () => {
      try {
        const res = await rpc("enableWindowedVrr");
        showToast(res?.message || "已开启 Win11 窗口化 VRR 优化", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnSysGoTune")?.addEventListener("click", () => {
      state.tab = "tune";
      state.focusIndex = 0;
      render();
    });

    document.getElementById("btnSysRefresh")?.addEventListener("click", async () => {
      try {
        const updated = await rpc("getSystemControl");
        if (state.snapshot && updated) state.snapshot.systemControl = updated;
        render();
        showToast("系统遥测已刷新", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnQamJumpSys")?.addEventListener("click", () => {
      state.qamTab = "system";
      state.focusIndex = 0;
      render();
    });

    // QAM Footer Actions
    document.getElementById("btnQamShowConsole")?.addEventListener("click", () => rpc("showConsole"));
    document.getElementById("btnQamClose")?.addEventListener("click", () => rpc("hideQam"));
  }

  async function executeInject(restartRunning) {
    const active = getActiveGame();
    if (!active || state.busy) return;
    if (active.antiCheat?.riskLevel === "HighRisk") {
      showToast(`该游戏检测到内核反作弊 (${active.antiCheat.detectedSystems?.join(", ")})，已安全拦截`, "error");
      return;
    }

    state.busy = true;
    render();
    try {
      const method = restartRunning ? "injectAndRestartGame" : "injectGame";
      const res = await rpc(method, {
        gameId: active.id,
        presetId: state.selectedPresetId,
        config: state.config
      });
      if (res?.success) {
        state._userModifiedConfig = false;
        syncConfigFromActiveGame(state.snapshot);
      }
      showToast(res?.message || "注入完成", res?.success ? "success" : "error");
    } catch (err) {
      showToast(err.message, "error");
    } finally {
      state.busy = false;
      render();
    }
  }

  async function executeRestore() {
    const active = getActiveGame();
    if (!active || state.busy) return;
    state.busy = true;
    render();
    try {
      const res = await rpc("restoreGame", { gameId: active.id });
      if (res?.success) {
        state._userModifiedConfig = false;
        syncConfigFromActiveGame(state.snapshot);
      }
      showToast(res?.message || "已还原至纯净原版", res?.success ? "success" : "info");
    } catch (err) {
      showToast(err.message, "error");
    } finally {
      state.busy = false;
      render();
    }
  }

  async function openDiagnosticsModal() {
    const active = getActiveGame();
    if (!active) return;
    try {
      const data = await rpc("readGameConfigAndLog", { gameId: active.id });
      document.getElementById("modalTitle").textContent = `${active.title} · 注入配置与运行日志`;
      document.getElementById("modalSubtitle").textContent = `渲染目录：${active.targetRenderDirectory}`;
      const body = document.getElementById("modalBody");
      const deployed = data?.manifest?.createdFiles || data?.manifest?.deployedFiles || [];
      const filesLine = deployed.length
        ? `已部署文件 (${deployed.length})：${deployed.join(", ")}`
        : "当前尚未通过 HaloDeck 写入注入清单。";
      body.innerHTML = `
        <div class="pill-badge ${data?.iniExists ? "blue" : ""}" style="align-self:flex-start">${esc(filesLine)}</div>
        <div class="section-label" style="margin-top:4px;"><span>OptiScaler.ini 内容预览</span></div>
        <pre class="code-block">${esc(data?.iniPreview || "尚未生成 OptiScaler.ini（请先点击「一键注入推荐配置」）")}</pre>
        <div class="section-label"><span>OptiScaler.log 最近输出</span></div>
        <pre class="code-block">${esc(data?.logSnippet || "未发现 OptiScaler.log（注入后进入游戏一次即可产生诊断日志）")}</pre>
      `;
      document.getElementById("modalBackdrop").classList.remove("hidden");
      applySpatialFocus();
    } catch (err) {
      showToast(err.message, "error");
    }
  }

  async function startRecordCombo(target) {
    state.recordingTarget = target;
    render();
    await rpc("beginRecordGamepadCombo");
    showToast("请在手柄上同时按下目标组合键后松开...", "info");
  }

  async function finishGamepadComboRecord(buttons) {
    const target = state.recordingTarget;
    state.recordingTarget = null;
    if (target && buttons?.length && state.snapshot?.settings) {
      const next = { ...state.snapshot.settings };
      if (target === "qam") next.qamGamepadCombo = { buttons };
      else if (target === "insert") next.insertGamepadCombo = { buttons };
      await rpc("saveSettings", next);
      showToast(`已保存手柄组合键：${buttons.join(" + ")}`, "success");
    } else {
      render();
    }
  }

  // ---------------- Spatial Controller & Keyboard Navigation ----------------
  function getFocusableItems() {
    const modal = document.getElementById("modalBackdrop");
    const root = modal && !modal.classList.contains("hidden") ? modal : document;
    return Array.from(root.querySelectorAll('[data-focusable="true"]')).filter((el) => {
      if (el.disabled || el.offsetParent === null) return false;
      const rect = el.getBoundingClientRect();
      return rect.width > 0 && rect.height > 0;
    });
  }

  function applySpatialFocus() {
    document.querySelectorAll(".gp-focused").forEach((el) => el.classList.remove("gp-focused"));
    const items = getFocusableItems();
    if (!items.length) return;
    if (state.focusIndex >= items.length) state.focusIndex = 0;
    if (state.focusIndex < 0) state.focusIndex = items.length - 1;
    const target = items[state.focusIndex];
    target.classList.add("gp-focused");
    target.scrollIntoView({ block: "nearest", inline: "nearest" });
  }

  function moveSpatial(dir) {
    const items = getFocusableItems();
    if (!items.length) return;

    const curr = items[state.focusIndex] || items[0];
    const cr = curr.getBoundingClientRect();
    const cx = cr.left + cr.width / 2;
    const cy = cr.top + cr.height / 2;

    if (dir === "UP" || dir === "DOWN") {
      const inQamBody = IS_QAM && cx > 52;
      const candidates = [];
      items.forEach((cand, idx) => {
        if (idx === state.focusIndex) return;
        const r = cand.getBoundingClientRect();
        const x = r.left + r.width / 2;
        const y = r.top + r.height / 2;
        const dy = y - cy;
        if (dir === "DOWN" && dy <= 8) return;
        if (dir === "UP" && dy >= -8) return;
        const dx = Math.abs(x - cx);
        candidates.push({
          idx,
          samePane: !inQamBody || x > 52,
          score: Math.abs(dy) * 2.2 + dx * 0.75
        });
      });

      const paneCandidates = candidates.filter((c) => c.samePane);
      const pool = paneCandidates.length ? paneCandidates : candidates;
      if (pool.length) {
        pool.sort((a, b) => a.score - b.score);
        state.focusIndex = pool[0].idx;
      } else {
        state.focusIndex = dir === "DOWN" ? 0 : items.length - 1;
      }
      applySpatialFocus();
      return;
    }

    const rowTolerance = Math.max(cr.height, 28) * 0.9;
    let sameRowBest = -1;
    let sameRowScore = Infinity;
    let anyBest = -1;
    let anyScore = Infinity;

    items.forEach((cand, idx) => {
      if (idx === state.focusIndex) return;
      const r = cand.getBoundingClientRect();
      const x = r.left + r.width / 2;
      const y = r.top + r.height / 2;
      const dx = x - cx;
      const dy = Math.abs(y - cy);
      if (dir === "RIGHT" && dx <= 4) return;
      if (dir === "LEFT" && dx >= -4) return;
      const score = Math.abs(dx) + dy * 2.4;
      if (dy <= rowTolerance && score < sameRowScore) {
        sameRowScore = score;
        sameRowBest = idx;
      }
      if (score < anyScore) {
        anyScore = score;
        anyBest = idx;
      }
    });

    const bestIdx = sameRowBest !== -1 ? sameRowBest : anyBest;
    if (bestIdx !== -1) state.focusIndex = bestIdx;
    else state.focusIndex = (state.focusIndex + (dir === "RIGHT" ? 1 : -1) + items.length) % items.length;
    applySpatialFocus();
  }

  function handleGamepadAction(action) {
    const modal = document.getElementById("modalBackdrop");
    const modalOpen = modal && !modal.classList.contains("hidden");

    switch (action) {
      case "UP":
      case "DOWN":
      case "LEFT":
      case "RIGHT":
        moveSpatial(action);
        break;
      case "A": {
        const items = getFocusableItems();
        items[state.focusIndex]?.click();
        break;
      }
      case "B":
        if (modalOpen) {
          modal.classList.add("hidden");
          applySpatialFocus();
        } else if (state.advancedOpen && !IS_QAM) {
          state.advancedOpen = false;
          render();
        } else if (IS_QAM) {
          rpc("hideQam");
        }
        break;
      case "X":
        if (!modalOpen && (state.tab === "tune" || (IS_QAM && state.qamTab === "tune"))) {
          executeInject(false);
        }
        break;
      case "Y":
        if (!modalOpen) rpc("sendInsertKey");
        break;
      case "LB":
      case "RB":
        if (!modalOpen) {
          const step = action === "RB" ? 1 : -1;
          if (IS_QAM) {
            state.qamTab = QAM_TABS[(QAM_TABS.indexOf(state.qamTab) + step + QAM_TABS.length) % QAM_TABS.length];
          } else {
            state.tab = CONSOLE_TABS[(CONSOLE_TABS.indexOf(state.tab) + step + CONSOLE_TABS.length) % CONSOLE_TABS.length];
          }
          state.focusIndex = 0;
          render();
        }
        break;
      case "START":
        if (IS_QAM) rpc("showConsole");
        break;
    }
  }

  // ---------------- Global Static Listeners & Boot ----------------
  document.querySelectorAll(".tab-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.tab = btn.dataset.tab;
      state.focusIndex = 0;
      render();
    });
  });

  document.querySelectorAll(".qam-rail-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.qamTab = btn.dataset.qamTab;
      state.focusIndex = 0;
      render();
    });
  });

  document.getElementById("topSysChip")?.addEventListener("click", () => {
    state.tab = "system";
    state.focusIndex = 0;
    render();
  });
  document.getElementById("btnOpenQam")?.addEventListener("click", () => rpc("toggleQam"));
  document.getElementById("btnMinimize")?.addEventListener("click", () => rpc("minimizeToTray"));
  document.getElementById("btnFullscreen")?.addEventListener("click", () => rpc("toggleFullscreen"));
  document.getElementById("btnCloseApp")?.addEventListener("click", () => rpc("minimizeToTray"));
  document.getElementById("btnCloseModal")?.addEventListener("click", () => {
    document.getElementById("modalBackdrop").classList.add("hidden");
    applySpatialFocus();
  });

  window.addEventListener("keydown", (e) => {
    if (e.target && (e.target.tagName === "INPUT" || e.target.tagName === "TEXTAREA")) return;
    if (e.key === "ArrowDown") { e.preventDefault(); moveSpatial("DOWN"); }
    else if (e.key === "ArrowUp") { e.preventDefault(); moveSpatial("UP"); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); moveSpatial("LEFT"); }
    else if (e.key === "ArrowRight") { e.preventDefault(); moveSpatial("RIGHT"); }
    else if (e.key === "Escape") handleGamepadAction("B");
  });

  (async function boot() {
    try {
      const snap = await rpc("getSnapshot");
      applySnapshot(snap);
    } catch {
      render();
    }
  })();
})();
