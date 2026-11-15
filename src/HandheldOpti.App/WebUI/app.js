(() => {
  "use strict";

  // Global error tracking for GUI self-verify diagnostics
  window.__hoErrors = [];
  window.addEventListener("error", (e) => window.__hoErrors.push(e.message || String(e)));
  window.addEventListener("unhandledrejection", (e) => window.__hoErrors.push(String(e.reason || e)));

  const IS_QAM = new URLSearchParams(window.location.search).get("mode") === "qam";
  document.body.classList.toggle("mode-qam", IS_QAM);
  document.body.classList.toggle("mode-console", !IS_QAM);

  const CONSOLE_TABS = ["handheld", "monitor", "controls", "opti"];
  const OPTI_SUB_TABS = ["tune", "library", "components"];
  const QAM_TABS = ["handheld", "osd", "controls", "opti"];

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

  const GPD_BACK_KEY_OPTIONS = [
    { value: -1, label: "保留原宏/不变" },
    { value: 0x0068, label: "F13 (侧边栏)" },
    { value: 0x0069, label: "F14 (性能OSD)" },
    { value: 0x0049, label: "Insert (菜单)" },
    { value: 0x006A, label: "F15 (菜单/备用)" },
    { value: 0x0000, label: "解除映射" }
  ];

  const state = {
    tab: "handheld",
    optiSubTab: "tune",
    qamTab: "handheld",
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
    busy: false,
    killArmed: false,
    killArmTimer: null,
    gpdLoaded: false,
    gpdFetching: false,
    gpdDraft: {
      l4Key: -1,
      r4Key: -1,
      rumble: 1,
      leftDeadzone: 0,
      rightDeadzone: 0
    }
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
      if (state.tab === "opti" && state.optiSubTab === "components") render();
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

  // ---------------- Formatters & Live Telemetry ----------------
  function formatPowerBadge(sys) {
    if (!sys) return "--%";
    if (!sys.hasBattery) return "AC 直连 (无电池)";
    const watts = typeof sys.batteryPowerWatts === "number" && sys.batteryPowerWatts > 0
      ? ` ${sys.batteryPowerWatts.toFixed(1)}W`
      : "";
    if (sys.isAcOnline) {
      return sys.isCharging ? `${sys.batteryPercent}% 充电${watts}` : `${sys.batteryPercent}% AC 满电`;
    }
    return `${sys.batteryPercent}%${watts ? ` · -${watts.trim()}` : " 电池"}`;
  }

  function formatResShort(resStr) {
    if (!resStr) return "1080p";
    if (resStr.startsWith("1920x")) return "1080p";
    if (resStr.startsWith("1600x")) return "900p";
    if (resStr.startsWith("1280x")) return "720p";
    return resStr;
  }

  function formatEta(minutes) {
    if (!minutes || minutes <= 0) return "";
    const h = Math.floor(minutes / 60);
    const m = minutes % 60;
    return h > 0 ? `约 ${h}h ${m}m` : `约 ${m} 分钟`;
  }

  function formatFanModeLabel(mode) {
    switch (mode) {
      case "silent": return "静音曲线";
      case "balanced": return "均衡曲线";
      case "turbo": return "狂暴散热";
      case "manual": return "手动定速";
      default: return "EC 自动";
    }
  }

  function buildSvgChart(seriesA, seriesB, maxA, maxB, colorA, colorB, labelA, labelB) {
    const W = 360;
    const H = 110;
    const padX = 8;
    const padY = 12;
    const innerW = W - padX * 2;
    const innerH = H - padY * 2;

    const toPts = (arr, cap) => {
      if (!Array.isArray(arr) || arr.length === 0) return "";
      const list = arr.length === 1 ? [arr[0], arr[0]] : arr;
      const safeCap = Math.max(1, cap || 100);
      return list.map((v, idx) => {
        const x = padX + (idx / (list.length - 1)) * innerW;
        const clamped = Math.max(0, Math.min(safeCap, Number(v) || 0));
        const y = padY + innerH - (clamped / safeCap) * innerH;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      }).join(" ");
    };

    const ptsA = toPts(seriesA, maxA);
    const ptsB = seriesB ? toPts(seriesB, maxB) : "";
    const lastA = Array.isArray(seriesA) && seriesA.length ? seriesA[seriesA.length - 1] : 0;
    const lastB = Array.isArray(seriesB) && seriesB.length ? seriesB[seriesB.length - 1] : 0;

    const areaA = ptsA
      ? `<polygon points="${padX},${padY + innerH} ${ptsA} ${W - padX},${padY + innerH}" fill="${colorA}" fill-opacity="0.14" />`
      : "";

    return `
      <svg class="sparkline-svg" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" aria-hidden="true">
        <line x1="${padX}" y1="${padY}" x2="${W - padX}" y2="${padY}" stroke="rgba(255,255,255,0.06)" stroke-dasharray="3 3" />
        <line x1="${padX}" y1="${padY + innerH / 2}" x2="${W - padX}" y2="${padY + innerH / 2}" stroke="rgba(255,255,255,0.07)" stroke-dasharray="3 3" />
        <line x1="${padX}" y1="${padY + innerH}" x2="${W - padX}" y2="${padY + innerH}" stroke="rgba(255,255,255,0.08)" />
        ${areaA}
        ${ptsA ? `<polyline fill="none" stroke="${colorA}" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" points="${ptsA}" />` : ""}
        ${ptsB ? `<polyline fill="none" stroke="${colorB}" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="4 2" points="${ptsB}" />` : ""}
        <text x="${padX + 2}" y="11" fill="${colorA}" font-size="9.5" font-weight="600">${esc(labelA)}: ${esc(Math.round(lastA))}</text>
        ${seriesB ? `<text x="${W - padX - 2}" y="11" text-anchor="end" fill="${colorB}" font-size="9.5" font-weight="600">${esc(labelB)}: ${esc(Math.round(lastB))}</text>` : ""}
      </svg>
    `;
  }

  function patchLiveTelemetryDom(sys) {
    if (!sys) return;
    const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
    const apuText = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W` : "--W";

    const topApuLabel = document.getElementById("topApuLabel");
    if (topApuLabel) {
      topApuLabel.textContent = `APU ${apuText} · ${maxTemp > 0 ? `${maxTemp}°C` : "--°C"}`;
    }

    const topSysLabel = document.getElementById("topSysLabel");
    if (topSysLabel) {
      topSysLabel.textContent = `${formatResShort(`${sys.screenWidth}x${sys.screenHeight}`)} · ${sys.refreshRateHz || "--"}Hz · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`;
    }

    const pfApuVal = document.getElementById("pfApuVal");
    if (pfApuVal) pfApuVal.textContent = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : `目标 ${state.snapshot?.settings?.currentTdpWatts || 45}W`;

    const pfApuSub = document.getElementById("pfApuSub");
    if (pfApuSub) pfApuSub.textContent = `GPU ${sys.gpuUsagePercent || 0}% · ${maxTemp > 0 ? `${maxTemp}°C` : "待读取温度"}`;

    const pfBatVal = document.getElementById("pfBatVal");
    if (pfBatVal) {
      if (!sys.hasBattery) pfBatVal.textContent = "无电池";
      else if (sys.isCharging) pfBatVal.textContent = `+${(sys.batteryPowerWatts || 0).toFixed(1)} W`;
      else if (!sys.isAcOnline && sys.batteryPowerWatts > 0) pfBatVal.textContent = `-${sys.batteryPowerWatts.toFixed(1)} W`;
      else pfBatVal.textContent = `${sys.batteryPercent}%`;
    }

    const pfBatSub = document.getElementById("pfBatSub");
    if (pfBatSub) {
      if (!sys.hasBattery) pfBatSub.textContent = "外接供电直连";
      else if (sys.isCharging) pfBatSub.textContent = `${sys.batteryPercent}% · 充电 ${formatEta(sys.chargeEtaMinutes)}`.trim();
      else if (sys.isAcOnline) pfBatSub.textContent = `${sys.batteryPercent}% · ${sys.batteryRemainingWh?.toFixed(1) || "--"} Wh`;
      else pfBatSub.textContent = `${sys.batteryPercent}% · ${formatEta(sys.batteryEtaMinutes) || `${sys.batteryRemainingWh?.toFixed(1) || "--"} Wh`}`;
    }

    const pfAcVal = document.getElementById("pfAcVal");
    if (pfAcVal) {
      pfAcVal.textContent = !sys.isAcOnline
        ? "未接 AC"
        : (sys.estimatedChargerWatts != null ? `≈ ${sys.estimatedChargerWatts.toFixed(1)} W` : "AC 在线");
    }

    const pfAcSub = document.getElementById("pfAcSub");
    if (pfAcSub) {
      pfAcSub.textContent = !sys.isAcOnline
        ? `电池 ${sys.voltageVolts > 0 ? `${sys.voltageVolts.toFixed(1)}V` : "离线续航中"}`
        : (sys.isBypassPower ? "AC 旁路直供模式" : "供电与电池快充中");
    }

    const fanRpmBadge = document.getElementById("fanRpmBadge");
    if (fanRpmBadge) {
      const hw = sys.hardwareService || {};
      fanRpmBadge.textContent = hw.ecReady && hw.fanRpm > 0
        ? `${hw.fanRpm} RPM · PWM ${hw.appliedFanPwm}%`
        : (hw.serviceRunning ? "仅 SMU 就绪" : "EC 未授权");
    }

    const kpiApuW = document.getElementById("kpiApuW");
    if (kpiApuW) kpiApuW.textContent = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W";
    const kpiGpuLoad = document.getElementById("kpiGpuLoad");
    if (kpiGpuLoad) kpiGpuLoad.textContent = `${sys.gpuUsagePercent || 0}%`;
    const kpiCpuLoad = document.getElementById("kpiCpuLoad");
    if (kpiCpuLoad) kpiCpuLoad.textContent = `${sys.cpuUsagePercent || 0}%`;
    const kpiTemps = document.getElementById("kpiTemps");
    if (kpiTemps) kpiTemps.textContent = maxTemp > 0 ? `${sys.gpuTempC || "--"} / ${sys.cpuTempC || "--"} °C` : "-- °C";
    const kpiMem = document.getElementById("kpiMem");
    if (kpiMem) kpiMem.textContent = sys.totalRamGb > 0 ? `${sys.usedRamGb} / ${sys.totalRamGb} GB` : "-- GB";
    const kpiBat = document.getElementById("kpiBat");
    if (kpiBat) kpiBat.textContent = formatPowerBadge(sys);

    const chartPower = document.getElementById("svgChartPowerTemp");
    if (chartPower) {
      chartPower.innerHTML = buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 85, 100, "#2dd4bf", "#fbbf24", "APU W", "Temp °C");
    }
    const chartLoad = document.getElementById("svgChartGpuCpu");
    if (chartLoad) {
      chartLoad.innerHTML = buildSvgChart(sys.gpuUsageHistory || [], sys.cpuUsageHistory || [], 100, 100, "#38bdf8", "#a78bfa", "GPU %", "CPU %");
    }
    const qamSpark = document.getElementById("qamOsdSparkline");
    if (qamSpark) {
      qamSpark.innerHTML = buildSvgChart(sys.apuWattsHistory || [], sys.gpuUsageHistory || [], 85, 100, "#2dd4bf", "#38bdf8", "APU W", "GPU %");
    }
    const qamPowerChip = document.getElementById("qamPowerChip");
    if (qamPowerChip) {
      const rpm = sys.hardwareService?.fanRpm || 0;
      qamPowerChip.textContent = `APU ${apuText} · ${maxTemp || "--"}°C · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}${rpm > 0 ? ` · ${rpm}RPM` : ""}`;
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

  function syncGpdDraft(gpd) {
    if (!gpd) return;
    state.gpdDraft = {
      l4Key: !gpd.connected || gpd.l4Mode === 2 ? -1 : (typeof gpd.l4KeyCode === "number" ? gpd.l4KeyCode : 0),
      r4Key: !gpd.connected || gpd.r4Mode === 2 ? -1 : (typeof gpd.r4KeyCode === "number" ? gpd.r4KeyCode : 0),
      rumble: typeof gpd.rumbleLevel === "number" ? gpd.rumbleLevel : 1,
      leftDeadzone: typeof gpd.leftDeadzone === "number" ? gpd.leftDeadzone : 0,
      rightDeadzone: typeof gpd.rightDeadzone === "number" ? gpd.rightDeadzone : 0
    };
  }

  function applySnapshot(snap) {
    state.snapshot = snap;
    syncConfigFromActiveGame(snap);
    if (snap?.gpdController && !state.gpdLoaded) {
      syncGpdDraft(snap.gpdController);
    }
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

  // ---------------- Console View 1: Handheld Home ----------------
  function renderHandheldView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const frozen = snap.frozenGames || [];
    const active = getActiveGame();

    const curTdp = settings.currentTdpWatts || 45;
    const silentW = settings.tdpPresetSilent || 18;
    const balancedW = settings.tdpPresetBalanced || 35;
    const turboW = settings.tdpPresetTurbo || 65;

    const fanMode = settings.fanMode || "auto";
    const manualFanPct = settings.manualFanPercent || 65;

    const hzOptions = (sys.supportedRefreshRates && sys.supportedRefreshRates.length)
      ? sys.supportedRefreshRates.map((h) => ({ value: String(h), label: `${h}Hz` }))
      : [{ value: "60", label: "60Hz" }, { value: "120", label: "120Hz" }];

    const resOptions = (sys.supportedResolutions && sys.supportedResolutions.length)
      ? sys.supportedResolutions.slice(0, 3).map((r) => ({ value: r, label: formatResShort(r) }))
      : [{ value: "1920x1080", label: "1080p" }, { value: "1600x900", label: "900p" }, { value: "1280x720", label: "720p" }];

    const curRes = `${sys.screenWidth || 1920}x${sys.screenHeight || 1080}`;
    const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
    const apuPowerText = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : `目标 ${curTdp}W`;
    const batMainText = !sys.hasBattery
      ? "无电池"
      : sys.isCharging
        ? `+${(sys.batteryPowerWatts || 0).toFixed(1)} W`
        : (!sys.isAcOnline && sys.batteryPowerWatts > 0)
          ? `-${sys.batteryPowerWatts.toFixed(1)} W`
          : `${sys.batteryPercent || "--"}%`;
    const batSubText = !sys.hasBattery
      ? "AC 电源直连"
      : sys.isCharging
        ? `${sys.batteryPercent}% · 充电 ${formatEta(sys.chargeEtaMinutes)}`.trim()
        : sys.isAcOnline
          ? `${sys.batteryPercent}% · ${(sys.batteryRemainingWh || 0).toFixed(1)} Wh`
          : `${sys.batteryPercent}% · ${formatEta(sys.batteryEtaMinutes) || `${(sys.batteryRemainingWh || 0).toFixed(1)} Wh`}`;
    const acMainText = !sys.isAcOnline
      ? "未接 AC"
      : (sys.estimatedChargerWatts != null ? `≈ ${sys.estimatedChargerWatts.toFixed(1)} W` : "AC 在线");
    const acSubText = !sys.isAcOnline
      ? `电池 ${sys.voltageVolts > 0 ? `${sys.voltageVolts.toFixed(1)}V` : "续航中"}`
      : (sys.isBypassPower ? "AC 旁路直供中" : "整机供电与快充");

    const perGameProfiles = settings.perGameProfiles || {};
    const gamePerf = active ? (perGameProfiles[active.id] || {
      gameId: active.id,
      gameName: active.title,
      enabled: false,
      tdpWatts: curTdp,
      refreshRateHz: sys.refreshRateHz || 120
    }) : null;

    const serviceBanner = !hw.serviceRunning
      ? `
        <div class="hh-banner warn">
          <div class="hh-banner-text">
            <strong>普通用户态运行中（显示 · 音频 · 监控 · 游戏工具 · 超分均已可用）</strong>
            <span class="hh-banner-sub">${hw.pawnIoInstalled
              ? "已检测到 PawnIO 驱动。启用 HaloDeck 硬件服务后即可免 UAC 弹窗直控 12W–85W SMU 功耗与 EC 双风扇。"
              : "未检测到 PawnIO 驱动或尚未注册 HaloDeckHardwareSvc。安装 PawnIO 后点击右侧按钮即可开启硬件直控。"}</span>
          </div>
          <div class="hh-banner-actions">
            ${hw.gpdServiceExists ? `<button id="btnToggleGpdSvc" class="btn btn-sm" data-focusable="true">${hw.gpdServiceRunning ? "暂停 GPDTools 服务" : "恢复 GPDTools 服务"}</button>` : ""}
            <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" data-focusable="true">一键启用 TDP / 风扇直控</button>
          </div>
        </div>`
      : `
        <div class="hh-banner">
          <div class="hh-banner-text">
            <strong>PawnIO 底层硬件直控已就绪 · ${esc(hw.dmiBoard || "GPD WIN 5")}</strong>
            <span class="hh-banner-sub">SMU: ${hw.smuReady ? `已就绪 (当前目标 ${hw.appliedTdpWatts}W)` : "未就绪"} · EC 风扇: ${hw.ecReady ? `${hw.fanRpm} RPM (${formatFanModeLabel(hw.fanMode)})` : "仅 SMU 模式"}</span>
          </div>
          <div class="hh-banner-actions">
            ${hw.gpdServiceExists ? `<button id="btnToggleGpdSvc" class="btn btn-sm" data-focusable="true">${hw.gpdServiceRunning ? "暂停 GPDTools 服务" : "恢复 GPDTools 服务"}</button>` : ""}
          </div>
        </div>`;

    return `
      <div class="page-scroll">
        ${serviceBanner}

        <div class="hh-layout">
          <!-- Left Column: Power Flow + TDP + Dual Fan -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">实时整机电源流</span>
                <span class="hh-card-badge ${sys.isBypassPower ? "live" : ""}">${sys.isAcOnline ? (sys.isBypassPower ? "AC 旁路直供" : "AC 供电中") : "便携电池供电"}</span>
              </div>
              <div class="power-flow-grid">
                <div class="pf-box">
                  <span class="pf-label">APU 封装功耗</span>
                  <span id="pfApuVal" class="pf-val">${esc(apuPowerText)}</span>
                  <span id="pfApuSub" class="pf-sub">GPU ${sys.gpuUsagePercent || 0}% · ${maxTemp > 0 ? `${maxTemp}°C` : "ADL2 待命"}</span>
                </div>
                <div class="pf-box">
                  <span class="pf-label">电池充放功率</span>
                  <span id="pfBatVal" class="pf-val">${esc(batMainText)}</span>
                  <span id="pfBatSub" class="pf-sub">${esc(batSubText)}</span>
                </div>
                <div class="pf-box">
                  <span class="pf-label">充电器输入估算</span>
                  <span id="pfAcVal" class="pf-val">${esc(acMainText)}</span>
                  <span id="pfAcSub" class="pf-sub">${esc(acSubText)}</span>
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">Strix Halo APU 功耗上限 (TDP)</span>
                <span class="hh-card-badge ${hw.smuReady ? "live" : "warn"}">${hw.smuReady ? `SMU ${hw.appliedTdpWatts}W 生效` : `设定 ${curTdp}W`}</span>
              </div>

              <div class="tdp-presets-grid">
                <button class="tdp-preset-btn ${curTdp === silentW ? "active" : ""}" data-tdp-preset="silent" data-watts="${silentW}" data-focusable="true">
                  <span class="tdp-preset-name">静音续航</span>
                  <span class="tdp-preset-watt">${silentW}W</span>
                </button>
                <button class="tdp-preset-btn ${curTdp === balancedW ? "active" : ""}" data-tdp-preset="balanced" data-watts="${balancedW}" data-focusable="true">
                  <span class="tdp-preset-name">甜点均衡</span>
                  <span class="tdp-preset-watt">${balancedW}W</span>
                </button>
                <button class="tdp-preset-btn ${curTdp === turboW ? "active" : ""}" data-tdp-preset="turbo" data-watts="${turboW}" data-focusable="true">
                  <span class="tdp-preset-name">狂暴释放</span>
                  <span class="tdp-preset-watt">${turboW}W</span>
                </button>
              </div>

              <div class="slider-control-row">
                <div class="slider-header">
                  <span>手动精细调节 (12W – 85W)</span>
                  <div style="display:flex;align-items:center;gap:6px;">
                    <button id="btnTdpDec" class="step-btn" data-focusable="true" title="降低 1W">−1W</button>
                    <strong id="tdpValReadout">${curTdp} W</strong>
                    <button id="btnTdpInc" class="step-btn" data-focusable="true" title="提高 1W">+1W</button>
                  </div>
                </div>
                <input id="tdpRangeInput" class="hh-range" type="range" min="12" max="85" step="1" value="${curTdp}" />
                <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;margin-top:2px;">
                  <span style="font-size:11px;color:var(--text-muted);">保存当前 ${curTdp}W 至档位：</span>
                  <div style="display:flex;gap:4px;">
                    <button class="pill-btn small" data-save-tdp-preset="silent" data-focusable="true">存静音</button>
                    <button class="pill-btn small" data-save-tdp-preset="balanced" data-focusable="true">存均衡</button>
                    <button class="pill-btn small" data-save-tdp-preset="turbo" data-focusable="true">存狂暴</button>
                  </div>
                </div>
              </div>

              <div class="cw-row" style="padding:8px 0 4px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">插拔电自动切换 TDP</span>
                  <span class="cw-row-sub">随 AC 电源插拔自动下发离电与插电功耗上限</span>
                </div>
                ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
              </div>

              <div class="linkage-row">
                <div class="pf-box" style="padding:8px 10px;">
                  <span class="pf-label">离电目标 TDP</span>
                  <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
                    <button id="btnBatTdpDec" class="step-btn" data-focusable="true">−2</button>
                    <strong style="font-size:14px;">${settings.batteryTdpWatts || 25}W</strong>
                    <button id="btnBatTdpInc" class="step-btn" data-focusable="true">+2</button>
                  </div>
                </div>
                <div class="pf-box" style="padding:8px 10px;">
                  <span class="pf-label">插电目标 TDP</span>
                  <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
                    <button id="btnAcTdpDec" class="step-btn" data-focusable="true">−2</button>
                    <strong style="font-size:14px;">${settings.acTdpWatts || 55}W</strong>
                    <button id="btnAcTdpInc" class="step-btn" data-focusable="true">+2</button>
                  </div>
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">双风扇智能温控</span>
                <span id="fanRpmBadge" class="hh-card-badge ${hw.ecReady ? "live" : ""}">${hw.ecReady && hw.fanRpm > 0 ? `${hw.fanRpm} RPM · PWM ${hw.appliedFanPwm}%` : (hw.serviceRunning ? "仅 SMU 就绪" : "EC 未授权")}</span>
              </div>
              <div class="fan-mode-pills">
                ${[
                  { value: "auto", label: "EC 自动" },
                  { value: "silent", label: "静音" },
                  { value: "balanced", label: "均衡" },
                  { value: "turbo", label: "狂暴" },
                  { value: "manual", label: "手动%" }
                ].map((opt) => `
                  <button class="pill-btn ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
                `).join("")}
              </div>
              ${fanMode === "manual" ? `
                <div class="slider-control-row">
                  <div class="slider-header">
                    <span>手动固定占空比</span>
                    <strong id="fanManualVal">${manualFanPct}%</strong>
                  </div>
                  <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="5" value="${manualFanPct}" />
                </div>` : ""}
              <div style="font-size:11px;color:var(--text-muted);">
                热安全保护：APU ≥ 82°C 时强制提升至 75%+ 防过热降频；服务停止或退出时自动交还 EC 自主控温。
              </div>
            </div>
          </div>

          <!-- Right Column: Display/CPU + Quick Toolbox + Per-Game Profile -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">显示 · 音频 · CPU 调度</span>
                <button id="btnSysEnableVrr" class="pill-btn small ${snap.vrrWindowedEnabled ? "active" : ""}" data-focusable="true">${snap.vrrWindowedEnabled ? "VRR 窗口化已开" : "开启窗口化 VRR"}</button>
              </div>

              <div class="cw-group">
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">屏幕刷新率</span>
                    <span class="cw-row-sub">当前 ${sys.refreshRateHz || "--"} Hz · GPD Win 5 推荐 120Hz + VRR</span>
                  </div>
                  ${renderSegmented("data-refresh-hz", hzOptions, String(sys.refreshRateHz || 120), "compact")}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">系统分辨率</span>
                    <span class="cw-row-sub">${sys.screenWidth || "--"} × ${sys.screenHeight || "--"}</span>
                  </div>
                  ${renderSegmented("data-res-mode", resOptions, curRes, "compact")}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">屏幕亮度 / 系统音量</span>
                  </div>
                  <div style="display:flex;flex-direction:column;gap:6px;min-width:210px;">
                    ${renderStepSlider({
                      idPrefix: "bright",
                      value: sys.brightnessPercent ?? 70,
                      displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "外接屏不支持",
                      disabled: !sys.brightnessSupported
                    })}
                    ${renderStepSlider({
                      idPrefix: "vol",
                      value: sys.volumePercent ?? 50,
                      muted: !!sys.isMuted,
                      muteBtnId: "btnToggleMute"
                    })}
                  </div>
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">CPU 睿频模式</span>
                  </div>
                  ${renderSegmented("data-cpu-boost", [
                    { value: "off", label: "关睿频" },
                    { value: "efficient", label: "平衡" },
                    { value: "aggressive", label: "满血" }
                  ], sys.cpuBoostMode || "aggressive", "compact")}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">CPU 最大频率上限 (PowrProf)</span>
                    <span class="cw-row-sub">限制 CPU 锁频把功耗预算让给核显 8060S</span>
                  </div>
                  ${renderSegmented("data-cpu-max-mhz", [
                    { value: "0", label: "不限" },
                    { value: "3200", label: "3.2G" },
                    { value: "3800", label: "3.8G" },
                    { value: "4400", label: "4.4G" },
                    { value: "5000", label: "5.0G" }
                  ], String(sys.cpuMaxFreqMhz ?? settings.cpuMaxFreqMhz ?? 0), "compact")}
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">掌机快捷工具箱</span>
                <span class="hh-card-badge">${frozen.length > 0 ? `已冻结 ${frozen.length} 个` : "即时响应"}</span>
              </div>
              <div class="toolbox-grid">
                <button class="toolbox-tile ${frozen.length > 0 ? "active" : ""}" data-toolbox="toggle_fridge" data-focusable="true">
                  <span>游戏冰箱</span>
                  <span class="tb-sub">${frozen.length > 0 ? "恢复挂起" : "挂起省电"}</span>
                </button>
                <button id="btnToolboxMouse" class="toolbox-tile ${settings.gamepadMouseModeEnabled ? "active" : ""}" data-focusable="true">
                  <span>手柄鼠标</span>
                  <span class="tb-sub">${settings.gamepadMouseModeEnabled ? "已开启" : "已关闭"}</span>
                </button>
                <button class="toolbox-tile" data-toolbox="show_desktop" data-focusable="true">
                  <span>返回桌面</span>
                  <span class="tb-sub">Win + D</span>
                </button>
                <button class="toolbox-tile" data-toolbox="task_view" data-focusable="true">
                  <span>切换窗口</span>
                  <span class="tb-sub">Win + Tab</span>
                </button>
                <button class="toolbox-tile" data-toolbox="osk" data-focusable="true">
                  <span>屏幕键盘</span>
                  <span class="tb-sub">TabTip</span>
                </button>
                <button class="toolbox-tile" data-toolbox="screenshot" data-focusable="true">
                  <span>区域截屏</span>
                  <span class="tb-sub">Win+Shift+S</span>
                </button>
                <button class="toolbox-tile" data-toolbox="taskmgr" data-focusable="true">
                  <span>任务管理器</span>
                  <span class="tb-sub">TaskMgr</span>
                </button>
                <button id="btnKillGame" class="toolbox-tile ${state.killArmed ? "armed" : ""}" data-focusable="true">
                  <span>${state.killArmed ? "确认强关?" : "强关游戏"}</span>
                  <span class="tb-sub">${state.killArmed ? "再次点击终止" : "二次确认安全"}</span>
                </button>
              </div>
              ${frozen.length > 0 ? `
                <div class="frozen-chip-row">
                  ${frozen.map((f) => `
                    <button class="frozen-chip" data-resume-pid="${f.pid}" data-focusable="true">❄ 唤醒 ${esc(f.gameName || f.processName)} (PID ${f.pid})</button>
                  `).join("")}
                </div>` : ""}
            </div>

            ${active && gamePerf ? `
              <div class="hh-card">
                <div class="hh-card-head">
                  <span class="hh-card-title">当前游戏独立性能档案</span>
                  <span class="hh-card-badge">${esc(active.title)}</span>
                </div>
                <div class="cw-row" style="padding:4px 0 8px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">运行该游戏时自动应用专属 TDP 与刷新率</span>
                    <span class="cw-row-sub">游戏退出后自动还原全局基础配置</span>
                  </div>
                  ${renderSwitch("swGamePerfEnabled", !!gamePerf.enabled)}
                </div>
                <div class="per-game-perf-row">
                  <span style="font-size:12px;color:var(--text-secondary);">专属 TDP：<strong>${gamePerf.tdpWatts || curTdp}W</strong></span>
                  <button id="btnGamePerfTdpDec" class="step-btn" data-focusable="true">−5W</button>
                  <button id="btnGamePerfTdpInc" class="step-btn" data-focusable="true">+5W</button>
                  ${renderSegmented("data-game-perf-hz", [
                    { value: "0", label: "不改Hz" },
                    { value: "60", label: "60Hz" },
                    { value: "120", label: "120Hz" }
                  ], String(gamePerf.refreshRateHz || 0), "compact")}
                </div>
              </div>` : ""}
          </div>
        </div>
      </div>
    `;
  }

  // ---------------- Console View 2: Monitor & Native Click-Through OSD ----------------
  function renderMonitorView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const settings = snap.settings || {};
    const level = settings.overlayLevel ?? 0;
    const pos = settings.overlayPosition || "top-center";
    const opacity = settings.overlayOpacity ?? 0.86;
    const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);

    const previewLines = level === 0
      ? ["悬浮窗已关闭（零资源占用，开启后鼠标完全穿透不拦截游戏点击）"]
      : level === 1
        ? [`APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W · ${maxTemp || "--"}°C · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`]
        : level === 2
          ? [
              `APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W (目标 ${settings.currentTdpWatts || 45}W)`,
              `GPU ${sys.gpuUsagePercent || 0}% @ ${sys.gfxClockMhz || 0}MHz · ${sys.gpuTempC || "--"}°C`,
              `CPU ${sys.cpuUsagePercent || 0}% · RAM ${sys.usedRamGb || "--"}/${sys.totalRamGb || "--"}GB`
            ]
          : [
              `${getActiveGame()?.title || "HaloDeck"} · 8060S`,
              `APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W · GPU ${sys.gpuUsagePercent || 0}% · CPU ${sys.cpuUsagePercent || 0}%`,
              `VRAM ${sys.vramUsedMb || "--"}MB · ${maxTemp || "--"}°C · ${formatPowerBadge(sys)}`
            ];

    return `
      <div class="page-scroll">
        <div class="mon-top-grid">
          <div class="hh-card">
            <div class="hh-card-head">
              <span class="hh-card-title">无钩子穿透式性能悬浮窗 (Steam Deck 风格 0–4 档)</span>
              <span class="hh-card-badge ${level > 0 ? "live" : ""}">${level === 0 ? "已关闭" : `档位 ${level}`}</span>
            </div>
            <p style="font-size:11.5px;color:var(--text-secondary);margin:0 0 10px;">
              纯原生 WPF WS_EX_TRANSPARENT 顶层透明窗口：不注入游戏进程、不触发任何反作弊、不抢游戏焦点、鼠标点击完全穿透。
            </p>
            <div class="osd-level-pills">
              ${[
                { v: 0, label: "0 · 关" },
                { v: 1, label: "1 · 单行" },
                { v: 2, label: "2 · 紧凑" },
                { v: 3, label: "3 · 详尽" },
                { v: 4, label: "4 · 曲线" }
              ].map((o) => `
                <button class="pill-btn ${level === o.v ? "active" : ""}" data-osd-level="${o.v}" data-focusable="true">${o.label}</button>
              `).join("")}
            </div>

            <div class="cw-group">
              <div class="cw-row">
                <div class="cw-row-copy"><span class="cw-row-title">屏幕停靠位置</span></div>
                ${renderSegmented("data-osd-pos", [
                  { value: "top-left", label: "左上角" },
                  { value: "top-center", label: "顶部居中" },
                  { value: "top-right", label: "右上角" }
                ], pos, "compact")}
              </div>
              <div class="cw-row">
                <div class="cw-row-copy"><span class="cw-row-title">背景透明度</span></div>
                ${renderSegmented("data-osd-opacity", [
                  { value: "0.55", label: "55%" },
                  { value: "0.75", label: "75%" },
                  { value: "0.88", label: "88%" },
                  { value: "1", label: "100%" }
                ], String(opacity >= 0.95 ? 1 : opacity >= 0.82 ? 0.88 : opacity >= 0.65 ? 0.75 : 0.55), "compact")}
              </div>
            </div>
          </div>

          <div class="hh-card">
            <div class="hh-card-head">
              <span class="hh-card-title">悬浮窗实时样式预览</span>
              <span class="hh-card-badge">${esc(pos)} · ${Math.round(opacity * 100)}%</span>
            </div>
            <div class="osd-preview-box pos-${esc(pos)}">
              <div class="osd-preview-hud" style="opacity:${opacity};">
                ${previewLines.map((line) => `<span>${esc(line)}</span>`).join("")}
              </div>
            </div>
          </div>
        </div>

        <div class="mon-kpi-grid">
          <div class="mon-kpi-card">
            <span class="pf-label">APU 封装功耗</span>
            <span id="kpiApuW" class="mon-kpi-val">${sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W"}</span>
            <span class="mon-kpi-sub">${sys.adlAvailable ? "AMD ADL2 实时采样" : "需 AMD 核显驱动"}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">GPU 占用与频率</span>
            <span id="kpiGpuLoad" class="mon-kpi-val">${sys.gpuUsagePercent || 0}%</span>
            <span class="mon-kpi-sub">${sys.gfxClockMhz || 0} MHz · VRAM ${sys.vramUsedMb || "--"} MB</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">CPU 占用与主频</span>
            <span id="kpiCpuLoad" class="mon-kpi-val">${sys.cpuUsagePercent || 0}%</span>
            <span class="mon-kpi-sub">${sys.cpuClockMhz > 0 ? `${sys.cpuClockMhz} MHz` : esc(sys.cpuName || "AMD Ryzen")}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">GPU / CPU 核心温度</span>
            <span id="kpiTemps" class="mon-kpi-val">${maxTemp > 0 ? `${sys.gpuTempC || "--"} / ${sys.cpuTempC || "--"} °C` : "-- °C"}</span>
            <span class="mon-kpi-sub">风扇 ${sys.hardwareService?.fanRpm || 0} RPM</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">系统内存占用</span>
            <span id="kpiMem" class="mon-kpi-val">${sys.totalRamGb > 0 ? `${sys.usedRamGb} / ${sys.totalRamGb} GB` : "-- GB"}</span>
            <span class="mon-kpi-sub">提交上限 ${snap.memoryHealth?.commitLimitGb || "--"} GB</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">电源与电池</span>
            <span id="kpiBat" class="mon-kpi-val" style="font-size:15px;">${esc(formatPowerBadge(sys))}</span>
            <span class="mon-kpi-sub">${sys.estimatedChargerWatts != null ? `充电器输入 ≈ ${sys.estimatedChargerWatts.toFixed(1)}W` : "实时电流采样"}</span>
          </div>
        </div>

        <div class="charts-grid">
          <div class="chart-card">
            <div class="hh-card-head" style="margin-bottom:4px;">
              <span class="hh-card-title">最近 60 秒 · APU 功耗 (W) & 温度 (°C)</span>
            </div>
            <div id="svgChartPowerTemp">
              ${buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 85, 100, "#2dd4bf", "#fbbf24", "APU W", "Temp °C")}
            </div>
          </div>
          <div class="chart-card">
            <div class="hh-card-head" style="margin-bottom:4px;">
              <span class="hh-card-title">最近 60 秒 · GPU (%) & CPU (%) 利用率</span>
            </div>
            <div id="svgChartGpuCpu">
              ${buildSvgChart(sys.gpuUsageHistory || [], sys.cpuUsageHistory || [], 100, 100, "#38bdf8", "#a78bfa", "GPU %", "CPU %")}
            </div>
          </div>
        </div>
      </div>
    `;
  }

  // ---------------- Console View 3: Controls, GPD Back Buttons & App Settings ----------------
  function renderControlsView() {
    const snap = state.snapshot || {};
    const settings = snap.settings || {};
    const gpd = snap.gpdController || {};
    const draft = state.gpdDraft;

    if (!state.gpdLoaded && !state.gpdFetching) {
      state.gpdFetching = true;
      setTimeout(async () => {
        try {
          const res = await rpc("getGpdController", { forceRefresh: false });
          if (state.snapshot && res) {
            state.snapshot.gpdController = res;
            syncGpdDraft(res);
          }
        } catch {
          // Non-fatal when device disconnected
        } finally {
          state.gpdLoaded = true;
          state.gpdFetching = false;
          if (state.tab === "controls" && !IS_QAM) render();
        }
      }, 40);
    }

    const qamButtons = settings.qamGamepadCombo?.buttons || ["BACK", "RB"];
    const osdButtons = settings.osdGamepadCombo?.buttons || ["BACK", "Y"];
    const mouseButtons = settings.mouseModeGamepadCombo?.buttons || ["BACK", "LB"];
    const insButtons = settings.insertGamepadCombo?.buttons || ["BACK", "START"];

    return `
      <div class="page-scroll">
        <div class="controls-layout">
          <!-- Left: GPD V2 Hardware Flash + Desktop Mouse Mode -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">GPD 手柄硬件固件设置 (VID_2F24&PID_0137 V2)</span>
                <div style="display:flex;align-items:center;gap:6px;">
                  <span class="hh-card-badge ${gpd.connected ? "live" : ""}">${gpd.connected ? `已连接 · FW X${gpd.firmwareX || "?"}/K${gpd.firmwareK || "?"}` : "未连接 GPD HID"}</span>
                  <button id="btnReadGpd" class="btn btn-sm" data-focusable="true">读取板载 Flash</button>
                </div>
              </div>

              <div class="gpd-warn-note">
                当前：L4 [${esc(gpd.l4KeyLabel || "未知")}] · R4 [${esc(gpd.r4KeyLabel || "未知")}]。若需保留原有出厂多键宏，请保持「保留原宏/不变」；设为 F13 / F14 后可配合 HaloDeck 后台全局监听一键呼出侧边栏与性能悬浮窗。
              </div>

              <div class="hh-field" style="margin-bottom:8px;">
                <label>L4 背键映射目标</label>
                <div style="display:flex;flex-wrap:wrap;gap:5px;">
                  ${GPD_BACK_KEY_OPTIONS.map((opt) => `
                    <button class="pill-btn small ${draft.l4Key === opt.value ? "active" : ""}" data-gpd-l4="${opt.value}" data-focusable="true">${opt.label}</button>
                  `).join("")}
                </div>
              </div>

              <div class="hh-field" style="margin-bottom:10px;">
                <label>R4 背键映射目标</label>
                <div style="display:flex;flex-wrap:wrap;gap:5px;">
                  ${GPD_BACK_KEY_OPTIONS.map((opt) => `
                    <button class="pill-btn small ${draft.r4Key === opt.value ? "active" : ""}" data-gpd-r4="${opt.value}" data-focusable="true">${opt.label}</button>
                  `).join("")}
                </div>
              </div>

              <div class="gpd-grid">
                <div class="hh-field">
                  <label>马达震动强度</label>
                  ${renderSegmented("data-gpd-rumble", [
                    { value: "0", label: "关闭" },
                    { value: "1", label: "弱震" },
                    { value: "2", label: "强震" }
                  ], String(draft.rumble), "compact")}
                </div>
                <div class="hh-field">
                  <label>左 / 右摇杆中心死区补偿 (-10..10)</label>
                  <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
                    <button id="btnGpdLdzDec" class="step-btn" data-focusable="true">L−</button>
                    <span style="font-size:12px;font-variant-numeric:tabular-nums;">左 ${draft.leftDeadzone}</span>
                    <button id="btnGpdLdzInc" class="step-btn" data-focusable="true">L+</button>
                    <button id="btnGpdRdzDec" class="step-btn" data-focusable="true">R−</button>
                    <span style="font-size:12px;font-variant-numeric:tabular-nums;">右 ${draft.rightDeadzone}</span>
                    <button id="btnGpdRdzInc" class="step-btn" data-focusable="true">R+</button>
                  </div>
                </div>
              </div>

              <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;">
                <span style="font-size:11px;color:var(--text-muted);">${esc(gpd.statusMessage || "无需常驻 GPDTools 进程，直通 HID Flash 读写")}</span>
                <button id="btnSaveGpd" class="btn btn-sm btn-primary" data-focusable="true" ${!gpd.connected ? "disabled" : ""}>写入手柄 Flash</button>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">XInput 手柄桌面鼠标模式</span>
                ${renderSwitch("swMouseMode", !!settings.gamepadMouseModeEnabled)}
              </div>
              <p style="font-size:12px;color:var(--text-secondary);margin:0;">
                开启后可直接用手柄操控 Windows 桌面：<strong>左摇杆</strong>移动光标 · <strong>A</strong> 左键 · <strong>B</strong> 右键 · <strong>X</strong> 中键 · <strong>右摇杆</strong>滚轮 · 按住 <strong>LB</strong> 精细减速 / <strong>RB</strong> 急速加速。当游戏运行且 HaloDeck 窗口未在前台时推荐用组合键随时开关。
              </p>
            </div>
          </div>

          <!-- Right: Global Hotkeys & App Preferences -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">全局手柄组合键与键盘热键</span>
                <span class="hh-card-badge">后台托盘全局生效</span>
              </div>
              <div class="hotkey-list">
                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">呼出 / 隐藏右侧快捷侧栏 (QAM)</div>
                    <div class="cw-row-sub">亦响应背键 F13</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(qamButtons)}
                    <button id="btnRecordQamCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "qam" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>

                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">循环切换穿透式性能 OSD (0–4 档)</div>
                    <div class="cw-row-sub">亦响应背键 F14</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(osdButtons)}
                    <button id="btnRecordOsdCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "osd" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>

                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">一键开关手柄桌面鼠标模式</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(mouseButtons)}
                    <button id="btnRecordMouseCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "mouse" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>

                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">向游戏发送 Insert（呼出超分插件菜单）</div>
                    <div class="cw-row-sub">亦响应背键 F15</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(insButtons)}
                    <button id="btnRecordInsCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "insert" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>

                <div class="hotkey-row">
                  <div class="cw-row-title">全局键盘热键（侧栏 / OSD / Insert）</div>
                  <div style="display:flex;gap:6px;flex-wrap:wrap;">
                    <button id="btnHotkeyQam" class="btn btn-sm" data-focusable="true">侧栏: ${esc(settings.qamHotkey?.displayText || "Ctrl+Alt+O")}</button>
                    <button id="btnHotkeyOsd" class="btn btn-sm" data-focusable="true">OSD: ${esc(settings.osdHotkey?.displayText || "Ctrl+Alt+M")}</button>
                    <button id="btnHotkeyIns" class="btn btn-sm" data-focusable="true">菜单: ${esc(settings.insertHotkey?.displayText || "Ctrl+Alt+I")}</button>
                  </div>
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">启动行为与系统优化</span>
              </div>
              <div class="cw-group">
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">随 Windows 开机自启（最小化驻留托盘）</span>
                    <span class="cw-row-sub">开箱即支持全局快捷侧栏、OSD、电源联动与自动注入</span>
                  </div>
                  ${renderSwitch("setStartup", !!settings.launchAtWindowsStartup)}
                </div>
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">控制台默认全屏启动</span>
                    <span class="cw-row-sub">掌机纯手柄使用体验更接近 SteamOS</span>
                  </div>
                  ${renderSwitch("setFullscreenBoot", !!settings.launchConsoleFullscreen)}
                </div>
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">后台静默检测运行中游戏并自动注入 OptiScaler</span>
                  </div>
                  <div class="comp-actions">
                    <button id="btnRunAutoInject" class="btn btn-sm" data-focusable="true">立即扫描一次</button>
                    ${renderSwitch("setAutoInject", !!settings.backgroundAutoInjectEnabled)}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `;
  }

  // ---------------- Console View 4: OptiScaler Suite (with Sub-Navigation) ----------------
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
                ${active ? `${esc(active.engine)} · ${esc(active.architecture)} · ${esc(active.targetRenderDirectory)}` : "切换上方「游戏库」选择目标或导入本地 EXE"}
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

        <div class="tune-right">
          ${state.snapshot?.memoryHealth?.lowCommitHeadroom ? `
            <div class="mem-alert-card">
              <div class="mem-alert-copy">
                <div class="mem-alert-title">检测到高 UMA 显存拆分：可用内存 ${esc(state.snapshot.memoryHealth.totalRamGb)} GB · 提交上限 ${esc(state.snapshot.memoryHealth.commitLimitGb)} GB</div>
                <div class="mem-alert-sub">大型 DX12 游戏易因虚拟内存耗尽报 Insufficient Memory。已默认将伪装显存限制为 10GB，建议扩容系统分页文件。</div>
              </div>
              <button class="mem-alert-btn" id="btnExpandPagefile" data-focusable="true">扩容至 24-40GB</button>
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
                  <span class="cw-row-sub">原生 DLSS-G 游戏选 XeFG（随游戏开关热启停）；无 DLSS-G 可选 OptiFG</span>
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
                  <span class="cw-row-sub">120Hz 开 VRR 推荐 90/116；未开 VRR 建议不限帧或 60</span>
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
                  <span class="cw-row-sub">${snap?.vrrWindowedEnabled ? "已启用翻转防撕裂 · Win11 窗口化 VRR 已开启" : "消除插帧撕裂；检测到 Win11 窗口化 VRR 未开启"}</span>
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
                    <span class="cw-row-sub">自动模式下优先接管原生 DLSS-G 减少 UI 鬼影</span>
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
                    <span class="cw-row-sub">默认 auto (dxgi.dll)；若冲突可改 version.dll 或 winmm.dll</span>
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
                  </div>
                  ${renderSwitch("swWatermark", !!cfg.fsr4Watermark)}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">OptiPatcher ASI 运行时解锁</span>
                  </div>
                  ${renderSwitch("swOptiPatcher", !!cfg.enableOptiPatcher)}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">DXGI 显卡身份伪装 (上限 10GB)</span>
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

  function renderOptiContainerView() {
    const subContent = state.optiSubTab === "library"
      ? renderLibraryView()
      : state.optiSubTab === "components"
        ? renderComponentsView()
        : renderTuneView();

    return `
      <div style="display:flex;flex-direction:column;height:100%;min-height:0;">
        <div style="display:flex;align-items:center;justify-content:space-between;gap:10px;padding-top:2px;">
          <div class="sub-nav-bar" role="tablist" aria-label="OptiScaler 子功能">
            <button class="sub-tab-btn ${state.optiSubTab === "tune" ? "active" : ""}" data-opti-sub="tune" data-focusable="true">调优与注入</button>
            <button class="sub-tab-btn ${state.optiSubTab === "library" ? "active" : ""}" data-opti-sub="library" data-focusable="true">游戏库</button>
            <button class="sub-tab-btn ${state.optiSubTab === "components" ? "active" : ""}" data-opti-sub="components" data-focusable="true">组件与扩展</button>
          </div>
          <span style="font-size:11.5px;color:var(--text-muted);">OptiScaler · XeFG · FSR 4.1.2 INT8 子系统</span>
        </div>
        <div style="flex:1;min-height:0;display:flex;flex-direction:column;">
          ${subContent}
        </div>
      </div>
    `;
  }

  // ---------------- QAM Overlay Views (Strictly <= 440px total width) ----------------
  function renderQamView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const frozen = snap.frozenGames || [];
    const active = getActiveGame();
    const games = snap.games || [];

    if (state.qamTab === "handheld") {
      const curTdp = settings.currentTdpWatts || 45;
      const silentW = settings.tdpPresetSilent || 18;
      const balancedW = settings.tdpPresetBalanced || 35;
      const turboW = settings.tdpPresetTurbo || 65;
      const fanMode = settings.fanMode || "auto";
      const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
      const apuStr = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W` : "--W";
      const rpm = hw.fanRpm || 0;
      const hzOptions = (sys.supportedRefreshRates && sys.supportedRefreshRates.length)
        ? sys.supportedRefreshRates.map((h) => ({ value: String(h), label: `${h}Hz` }))
        : [{ value: "60", label: "60Hz" }, { value: "120", label: "120Hz" }];

      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>掌机状态实时流</span>
              <span id="qamPowerChip" class="hh-card-badge ${hw.serviceRunning ? "live" : ""}">APU ${apuStr} · ${maxTemp || "--"}°C · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}${rpm > 0 ? ` · ${rpm}RPM` : ""}</span>
            </div>
            ${!hw.serviceRunning ? `
              <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" style="width:100%;margin-bottom:6px;" data-focusable="true">一键启用 TDP / 风扇直控</button>
            ` : ""}
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>APU 功耗调节</span>
              <strong>${curTdp} W</strong>
            </div>
            <div class="qam-preset-row">
              <button class="pill-btn small ${curTdp === silentW ? "active" : ""}" data-tdp-preset="silent" data-watts="${silentW}" data-focusable="true">静音 ${silentW}W</button>
              <button class="pill-btn small ${curTdp === balancedW ? "active" : ""}" data-tdp-preset="balanced" data-watts="${balancedW}" data-focusable="true">均衡 ${balancedW}W</button>
              <button class="pill-btn small ${curTdp === turboW ? "active" : ""}" data-tdp-preset="turbo" data-watts="${turboW}" data-focusable="true">狂暴 ${turboW}W</button>
            </div>
            <div style="display:flex;align-items:center;gap:6px;margin-bottom:6px;">
              <button id="btnTdpDec" class="step-btn" data-focusable="true">−2</button>
              <input id="tdpRangeInput" class="hh-range" type="range" min="12" max="85" step="1" value="${curTdp}" style="flex:1;" />
              <button id="btnTdpInc" class="step-btn" data-focusable="true">+2</button>
            </div>
            <div class="qam-fan-row">
              ${[
                { value: "auto", label: "自动" },
                { value: "silent", label: "静" },
                { value: "balanced", label: "均" },
                { value: "turbo", label: "狂" },
                { value: "manual", label: "定" }
              ].map((opt) => `
                <button class="pill-btn small ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
              `).join("")}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>屏幕与音量</span>
              <span style="font-size:11px;color:var(--text-secondary);">${sys.refreshRateHz || "--"}Hz · ${formatResShort(`${sys.screenWidth}x${sys.screenHeight}`)}</span>
            </div>
            <div style="margin-bottom:8px;">
              ${renderSegmented("data-refresh-hz", hzOptions, String(sys.refreshRateHz || 120), "compact")}
            </div>
            <div style="display:flex;flex-direction:column;gap:6px;">
              ${renderStepSlider({ idPrefix: "bright", value: sys.brightnessPercent ?? 70, displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "不支持亮度", disabled: !sys.brightnessSupported })}
              ${renderStepSlider({ idPrefix: "vol", value: sys.volumePercent ?? 50, muted: !!sys.isMuted, muteBtnId: "btnToggleMute" })}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>快捷工具</span></div>
            <div class="qam-toolbox-mini">
              <button class="pill-btn small ${frozen.length > 0 ? "active" : ""}" data-toolbox="toggle_fridge" data-focusable="true">游戏冰箱${frozen.length ? ` (${frozen.length})` : ""}</button>
              <button id="btnToolboxMouse" class="pill-btn small ${settings.gamepadMouseModeEnabled ? "active" : ""}" data-focusable="true">手柄鼠标 ${settings.gamepadMouseModeEnabled ? "开" : "关"}</button>
              <button class="pill-btn small" data-toolbox="show_desktop" data-focusable="true">返回桌面</button>
              <button class="pill-btn small" data-toolbox="task_view" data-focusable="true">切换窗口</button>
              <button class="pill-btn small" data-toolbox="osk" data-focusable="true">屏幕键盘</button>
              <button id="btnKillGame" class="pill-btn small ${state.killArmed ? "btn-armed-danger" : ""}" data-focusable="true">${state.killArmed ? "确认强关?" : "强关游戏"}</button>
            </div>
          </div>
        </div>
      `;
    }

    if (state.qamTab === "osd") {
      const level = settings.overlayLevel ?? 0;
      const pos = settings.overlayPosition || "top-center";
      const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>性能悬浮窗级别 (0–4 档)</span>
              <span class="hh-card-badge ${level > 0 ? "live" : ""}">${level === 0 ? "已关闭" : `档位 ${level}`}</span>
            </div>
            <div class="osd-level-pills" style="margin-bottom:8px;">
              ${[0, 1, 2, 3, 4].map((v) => `
                <button class="pill-btn small ${level === v ? "active" : ""}" data-osd-level="${v}" data-focusable="true">${v === 0 ? "关" : `${v}级`}</button>
              `).join("")}
            </div>
            ${renderSegmented("data-osd-pos", [
              { value: "top-left", label: "左上" },
              { value: "top-center", label: "顶中" },
              { value: "top-right", label: "右上" }
            ], pos, "compact")}
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>硬件实时摘要</span></div>
            <div style="font-size:12px;color:var(--text-secondary);display:flex;flex-direction:column;gap:4px;">
              <div>APU: <strong style="color:#fff;">${sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W` : "--"}</strong> · GPU <strong style="color:#fff;">${sys.gpuUsagePercent || 0}%</strong> (${sys.gfxClockMhz || 0}MHz)</div>
              <div>CPU: <strong style="color:#fff;">${sys.cpuUsagePercent || 0}%</strong> · 最高温 <strong style="color:#fff;">${maxTemp || "--"}°C</strong> · 风扇 <strong style="color:#fff;">${hw.fanRpm || 0} RPM</strong></div>
              <div>内存: <strong style="color:#fff;">${sys.usedRamGb || "--"}/${sys.totalRamGb || "--"} GB</strong> · ${esc(formatPowerBadge(sys))}</div>
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>最近 60 秒 APU 功耗与 GPU 趋势</span></div>
            <div id="qamOsdSparkline">
              ${buildSvgChart(sys.apuWattsHistory || [], sys.gpuUsageHistory || [], 85, 100, "#2dd4bf", "#38bdf8", "APU W", "GPU %")}
            </div>
          </div>
        </div>
      `;
    }

    if (state.qamTab === "controls") {
      const gpd = snap.gpdController || {};
      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>手柄桌面鼠标模式</span>
              ${renderSwitch("swQamMouseMode", !!settings.gamepadMouseModeEnabled)}
            </div>
            <p style="font-size:11.5px;color:var(--text-secondary);margin:0;">
              左摇杆移光标 · A左键 · B右键 · X中键 · 右摇杆滚轮 · LB降速 / RB加速。
            </p>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>GPD 背键板载状态</span>
              <span class="hh-card-badge ${gpd.connected ? "live" : ""}">${gpd.connected ? "V2 已连接" : "未检测到"}</span>
            </div>
            <div style="font-size:11.5px;color:var(--text-secondary);margin-bottom:8px;">
              L4: ${esc(gpd.l4KeyLabel || "未知")} · R4: ${esc(gpd.r4KeyLabel || "未知")}
            </div>
            <div style="display:flex;gap:6px;flex-direction:column;">
              <button id="btnQamApplyRecGpd" class="btn btn-sm btn-primary" data-focusable="true" ${!gpd.connected ? "disabled" : ""}>一键设推荐背键 (L4=F13侧栏 / R4=F14OSD)</button>
              <button id="btnSendInsert" class="btn btn-sm" data-focusable="true">向游戏发送 Insert（呼出 OptiScaler 菜单）</button>
            </div>
          </div>
        </div>
      `;
    }

    // QAM Tab 4: OptiScaler Tuning + Inline Game Switcher & Real Status Banner
    const inj = active?.injection;
    const cfg = state.config;
    const presets = snap.presets || [];
    const statusText = inj?.isInjected
      ? `已注入 · ${formatConfigSummary(inj.appliedConfig || cfg, true)}`
      : "当前目标原生未注入（切换上方游戏可即时查看各自真实注入状态）";

    return `
      <div class="qam-body">
        <div class="qam-game-switcher" aria-label="快捷切游戏查看注入状态">
          ${games.map((g) => `
            <button class="qam-game-pill ${active && active.id === g.id ? "active" : ""}"
                    data-select-game="${esc(g.id)}"
                    title="${esc(g.title)}"
                    data-focusable="true">${g.injection?.isInjected ? "● " : ""}${esc(g.title)}</button>
          `).join("")}
        </div>

        <div id="qamStatusBanner" class="qam-status-banner ${inj?.isInjected ? "injected" : "uninjected"}">
          <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
            <strong style="font-size:12.5px;color:#fff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${esc(active?.title || "未选择游戏")}</strong>
            <span class="hh-card-badge ${inj?.isInjected ? "live" : "warn"}">${inj?.isInjected ? "已注入" : "原生未注入"}</span>
          </div>
          <span style="font-size:11px;color:var(--text-secondary);">${esc(statusText)}</span>
        </div>

        <div class="qam-section">
          <div class="qam-section-title"><span>预设与超分参数</span></div>
          <div class="qam-preset-row">
            ${presets.slice(0, 3).map((p) => `
              <button class="pill-btn small ${state.selectedPresetId === p.id ? "active" : ""}" data-preset="${esc(p.id)}" data-focusable="true">${esc(p.name.split("·")[0].trim())}</button>
            `).join("")}
          </div>
          <div style="display:flex;flex-direction:column;gap:6px;margin-top:6px;">
            ${renderSegmented("data-sr-mode", [
              { value: "fsr4_int8", label: "FSR4" },
              { value: "fsr4_int8_lite", label: "4 Lite" },
              { value: "xess", label: "XeSS" },
              { value: "fsr31", label: "FSR3" }
            ], cfg.srMode, "compact")}
            ${renderSegmented("data-fg-type", [
              { value: "xefg", label: "XeFG" },
              { value: "optifg", label: "OptiFG" },
              { value: "nofg", label: "关闭插帧" }
            ], cfg.fgType, "compact")}
            ${renderSegmented("data-fps-limit", [
              { value: "0", label: "不限帧" },
              { value: "60", label: "60帧" },
              { value: "90", label: "90帧" },
              { value: "116", label: "116" }
            ], String(cfg.framerateLimit || 0), "compact")}
            ${renderSegmented("data-vsync-mode", [
              { value: "on", label: "防撕裂" },
              { value: "auto", label: "跟随" },
              { value: "off", label: "关VSync" }
            ], cfg.vsyncMode || "on", "compact")}
          </div>
        </div>

        <div style="display:flex;flex-direction:column;gap:6px;">
          <button id="btnInject" class="btn btn-primary" data-focusable="true" ${!active || state.busy ? "disabled" : ""}>
            ${svgIcon("bolt")}
            <span>${inj?.isInjected ? "应用参数并重新注入" : "一键注入推荐配置"}</span>
          </button>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;">
            <button id="btnSendInsert" class="btn btn-sm" data-focusable="true">游戏内菜单</button>
            <button id="btnRestore" class="btn btn-sm btn-danger-soft" data-focusable="true" ${!inj?.isInjected || state.busy ? "disabled" : ""}>还原原版</button>
          </div>
        </div>
      </div>
    `;
  }

  function renderFooterLegend() {
    const legend = document.getElementById("gamepadLegend");
    if (!legend) return;

    if (IS_QAM) {
      const showXInject = state.qamTab === "opti";
      legend.innerHTML = `
        <div class="legend-group" style="gap:10px;">
          <span class="legend-item"><span class="gp-btn-glyph a">A</span>确认</span>
          <span class="legend-item"><span class="gp-btn-glyph b">B</span>关闭</span>
          ${showXInject ? `<span class="legend-item"><span class="gp-btn-glyph x">X</span>注入</span>` : ""}
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
    const settings = state.snapshot?.settings || {};
    const inOptiTune = state.tab === "opti" && state.optiSubTab === "tune";
    legend.innerHTML = `
      <div class="legend-group">
        <span class="legend-item"><span class="gp-btn-glyph a">A</span>确认 / 选择</span>
        <span class="legend-item"><span class="gp-btn-glyph b">B</span>返回 / 折叠</span>
        ${inOptiTune ? `<span class="legend-item"><span class="gp-btn-glyph x">X</span>一键注入</span>` : ""}
        <span class="legend-item"><span class="gp-btn-glyph y">Y</span>游戏内菜单</span>
        <span class="legend-item"><span class="keycap-pill" style="height:18px;font-size:10px;">LB/RB</span>切换分页</span>
      </div>
      <div class="legend-group" style="color:var(--text-muted);font-size:11px;">
        <span>TDP 设定: ${settings.currentTdpWatts || 45}W · 风扇: ${esc(formatFanModeLabel(settings.fanMode))}</span>
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
        btn.classList.toggle("active", btn.dataset.nav === state.tab);
      });
    }

    const mainView = document.getElementById("mainView");
    if (mainView) {
      if (IS_QAM) {
        mainView.innerHTML = renderQamView();
      } else if (state.tab === "handheld") {
        mainView.innerHTML = renderHandheldView();
      } else if (state.tab === "monitor") {
        mainView.innerHTML = renderMonitorView();
      } else if (state.tab === "controls") {
        mainView.innerHTML = renderControlsView();
      } else {
        mainView.innerHTML = renderOptiContainerView();
      }
    }

    renderFooterLegend();
    bindViewEvents();
    applySpatialFocus();
  }

  function armKillGame() {
    if (state.killArmed) {
      state.killArmed = false;
      if (state.killArmTimer) clearTimeout(state.killArmTimer);
      state.killArmTimer = null;
      return true;
    }
    state.killArmed = true;
    if (state.killArmTimer) clearTimeout(state.killArmTimer);
    state.killArmTimer = setTimeout(() => {
      state.killArmed = false;
      state.killArmTimer = null;
      render();
    }, 3500);
    render();
    showToast("再次点击「确认强关」将在 3.5 秒内结束游戏进程", "warn");
    return false;
  }

  function bindViewEvents() {
    const applySys = async (method, payload, okMsg = null) => {
      try {
        const updated = await rpc(method, payload);
        if (state.snapshot && updated) state.snapshot.systemControl = updated;
        render();
        if (okMsg) showToast(okMsg, "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    };

    const setTdpWatts = async (watts, savePreset = null) => {
      const clamped = Math.max(12, Math.min(85, Math.round(watts)));
      try {
        const res = await rpc("setTdp", savePreset ? { watts: clamped, savePreset } : { watts: clamped });
        if (res?.systemControl && state.snapshot) {
          state.snapshot.systemControl = res.systemControl;
        }
        showToast(res?.message || `目标 TDP 已设为 ${clamped}W`, res?.hardwareApplied ? "success" : "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    };

    // Handheld TDP, Linkage, Fan & Toolbox events
    document.querySelectorAll("[data-tdp-preset]").forEach((el) => {
      el.addEventListener("click", () => {
        const watts = Number(el.dataset.watts);
        if (watts > 0) setTdpWatts(watts);
      });
    });

    document.querySelectorAll("[data-save-tdp-preset]").forEach((el) => {
      el.addEventListener("click", () => {
        const curW = state.snapshot?.settings?.currentTdpWatts || 45;
        setTdpWatts(curW, el.dataset.saveTdpPreset);
      });
    });

    const tdpRange = document.getElementById("tdpRangeInput");
    tdpRange?.addEventListener("input", () => {
      const readout = document.getElementById("tdpValReadout");
      if (readout) readout.textContent = `${tdpRange.value} W`;
    });
    tdpRange?.addEventListener("change", () => setTdpWatts(Number(tdpRange.value)));

    document.getElementById("btnTdpDec")?.addEventListener("click", () => {
      const step = IS_QAM ? 2 : 1;
      setTdpWatts((state.snapshot?.settings?.currentTdpWatts || 45) - step);
    });
    document.getElementById("btnTdpInc")?.addEventListener("click", () => {
      const step = IS_QAM ? 2 : 1;
      setTdpWatts((state.snapshot?.settings?.currentTdpWatts || 45) + step);
    });

    document.getElementById("swPowerLinkage")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings || {};
      await rpc("setPowerLinkage", {
        enabled: !s.powerLinkageEnabled,
        batteryWatts: s.batteryTdpWatts || 25,
        acWatts: s.acTdpWatts || 55
      });
      showToast(!s.powerLinkageEnabled ? "已开启插拔电自动切换 TDP" : "已关闭插拔电自动切换", "info");
    });

    const stepLinkage = async (isAc, delta) => {
      const s = state.snapshot?.settings || {};
      const bat = isAc ? (s.batteryTdpWatts || 25) : Math.max(12, Math.min(85, (s.batteryTdpWatts || 25) + delta));
      const ac = isAc ? Math.max(12, Math.min(85, (s.acTdpWatts || 55) + delta)) : (s.acTdpWatts || 55);
      await rpc("setPowerLinkage", { enabled: !!s.powerLinkageEnabled, batteryWatts: bat, acWatts: ac });
    };
    document.getElementById("btnBatTdpDec")?.addEventListener("click", () => stepLinkage(false, -2));
    document.getElementById("btnBatTdpInc")?.addEventListener("click", () => stepLinkage(false, 2));
    document.getElementById("btnAcTdpDec")?.addEventListener("click", () => stepLinkage(true, -2));
    document.getElementById("btnAcTdpInc")?.addEventListener("click", () => stepLinkage(true, 2));

    document.querySelectorAll("[data-fan-mode]").forEach((el) => {
      el.addEventListener("click", async () => {
        try {
          const mode = el.dataset.fanMode;
          const manualPct = state.snapshot?.settings?.manualFanPercent || 65;
          const res = await rpc("setFanMode", { mode, manualPercent: manualPct });
          showToast(res?.message || `风扇模式已切换为 ${mode}`, res?.hardwareApplied ? "success" : "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    const fanRange = document.getElementById("fanManualRange");
    fanRange?.addEventListener("input", () => {
      const val = document.getElementById("fanManualVal");
      if (val) val.textContent = `${fanRange.value}%`;
    });
    fanRange?.addEventListener("change", async () => {
      try {
        const res = await rpc("setFanMode", { mode: "manual", manualPercent: Number(fanRange.value) });
        showToast(res?.message || `风扇手动占空比 ${fanRange.value}%`, "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnInstallHwSvc")?.addEventListener("click", async () => {
      try {
        showToast("正在请求一次性管理员授权安装 HaloDeckHardwareSvc...");
        const res = await rpc("installHardwareService", {});
        showToast(res?.message || "底层硬件服务已启用", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnToggleGpdSvc")?.addEventListener("click", async () => {
      try {
        const running = !!state.snapshot?.systemControl?.hardwareService?.gpdServiceRunning;
        const res = await rpc("toggleGpdService", { paused: running });
        showToast(res?.message || "已完成服务切换", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-cpu-max-mhz]").forEach((el) => {
      el.addEventListener("click", async () => {
        const mhz = Number(el.dataset.cpuMaxMhz);
        await applySys("setCpuMaxFreq", { mhz }, mhz === 0 ? "CPU 频率上限已恢复不限" : `CPU 最大频率已限为 ${(mhz / 1000).toFixed(1)} GHz`);
      });
    });

    document.querySelectorAll("[data-toolbox]").forEach((el) => {
      el.addEventListener("click", async () => {
        try {
          const res = await rpc("runToolboxAction", { action: el.dataset.toolbox });
          showToast(res?.message || "操作完成", "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.querySelectorAll("[data-resume-pid]").forEach((el) => {
      el.addEventListener("click", async () => {
        try {
          const res = await rpc("runToolboxAction", { action: "toggle_fridge", pid: Number(el.dataset.resumePid) });
          showToast(res?.message || "已唤醒挂起游戏", "success");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.getElementById("btnKillGame")?.addEventListener("click", async () => {
      if (!armKillGame()) return;
      try {
        const res = await rpc("runToolboxAction", { action: "kill_game" });
        showToast(res?.message || "已强关游戏", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    const toggleMouseMode = async () => {
      const enabled = !state.snapshot?.settings?.gamepadMouseModeEnabled;
      await rpc("setGamepadMouseMode", { enabled });
      showToast(enabled ? "已开启手柄桌面鼠标模式" : "已关闭手柄桌面鼠标模式", enabled ? "success" : "info");
    };
    document.getElementById("btnToolboxMouse")?.addEventListener("click", toggleMouseMode);
    document.getElementById("swMouseMode")?.addEventListener("click", toggleMouseMode);
    document.getElementById("swQamMouseMode")?.addEventListener("click", toggleMouseMode);

    // Per-game performance profile
    const saveGamePerf = async (patch) => {
      const active = getActiveGame();
      if (!active) return;
      const curMap = state.snapshot?.settings?.perGameProfiles || {};
      const existing = curMap[active.id] || {
        gameId: active.id,
        gameName: active.title,
        enabled: false,
        tdpWatts: state.snapshot?.settings?.currentTdpWatts || 45,
        refreshRateHz: state.snapshot?.systemControl?.refreshRateHz || 120
      };
      await rpc("saveGamePerfProfile", { ...existing, ...patch, gameId: active.id, gameName: active.title });
    };
    document.getElementById("swGamePerfEnabled")?.addEventListener("click", () => {
      const active = getActiveGame();
      const cur = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.enabled;
      saveGamePerf({ enabled: !cur });
    });
    document.getElementById("btnGamePerfTdpDec")?.addEventListener("click", () => {
      const active = getActiveGame();
      const curW = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.tdpWatts || state.snapshot?.settings?.currentTdpWatts || 45;
      saveGamePerf({ tdpWatts: Math.max(12, curW - 5) });
    });
    document.getElementById("btnGamePerfTdpInc")?.addEventListener("click", () => {
      const active = getActiveGame();
      const curW = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.tdpWatts || state.snapshot?.settings?.currentTdpWatts || 45;
      saveGamePerf({ tdpWatts: Math.min(85, curW + 5) });
    });
    document.querySelectorAll("[data-game-perf-hz]").forEach((el) => {
      el.addEventListener("click", () => saveGamePerf({ refreshRateHz: Number(el.dataset.gamePerfHz) }));
    });

    // Monitor & OSD events
    document.querySelectorAll("[data-osd-level]").forEach((el) => {
      el.addEventListener("click", async () => {
        await rpc("setOverlayHud", { level: Number(el.dataset.osdLevel) });
      });
    });
    document.querySelectorAll("[data-osd-pos]").forEach((el) => {
      el.addEventListener("click", async () => {
        await rpc("setOverlayHud", { position: el.dataset.osdPos });
      });
    });
    document.querySelectorAll("[data-osd-opacity]").forEach((el) => {
      el.addEventListener("click", async () => {
        await rpc("setOverlayHud", { opacity: Number(el.dataset.osdOpacity) });
      });
    });

    // Controls & GPD V2 events
    document.getElementById("btnReadGpd")?.addEventListener("click", async () => {
      try {
        const res = await rpc("getGpdController", { forceRefresh: true });
        if (state.snapshot && res) {
          state.snapshot.gpdController = res;
          syncGpdDraft(res);
          state.gpdLoaded = true;
        }
        showToast(res?.statusMessage || "已读取 GPD 手柄板载 Flash", res?.connected ? "success" : "warn");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.querySelectorAll("[data-gpd-l4]").forEach((el) => {
      el.addEventListener("click", () => {
        state.gpdDraft.l4Key = Number(el.dataset.gpdL4);
        render();
      });
    });
    document.querySelectorAll("[data-gpd-r4]").forEach((el) => {
      el.addEventListener("click", () => {
        state.gpdDraft.r4Key = Number(el.dataset.gpdR4);
        render();
      });
    });
    document.querySelectorAll("[data-gpd-rumble]").forEach((el) => {
      el.addEventListener("click", () => {
        state.gpdDraft.rumble = Number(el.dataset.gpdRumble);
        render();
      });
    });
    document.getElementById("btnGpdLdzDec")?.addEventListener("click", () => {
      state.gpdDraft.leftDeadzone = Math.max(-10, state.gpdDraft.leftDeadzone - 1);
      render();
    });
    document.getElementById("btnGpdLdzInc")?.addEventListener("click", () => {
      state.gpdDraft.leftDeadzone = Math.min(10, state.gpdDraft.leftDeadzone + 1);
      render();
    });
    document.getElementById("btnGpdRdzDec")?.addEventListener("click", () => {
      state.gpdDraft.rightDeadzone = Math.max(-10, state.gpdDraft.rightDeadzone - 1);
      render();
    });
    document.getElementById("btnGpdRdzInc")?.addEventListener("click", () => {
      state.gpdDraft.rightDeadzone = Math.min(10, state.gpdDraft.rightDeadzone + 1);
      render();
    });
    document.getElementById("btnSaveGpd")?.addEventListener("click", async () => {
      try {
        const res = await rpc("saveGpdController", {
          l4Key: state.gpdDraft.l4Key >= 0 ? state.gpdDraft.l4Key : null,
          r4Key: state.gpdDraft.r4Key >= 0 ? state.gpdDraft.r4Key : null,
          rumble: state.gpdDraft.rumble,
          leftDeadzone: state.gpdDraft.leftDeadzone,
          rightDeadzone: state.gpdDraft.rightDeadzone
        });
        if (res && state.snapshot) {
          state.snapshot.gpdController = res;
          syncGpdDraft(res);
        }
        showToast(res?.statusMessage || "已写入 GPD 手柄板载 Flash", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.getElementById("btnQamApplyRecGpd")?.addEventListener("click", async () => {
      try {
        const d = state.gpdDraft;
        const res = await rpc("saveGpdController", {
          l4Key: 0x0068,
          r4Key: 0x0069,
          rumble: d.rumble,
          leftDeadzone: d.leftDeadzone,
          rightDeadzone: d.rightDeadzone
        });
        if (res && state.snapshot) {
          state.snapshot.gpdController = res;
          syncGpdDraft(res);
        }
        showToast("已将 L4 设为 F13 侧栏、R4 设为 F14 OSD", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    // Hotkeys & Settings
    document.getElementById("btnRecordQamCombo")?.addEventListener("click", () => startRecordCombo("qam"));
    document.getElementById("btnRecordOsdCombo")?.addEventListener("click", () => startRecordCombo("osd"));
    document.getElementById("btnRecordMouseCombo")?.addEventListener("click", () => startRecordCombo("mouse"));
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

    document.getElementById("btnHotkeyOsd")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 3, virtualKey: 0x4D, displayText: "Ctrl + Alt + M" },
        { modifiers: 6, virtualKey: 0x4D, displayText: "Ctrl + Shift + M" },
        { modifiers: 0, virtualKey: 0x79, displayText: "F10" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.osdHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, osdHotkey: next });
      showToast(`OSD 键盘热键已切换为 ${next.displayText}`, "success");
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

    // Display / Audio / CPU system controls (used across Handheld & QAM)
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
        if (state.snapshot) state.snapshot.vrrWindowedEnabled = true;
        showToast(res?.message || "已开启 Win11 窗口化 VRR 优化", "success");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    // OptiScaler Sub-navigation
    document.querySelectorAll("[data-opti-sub]").forEach((el) => {
      el.addEventListener("click", () => {
        state.optiSubTab = el.dataset.optiSub;
        state.focusIndex = 0;
        render();
      });
    });

    // OptiScaler Tune / Library / Components events
    document.querySelectorAll("[data-preset]").forEach((el) => {
      el.addEventListener("click", () => applyPreset(el.dataset.preset));
    });
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
        if (state.snapshot) state.snapshot.vrrWindowedEnabled = true;
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
        if (res?.memoryHealth && state.snapshot) state.snapshot.memoryHealth = res.memoryHealth;
        showToast(res?.message || "已扩容虚拟内存分页文件，请重启后生效", "success");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-select-game]").forEach((el) => {
      el.addEventListener("click", async () => {
        state._userModifiedConfig = false;
        await rpc("selectRunningFallback", { gameId: el.dataset.selectGame });
        if (IS_QAM) {
          state.qamTab = "opti";
          state.focusIndex = 0;
        }
        syncConfigFromActiveGame(state.snapshot);
        render();
      });
    });

    document.getElementById("btnMoreGames")?.addEventListener("click", () => {
      state.tab = "opti";
      state.optiSubTab = "library";
      state.focusIndex = 0;
      render();
    });

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

    // Library
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
        state.tab = "opti";
        state.optiSubTab = "tune";
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
        state.tab = "opti";
        state.optiSubTab = "tune";
        render();
        showToast(`已添加 ${entry.title}`, "success");
      }
    };
    document.getElementById("btnAddExeInline")?.addEventListener("click", addExeHandler);
    document.getElementById("btnAddExeLib")?.addEventListener("click", addExeHandler);

    // Components
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

    // QAM Footer
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
      else if (target === "osd") next.osdGamepadCombo = { buttons };
      else if (target === "mouse") next.mouseModeGamepadCombo = { buttons };
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
        if (!modalOpen && ((state.tab === "opti" && state.optiSubTab === "tune") || (IS_QAM && state.qamTab === "opti"))) {
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
      state.tab = btn.dataset.nav || "handheld";
      state.focusIndex = 0;
      render();
    });
  });

  document.querySelectorAll(".qam-rail-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.qamTab = btn.dataset.qamTab || "handheld";
      state.focusIndex = 0;
      render();
    });
  });

  document.getElementById("topApuChip")?.addEventListener("click", () => {
    state.tab = "monitor";
    state.focusIndex = 0;
    render();
  });

  document.getElementById("topSysChip")?.addEventListener("click", () => {
    state.tab = "handheld";
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
