(() => {
  "use strict";

  // Global error tracking for GUI self-verify diagnostics
  window.__hoErrors = [];
  window.addEventListener("error", (e) => window.__hoErrors.push(e.message || String(e)));
  window.addEventListener("unhandledrejection", (e) => window.__hoErrors.push(String(e.reason || e)));

  const IS_QAM = new URLSearchParams(window.location.search).get("mode") === "qam";
  document.body.classList.toggle("mode-qam", IS_QAM);
  document.body.classList.toggle("mode-console", !IS_QAM);

  const CONSOLE_TABS = ["handheld", "opti", "monitor", "controls", "plugins"];
  const OPTI_SUB_TABS = ["tune", "library", "components"];
  const QAM_TABS = ["handheld", "opti", "osd", "controls", "plugins"];

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
    dxgiVramGb: 0,
    spoofedVendorId: "auto",
    spoofedDeviceId: "auto",
    spoofedGpuName: "",
    streamlineSpoofing: true,
    shortcutKeyHex: "0x2D",
    enableFsr4Int8Hint: true,
    fsr4Watermark: false,
    framerateLimit: 0,
    vsyncMode: "on",
    xeFgMultiplier: 2,
    xeFgDynamicMfg: false,
    xeFgDynamicTargetFps: 0
  };

  const GPD_BACK_GAMEPAD_OPTIONS = [
    { value: 0x800F, label: "手柄 L3" },
    { value: 0x8010, label: "手柄 R3" },
    { value: 0x8007, label: "手柄 A" },
    { value: 0x8008, label: "手柄 B" },
    { value: 0x8009, label: "手柄 X" },
    { value: 0x800A, label: "手柄 Y" },
    { value: 0x800B, label: "手柄 LB" },
    { value: 0x800C, label: "手柄 RB" },
    { value: 0x800D, label: "手柄 LT" },
    { value: 0x800E, label: "手柄 RT" },
    { value: 0x8000, label: "十字 ↑" },
    { value: 0x8001, label: "十字 ↓" },
    { value: 0x8002, label: "十字 ←" },
    { value: 0x8003, label: "十字 →" },
    { value: 0x8004, label: "Start" },
    { value: 0x8005, label: "Select" }
  ];

  const GPD_BACK_KEYBOARD_OPTIONS = [
    { value: 0x0000, label: "恢复原厂 / 清空背键 (NC)" },
    { value: 0x002C, label: "Space 跳跃" },
    { value: 0x00E1, label: "Shift 冲刺" },
    { value: 0x00E0, label: "Ctrl 下蹲" },
    { value: 0x0008, label: "E 互动" },
    { value: 0x0009, label: "F 互动" },
    { value: 0x0015, label: "R 换弹" },
    { value: 0x002B, label: "Tab" },
    { value: 0x0029, label: "Esc" },
    { value: 0x0068, label: "F13 (呼出侧栏)" },
    { value: 0x0069, label: "F14 (切换帧率窗)" },
    { value: 0x006A, label: "F15 (超分菜单)" },
    { value: 0x006B, label: "F16 (呼出前端主界面)" },
    { value: 0x0049, label: "Insert (超分菜单)" }
  ];

  const GPD_FULL_KEYBOARD_GROUPS = (() => {
    const letters = [];
    for (let i = 0; i < 26; i++) {
      letters.push({ value: 0x0004 + i, label: String.fromCharCode(65 + i) });
    }
    const numbers = [];
    for (let i = 1; i <= 9; i++) {
      numbers.push({ value: 0x001E + (i - 1), label: String(i) });
    }
    numbers.push({ value: 0x0027, label: "0" });
    numbers.push({ value: 0x002D, label: "-" }, { value: 0x002E, label: "=" }, { value: 0x0035, label: "`" });

    const fnKeys = [];
    for (let i = 1; i <= 12; i++) {
      fnKeys.push({ value: 0x003A + (i - 1), label: `F${i}` });
    }
    fnKeys.push(
      { value: 0x0068, label: "F13 侧栏", special: true },
      { value: 0x0069, label: "F14 帧率窗", special: true },
      { value: 0x006A, label: "F15 超分", special: true },
      { value: 0x006B, label: "F16 主界面", special: true }
    );

    const modifiers = [
      { value: 0x00E0, label: "LCtrl" },
      { value: 0x00E1, label: "LShift" },
      { value: 0x00E2, label: "LAlt" },
      { value: 0x00E3, label: "LWin" },
      { value: 0x00E4, label: "RCtrl" },
      { value: 0x00E5, label: "RShift" },
      { value: 0x00E6, label: "RAlt" },
      { value: 0x00E7, label: "RWin" }
    ];

    const controls = [
      { value: 0x0029, label: "Esc" },
      { value: 0x002B, label: "Tab" },
      { value: 0x002C, label: "Space" },
      { value: 0x0028, label: "Enter" },
      { value: 0x002A, label: "Backspace" },
      { value: 0x0049, label: "Insert" },
      { value: 0x004C, label: "Delete" },
      { value: 0x004A, label: "Home" },
      { value: 0x004D, label: "End" },
      { value: 0x004B, label: "PgUp" },
      { value: 0x004E, label: "PgDn" },
      { value: 0x0046, label: "PrtSc" },
      { value: 0x0052, label: "↑" },
      { value: 0x0051, label: "↓" },
      { value: 0x0050, label: "←" },
      { value: 0x004F, label: "→" }
    ];

    return [
      { title: "字母键 (A–Z)", keys: letters },
      { title: "数字与符号", keys: numbers },
      { title: "功能键 (F1–F16)", keys: fnKeys },
      { title: "修饰键", keys: modifiers },
      { title: "控制与方向键", keys: controls }
    ];
  })();

  const CODE_TO_HID_MAP = (() => {
    const m = {
      ControlLeft: 0x00E0, ShiftLeft: 0x00E1, AltLeft: 0x00E2, MetaLeft: 0x00E3,
      ControlRight: 0x00E4, ShiftRight: 0x00E5, AltRight: 0x00E6, MetaRight: 0x00E7,
      Enter: 0x0028, Escape: 0x0029, Backspace: 0x002A, Tab: 0x002B, Space: 0x002C,
      Insert: 0x0049, Home: 0x004A, PageUp: 0x004B, Delete: 0x004C, End: 0x004D, PageDown: 0x004E,
      ArrowRight: 0x004F, ArrowLeft: 0x0050, ArrowDown: 0x0051, ArrowUp: 0x0052
    };
    for (let i = 0; i < 26; i++) {
      m[`Key${String.fromCharCode(65 + i)}`] = 0x0004 + i;
    }
    for (let i = 1; i <= 9; i++) {
      m[`Digit${i}`] = 0x001E + (i - 1);
    }
    m.Digit0 = 0x0027;
    for (let i = 1; i <= 12; i++) {
      m[`F${i}`] = 0x003A + (i - 1);
    }
    m.F13 = 0x0068;
    m.F14 = 0x0069;
    m.F15 = 0x006A;
    m.F16 = 0x006B;
    return m;
  })();

  const SHORT_HID_LABELS = (() => {
    const map = new Map([
      [0x8000, "↑"], [0x8001, "↓"], [0x8002, "←"], [0x8003, "→"],
      [0x8004, "Start"], [0x8005, "Select"], [0x8006, "Guide"],
      [0x8007, "A"], [0x8008, "B"], [0x8009, "X"], [0x800A, "Y"],
      [0x800B, "LB"], [0x800C, "RB"], [0x800D, "LT"], [0x800E, "RT"],
      [0x800F, "L3"], [0x8010, "R3"],
      [0x0028, "Enter"], [0x0029, "Esc"], [0x002A, "Backspace"], [0x002B, "Tab"], [0x002C, "Space"],
      [0x0049, "Insert"], [0x004A, "Home"], [0x004B, "PgUp"], [0x004C, "Del"], [0x004D, "End"], [0x004E, "PgDn"],
      [0x004F, "→"], [0x0050, "←"], [0x0051, "↓"], [0x0052, "↑"],
      [0x00E0, "Ctrl"], [0x00E1, "Shift"], [0x00E2, "Alt"], [0x00E3, "Win"],
      [0x00E4, "RCtrl"], [0x00E5, "RShift"], [0x00E6, "RAlt"], [0x00E7, "RWin"],
      [0x0068, "F13"], [0x0069, "F14"], [0x006A, "F15"], [0x006B, "F16"]
    ]);
    for (let i = 0; i < 26; i++) map.set(0x0004 + i, String.fromCharCode(65 + i));
    for (let i = 1; i <= 9; i++) map.set(0x001E + (i - 1), String(i));
    map.set(0x0027, "0");
    for (let i = 1; i <= 12; i++) map.set(0x003A + (i - 1), `F${i}`);
    return map;
  })();

  function resolveShortHidLabel(code) {
    if (SHORT_HID_LABELS.has(code)) return SHORT_HID_LABELS.get(code);
    return `0x${Number(code || 0).toString(16).toUpperCase().padStart(4, "0")}`;
  }

  function resolveBackKeyLabel(code) {
    if (typeof code !== "number" || code < 0) return "保持现有多步宏";
    if (code === 0) return "未映射 (NC)";
    const match = GPD_BACK_GAMEPAD_OPTIONS.find((o) => o.value === code) ||
      GPD_BACK_KEYBOARD_OPTIONS.find((o) => o.value === code);
    if (match) return match.label;
    if (SHORT_HID_LABELS.has(code)) return SHORT_HID_LABELS.get(code);
    return `HID 0x${code.toString(16).toUpperCase().padStart(4, "0")}`;
  }

  function normalizeComboCodesList(codes) {
    const list = [];
    (codes || []).forEach((c) => {
      const n = Number(c);
      if (Number.isFinite(n) && n > 0 && !list.includes(n) && list.length < 4) {
        list.push(n);
      }
    });
    const mods = list.filter((c) => c >= 0x00E0 && c <= 0x00E7);
    const others = list.filter((c) => c < 0x00E0 || c > 0x00E7);
    return [...mods, ...others];
  }

  function resolveBackSlotLabel(singleCode, comboCodes) {
    if (Array.isArray(comboCodes) && comboCodes.length >= 2) {
      return comboCodes.map(resolveShortHidLabel).join(" + ");
    }
    if (Array.isArray(comboCodes) && comboCodes.length === 1) {
      return resolveBackKeyLabel(comboCodes[0]);
    }
    return resolveBackKeyLabel(singleCode);
  }

  const UI_THEMES = [
    { id: "aurora", shortLabel: "极光", label: "极光深色", desc: "冰蓝青绿半透毛玻璃 · 掌机沉浸暗色" },
    { id: "frost", shortLabel: "凝霜", label: "凝霜浅色", desc: "陶瓷云母白昼质感 · 室内强光清晰高对比" }
  ];

  const UI_LAYOUTS = [
    { id: "bento", shortLabel: "磁贴", label: "掌机大磁贴风", desc: "一屏零滚动 · 大触控胶囊滑条与情景磁贴" },
    { id: "cockpit", shortLabel: "仪表盘", label: "桌面仪表盘风", desc: "顶部实时遥测带 · 三栏高密度参数矩阵" }
  ];

  function resolveTdpSceneLabel(w) {
    if (w <= 12) return "长续航";
    if (w <= 20) return "省电模式";
    if (w <= 30) return "均衡模式";
    if (w <= 50) return "性能模式";
    if (w <= 75) return "满血模式";
    return "极速释放";
  }

  const state = {
    tab: "handheld",
    optiSubTab: "tune",
    qamTab: "handheld",
    qamOptiSubView: "tune",
    libraryLayout: "posters",
    snapshot: null,
    activeGameId: null,
    selectedPresetId: "strix-halo-quality",
    _userModifiedConfig: false,
    config: { ...DEFAULT_CONFIG },
    advancedOpen: false,
    powerAdvancedOpen: false,
    fanNodesOpen: false,
    cpuAdvancedOpen: false,
    controlsAdvancedOpen: false,
    searchQuery: "",
    libraryFilter: "all",
    activePluginId: null,
    pluginReloadNonce: 0,
    pluginsSubTab: "installed",
    qamPluginSubTab: "enabled",
    pluginStoreData: null,
    pluginStoreLoading: false,
    pluginStoreSearch: "",
    pluginStoreCategory: "all",
    pluginCustomSourceDraft: "",
    pluginDirectUrlDraft: "",
    pluginMirrorDraft: null,
    pluginDownloadProgress: null,
    appUpdateChecking: false,
    appUpdateProgress: null,
    appUpdateIncludePrerelease: false,
    focusIndex: 0,
    lastInputModality: "pointer",
    isDraggingRange: false,
    activeSliderDrag: null,
    dragCooldownUntil: 0,
    pendingSnapshot: null,
    scrollPositions: {},
    lastScrollRouteKey: "",
    recordingTarget: null,
    progressBySlot: {},
    busy: false,
    killArmed: false,
    killArmTimer: null,
    gpdLoaded: false,
    gpdFetching: false,
    gpdSaving: false,
    menuKeyDiagRunning: false,
    menuKeyDiagSecLeft: 0,
    menuKeyDiagReport: null,
    lastSyncedGpdSig: "",
    gpdDraft: {
      l4Key: -1,
      r4Key: -1,
      l4Combo: [],
      r4Combo: [],
      rumble: 1,
      leftDeadzone: 0,
      rightDeadzone: 0
    },
    backModal: {
      open: false,
      slot: "l4",
      tab: "single",
      recordingActive: false,
      recordedCodes: [],
      comboCodes: []
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
    } else if (name === "navigateConsoleTab" && data && !IS_QAM) {
      if (data.tab && CONSOLE_TABS.includes(data.tab)) {
        state.tab = data.tab;
        state.activePluginId = null;
        if (data.tab === "plugins" && data.subtab) {
          state.pluginsSubTab = data.subtab;
        }
        state.focusIndex = 0;
        render();
        if (state.tab === "plugins" && state.pluginsSubTab === "store" && !state.pluginStoreData) {
          ensurePluginStoreLoaded(false);
        }
      }
    } else if (name === "powerSettingsPatched" && data) {
      if (state.snapshot?.settings) {
        if (typeof data.currentTdpWatts === "number") state.snapshot.settings.currentTdpWatts = data.currentTdpWatts;
        if (typeof data.unlockThreeStagePower === "boolean") state.snapshot.settings.unlockThreeStagePower = data.unlockThreeStagePower;
        if (typeof data.slowPptWatts === "number") state.snapshot.settings.slowPptWatts = data.slowPptWatts;
        if (typeof data.fastPptWatts === "number") state.snapshot.settings.fastPptWatts = data.fastPptWatts;
      }
      if (state.snapshot?.systemControl?.hardwareService) {
        const hw = state.snapshot.systemControl.hardwareService;
        if (typeof data.appliedTdpWatts === "number") {
          hw.appliedTdpWatts = data.appliedTdpWatts;
          hw.hwStapmLimitWatts = data.appliedTdpWatts;
        }
        if (typeof data.appliedSlowWatts === "number") {
          hw.appliedSlowWatts = data.appliedSlowWatts;
          hw.hwSlowLimitWatts = data.appliedSlowWatts;
        }
        if (typeof data.appliedFastWatts === "number") {
          hw.appliedFastWatts = data.appliedFastWatts;
          hw.hwFastLimitWatts = data.appliedFastWatts;
        }
      }
      if (state.activeSliderDrag !== "tdp" && typeof data.currentTdpWatts === "number") {
        updateTdpUiImmediate(data.currentTdpWatts);
      }
    } else if (name === "controlValuePatched" && data) {
      if (state.snapshot?.systemControl) {
        const sys = state.snapshot.systemControl;
        if (typeof data.brightnessPercent === "number") sys.brightnessPercent = data.brightnessPercent;
        if (typeof data.volumePercent === "number") sys.volumePercent = data.volumePercent;
        if (typeof data.isMuted === "boolean") sys.isMuted = data.isMuted;
        if (typeof data.systemDrawWatts === "number") sys.systemDrawWatts = data.systemDrawWatts;
        if (data.estimatedStandbyMinutes !== undefined) sys.estimatedStandbyMinutes = data.estimatedStandbyMinutes;
        if (data.estimatedChargerWatts !== undefined) sys.estimatedChargerWatts = data.estimatedChargerWatts;
        if (typeof data.batteryEtaMinutes === "number") sys.batteryEtaMinutes = data.batteryEtaMinutes;
      }
      if (data.kind === "brightness" && state.activeSliderDrag !== "bright") {
        updateBrightnessUiImmediate(data.brightnessPercent);
        if (state.snapshot?.systemControl) patchLiveTelemetryDom(state.snapshot.systemControl);
      } else if (data.kind === "volume" && state.activeSliderDrag !== "vol") {
        updateVolumeUiImmediate(data.volumePercent, data.isMuted);
      }
    } else if (name === "backButtonGamepadRecorded" && Array.isArray(data?.codes)) {
      if (state.backModal.open && state.backModal.tab === "record" && data.codes.length > 0) {
        state.backModal.recordedCodes = normalizeComboCodesList(data.codes);
        state.backModal.recordingActive = false;
        if (typeof window.__hoRefreshBackModal === "function") window.__hoRefreshBackModal();
      }
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
    } else if (name === "pluginEvent" && data?.pluginId) {
      forwardEventToPluginFrame(data.pluginId, data.eventName || "message", data.data);
    } else if (name === "pluginThemeUpdated") {
      if (state.snapshot) state.snapshot.activePluginTheme = data?.variables && Object.keys(data.variables).length ? data : null;
      applyPluginThemeOverride(state.snapshot?.activePluginTheme);
      pushThemeToActivePluginFrame();
    } else if (name === "pluginDownloadProgress" && data) {
      state.pluginDownloadProgress = data.status === "done" ? null : { label: data.label || "正在下载插件...", percent: data.percent || 0 };
      const bar = document.getElementById("pluginDlProgressBanner");
      if (bar) {
        if (data.status === "done") {
          bar.classList.add("hidden");
        } else {
          bar.classList.remove("hidden");
          bar.textContent = `正在下载：${data.label || "插件"} · ${Math.round(data.percent || 0)}%`;
        }
      }
    } else if (name === "appUpdateProgress" && data) {
      state.appUpdateProgress = data.status === "idle" ? null : { version: data.version || "", percent: data.percent || 0, status: data.status || "downloading" };
      const btn = document.getElementById("btnApplyAppUpdate");
      const modalBtn = document.getElementById("btnModalApplyAppUpdate");
      const modalProg = document.getElementById("appUpdateModalProg");
      const text = data.status === "applying"
        ? "校验通过，正在重启更新..."
        : `下载中 ${Math.round(data.percent || 0)}%`;
      if (btn) {
        btn.textContent = text;
        btn.disabled = true;
      }
      if (modalBtn) {
        modalBtn.textContent = text;
        modalBtn.disabled = true;
      }
      if (modalProg) {
        modalProg.classList.remove("hidden");
        modalProg.textContent = text;
      }
    } else if (name === "toast" && data?.message) {
      showToast(data.message, data.kind || "info");
    }
  }

  // ---------------- Formatters & Live Telemetry ----------------
  function formatRefreshTag(sys) {
    if (!sys) return "--Hz";
    if (sys.isDynamicRefreshRate) {
      const baseHz = sys.baseRefreshRateHz || Math.round((sys.refreshRateHz || 120) / 2);
      return `动态 ${baseHz}–${sys.refreshRateHz || 120}Hz`;
    }
    return `${sys.refreshRateHz || "--"}Hz`;
  }

  function buildRefreshOptions(sys) {
    const baseRates = (sys?.supportedRefreshRates && sys.supportedRefreshRates.length)
      ? sys.supportedRefreshRates.map((h) => ({ value: String(h), label: `${h}Hz` }))
      : [{ value: "60", label: "60Hz" }, { value: "120", label: "120Hz" }];
    if (sys?.dynamicRefreshSupported || sys?.isDynamicRefreshRate) {
      return [...baseRates, { value: "drr", label: "动态" }];
    }
    return baseRates;
  }

  function formatStandbyEta(sys) {
    if (!sys?.hasBattery) return "无电池";
    const mins = (typeof sys.estimatedStandbyMinutes === "number" && sys.estimatedStandbyMinutes > 0)
      ? sys.estimatedStandbyMinutes
      : (sys.batteryEtaMinutes > 0 ? sys.batteryEtaMinutes : 0);
    if (!mins) return "计算中...";
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    return h > 0 ? `${h}h ${String(m).padStart(2, "0")}m` : `${m} 分钟`;
  }

  function computePowerFlowUi(sys, curTdp = 45) {
    if (!sys) {
      return {
        sysDrawNow: 0,
        modeText: "读取中",
        modeClass: "",
        sysVal: "-- W",
        sysSub: "读取电源传感器...",
        batVal: "--%",
        batSub: "--",
        batWarn: false,
        etaVal: "--",
        etaSub: "正在采样",
        badge: "--%",
        kpiBatSub: "整机功耗读取中"
      };
    }

    const apuPowerText = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : `目标 ${curTdp}W`;
    const fallbackApu = sys.apuPowerWatts != null ? Math.max(1.5, sys.apuPowerWatts) : 0;
    const sysDrawNow = typeof sys.systemDrawWatts === "number" && sys.systemDrawWatts > 0
      ? sys.systemDrawWatts
      : (fallbackApu > 0 ? fallbackApu * 1.18 + 3.4 + 5.2 * Math.max(0, Math.min(1, (fallbackApu - 9) / 14)) : 0);
    const sysValExact = sysDrawNow > 0 ? `${sysDrawNow.toFixed(1)} W` : "-- W";
    const sysValEst = sysDrawNow > 0 ? `≈${sysDrawNow.toFixed(1)} W` : "-- W";

    const mode = sys.powerRuntimeMode || (
      !sys.hasBattery
        ? "no_battery"
        : (sys.isAcOnline
          ? (sys.isBatteryDischarging && (sys.batteryDischargeWatts > 0.35 || sys.batteryPowerWatts > 0.35)
            ? "ac_discharging"
            : (sys.isCharging ? "charging" : "ac_sustained"))
          : "battery")
    );

    if (mode === "no_battery") {
      return {
        apuPowerText,
        sysDrawNow,
        modeText: "AC 直连 (无电池)",
        modeClass: "live",
        sysVal: sysValEst,
        sysSub: "AC 直供估算整机功耗",
        batVal: "无电池",
        batSub: "外接供电直连",
        batWarn: false,
        etaVal: "持续供电",
        etaSub: "未安装电池组",
        badge: "AC 直连 (无电池)",
        kpiBatSub: `整机 ≈${sysDrawNow.toFixed(1)}W · AC 无电池直连`
      };
    }

    if (mode === "charging") {
      const chgW = Math.max(0, Number(sys.batteryPowerWatts || 0));
      const estIn = sys.estimatedChargerWatts ?? (sysDrawNow + chgW);
      const chgEta = sys.chargeEtaMinutes > 0 ? formatEta(sys.chargeEtaMinutes) : "";
      return {
        apuPowerText,
        sysDrawNow,
        modeText: "正在充电",
        modeClass: "live",
        sysVal: sysValEst,
        sysSub: `不含充电估算 · 充电器 ≈${estIn.toFixed(1)}W`,
        batVal: chgW > 0 ? `+${chgW.toFixed(1)} W` : `${sys.batteryPercent}%`,
        batSub: `${sys.batteryPercent}% · 净充入电池${chgEta ? ` (${chgEta})` : ""}`,
        batWarn: false,
        etaVal: "正在充电",
        etaSub: chgEta ? `供电充足 · ${chgEta}充满` : "充电功率 > 整机功耗 · 持续供电",
        badge: `${sys.batteryPercent}% 正在充电${chgW > 0.5 ? ` (+${chgW.toFixed(0)}W)` : ""}`,
        kpiBatSub: `整机 ≈${sysDrawNow.toFixed(1)}W · 充电器 ≈${estIn.toFixed(1)}W`
      };
    }

    if (mode === "ac_sustained") {
      return {
        apuPowerText,
        sysDrawNow,
        modeText: sys.isBypassPower ? "AC 旁路直供" : "AC 持续供电",
        modeClass: "live",
        sysVal: sysValEst,
        sysSub: sys.isBypassPower ? "AC 旁路估算整机功耗" : "AC 外接估算整机功耗",
        batVal: `${sys.batteryPercent}%`,
        batSub: `电池未放电 · ${(sys.batteryRemainingWh || 0).toFixed(1)} Wh`,
        batWarn: false,
        etaVal: "插电长续航",
        etaSub: sys.isBypassPower ? "充电器功率充足 · 零损耗旁路" : "充电器功率充足 · 持续供电不掉电",
        badge: `${sys.batteryPercent}% ${sys.isBypassPower ? "旁路持续供电" : "插电长续航"}`,
        kpiBatSub: `整机 ≈${sysDrawNow.toFixed(1)}W · 充电器 ≈${(sys.estimatedChargerWatts ?? sysDrawNow).toFixed(1)}W`
      };
    }

    if (mode === "ac_discharging") {
      const disW = Number(sys.batteryDischargeWatts || sys.batteryPowerWatts || 0);
      const estIn = sys.estimatedChargerWatts ?? Math.max(0, sysDrawNow - disW);
      const eta = formatStandbyEta(sys);
      const etaPart = eta && eta !== "计算中..." ? ` · 续航${eta}` : "";
      return {
        apuPowerText,
        sysDrawNow,
        modeText: "供电不足 · 电池补电",
        modeClass: "warn",
        sysVal: sysValEst,
        sysSub: `充电器 ≈${estIn.toFixed(1)}W + 电池补电 ${disW.toFixed(1)}W`,
        batVal: `-${disW.toFixed(1)} W`,
        batSub: `${sys.batteryPercent}% · 充电器低于整机功耗，正在掉电`,
        batWarn: true,
        etaVal: eta,
        etaSub: `充电器不足 · 按电池净放电 ${disW.toFixed(1)}W 计算`,
        badge: `${sys.batteryPercent}% 补电-${disW.toFixed(1)}W${etaPart}`,
        kpiBatSub: `整机 ≈${sysDrawNow.toFixed(1)}W · 补电-${disW.toFixed(1)}W 续航${eta}`
      };
    }

    const disW = Number(sys.batteryDischargeWatts || sysDrawNow || 0);
    const batShownW = sys.batteryPowerWatts > 0 ? sys.batteryPowerWatts : disW;
    const eta = formatStandbyEta(sys);
    const etaPart = eta && eta !== "计算中..." ? ` · 续航${eta}` : "";
    return {
      apuPowerText,
      sysDrawNow,
      modeText: "便携电池供电",
      modeClass: "",
      sysVal: sysValExact,
      sysSub: "APU + 外围即时融合功耗",
      batVal: batShownW > 0 ? `-${batShownW.toFixed(1)} W` : `${sys.batteryPercent}%`,
      batSub: `${sys.batteryPercent}% · ${sys.voltageVolts > 0 ? `${sys.voltageVolts.toFixed(1)}V` : "离线放电"}`,
      batWarn: false,
      etaVal: eta,
      etaSub: `余电 ${(sys.batteryRemainingWh || 0).toFixed(1)} / ${(sys.batteryFullWh || 0).toFixed(1)} Wh`,
      badge: `${sys.batteryPercent}%${sysDrawNow > 0 ? ` -${sysDrawNow.toFixed(1)}W` : " 电池"}${etaPart}`,
      kpiBatSub: `整机 ${sysDrawNow.toFixed(1)}W · 预计续航 ${eta}`
    };
  }

  function formatPowerBadge(sys) {
    return computePowerFlowUi(sys, state.snapshot?.settings?.currentTdpWatts || 45).badge;
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

  function normalizeFanModeUi(mode) {
    const m = String(mode || "auto").toLowerCase();
    if (m === "manual") return "manual";
    if (m === "curve" || m === "silent" || m === "balanced" || m === "turbo") return "curve";
    return "auto";
  }

  function formatFanModeLabel(mode) {
    switch (normalizeFanModeUi(mode)) {
      case "manual": return "直接百分比";
      case "curve": return "温度曲线";
      default: return "EC 自动";
    }
  }

  function getDefaultFanCurves() {
    return [
      {
        id: "low_rpm",
        name: "低噪温控 (30–80%)",
        points: [
          { tempC: 40, fanPercent: 30 },
          { tempC: 55, fanPercent: 38 },
          { tempC: 68, fanPercent: 52 },
          { tempC: 76, fanPercent: 68 },
          { tempC: 82, fanPercent: 85 }
        ]
      },
      {
        id: "linear_60",
        name: "标准线性 (35–92%)",
        points: [
          { tempC: 40, fanPercent: 35 },
          { tempC: 55, fanPercent: 48 },
          { tempC: 65, fanPercent: 62 },
          { tempC: 75, fanPercent: 78 },
          { tempC: 82, fanPercent: 92 }
        ]
      },
      {
        id: "max_cool",
        name: "低温优先 (45–100%)",
        points: [
          { tempC: 35, fanPercent: 45 },
          { tempC: 50, fanPercent: 60 },
          { tempC: 62, fanPercent: 78 },
          { tempC: 72, fanPercent: 92 },
          { tempC: 80, fanPercent: 100 }
        ]
      }
    ];
  }

  function getActiveFanCurve(settings) {
    const curves = (settings?.fanCurves && settings.fanCurves.length) ? settings.fanCurves : getDefaultFanCurves();
    const id = settings?.activeFanCurveId;
    return curves.find((c) => c.id === id) || curves[0];
  }

  function evaluateFanCurvePct(tempC, points) {
    const pts = (points && points.length ? [...points] : getDefaultFanCurves()[1].points)
      .slice()
      .sort((a, b) => a.tempC - b.tempC);
    const t = Math.max(0, Math.min(115, Number(tempC) || 48));
    let pct;
    if (t <= pts[0].tempC) pct = pts[0].fanPercent;
    else if (t >= pts[pts.length - 1].tempC) pct = pts[pts.length - 1].fanPercent;
    else {
      pct = pts[pts.length - 1].fanPercent;
      for (let i = 0; i < pts.length - 1; i++) {
        const p0 = pts[i];
        const p1 = pts[i + 1];
        if (t >= p0.tempC && t <= p1.tempC) {
          const ratio = p1.tempC === p0.tempC ? 1 : (t - p0.tempC) / (p1.tempC - p0.tempC);
          pct = Math.round(p0.fanPercent + (p1.fanPercent - p0.fanPercent) * ratio);
          break;
        }
      }
    }
    pct = Math.max(20, Math.min(100, pct));
    if (t >= 86) pct = 100;
    else if (t >= 82 && pct < 80) pct = 80;
    return pct;
  }

  function renderFanCurveSvg(curve, curTempC, evalPct) {
    const W = 360;
    const H = 148;
    const padL = 32;
    const padR = 14;
    const padT = 14;
    const padB = 22;
    const iw = W - padL - padR;
    const ih = H - padT - padB;
    const minT = 30;
    const maxT = 95;
    const minF = 20;
    const maxF = 100;

    const mapX = (t) => padL + ((Math.max(minT, Math.min(maxT, t)) - minT) / (maxT - minT)) * iw;
    const mapY = (f) => padT + (1 - (Math.max(minF, Math.min(maxF, f)) - minF) / (maxF - minF)) * ih;

    const pts = (curve?.points || getDefaultFanCurves()[1].points)
      .slice()
      .sort((a, b) => a.tempC - b.tempC);
    const coordStr = pts.map((p) => `${mapX(p.tempC).toFixed(1)},${mapY(p.fanPercent).toFixed(1)}`).join(" ");
    const areaStr = pts.length
      ? `${mapX(pts[0].tempC).toFixed(1)},${(padT + ih).toFixed(1)} ${coordStr} ${mapX(pts[pts.length - 1].tempC).toFixed(1)},${(padT + ih).toFixed(1)}`
      : "";

    const safetyX = mapX(82).toFixed(1);
    const safetyY = mapY(80).toFixed(1);
    const liveX = mapX(curTempC || 48).toFixed(1);
    const liveY = mapY(evalPct).toFixed(1);

    const isFrost = (document.body.dataset.theme || "aurora") === "frost";
    const gridStroke = isFrost ? "rgba(15,23,42,0.09)" : "rgba(255,255,255,0.08)";
    const axisFill = isFrost ? "#475569" : "#7f8da3";
    const nodeLabelFill = isFrost ? "#0f172a" : "#cbd5e1";
    const nodeCenterFill = isFrost ? "#ffffff" : "#0f1722";
    const curveColor = isFrost ? "#2563eb" : "#2dd4bf";

    return `
      <svg class="fan-curve-svg" viewBox="0 0 ${W} ${H}" role="img" aria-label="温度与风扇占空比曲线图">
        <defs>
          <linearGradient id="fanCurveArea" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="${curveColor}" stop-opacity="0.28" />
            <stop offset="100%" stop-color="${curveColor}" stop-opacity="0.02" />
          </linearGradient>
        </defs>
        ${[40, 60, 80, 100].map((f) => {
          const y = mapY(f).toFixed(1);
          return `<line x1="${padL}" y1="${y}" x2="${W - padR}" y2="${y}" stroke="${gridStroke}" stroke-width="1" />
                  <text x="4" y="${Number(y) + 3}" fill="${axisFill}" font-size="9">${f}%</text>`;
        }).join("")}
        ${[40, 55, 70, 82].map((t) => {
          const x = mapX(t).toFixed(1);
          return `<line x1="${x}" y1="${padT}" x2="${x}" y2="${padT + ih}" stroke="${gridStroke}" stroke-width="1" />
                  <text x="${Number(x) - 9}" y="${H - 6}" fill="${t >= 82 ? "#d97706" : axisFill}" font-size="9">${t}°</text>`;
        }).join("")}
        <rect x="${safetyX}" y="${padT}" width="${(W - padR - Number(safetyX)).toFixed(1)}" height="${(Number(safetyY) - padT).toFixed(1)}" fill="rgba(245,158,11,0.12)" stroke="rgba(245,158,11,0.4)" stroke-dasharray="3,3" stroke-width="1" />
        ${areaStr ? `<polygon points="${areaStr}" fill="url(#fanCurveArea)" />` : ""}
        ${coordStr ? `<polyline points="${coordStr}" fill="none" stroke="${curveColor}" stroke-width="2.2" stroke-linejoin="round" />` : ""}
        ${pts.map((p) => `
          <circle cx="${mapX(p.tempC).toFixed(1)}" cy="${mapY(p.fanPercent).toFixed(1)}" r="4.2" fill="${nodeCenterFill}" stroke="${curveColor}" stroke-width="2" />
          <text x="${mapX(p.tempC).toFixed(1)}" y="${Math.max(11, mapY(p.fanPercent) - 7).toFixed(1)}" text-anchor="middle" fill="${nodeLabelFill}" font-size="8.5" font-weight="600">${p.fanPercent}%</text>
        `).join("")}
        <circle cx="${liveX}" cy="${liveY}" r="5" fill="#f59e0b" stroke="#111827" stroke-width="1.5" />
      </svg>
    `;
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

    const isFrost = (document.body.dataset.theme || "aurora") === "frost";
    const gridDash = isFrost ? "rgba(15,23,42,0.09)" : "rgba(255,255,255,0.07)";
    const gridBase = isFrost ? "rgba(15,23,42,0.14)" : "rgba(255,255,255,0.09)";
    const safeColorA = isFrost && colorA === "#2dd4bf" ? "#0d9488" : (isFrost && colorA === "#38bdf8" ? "#2563eb" : colorA);
    const safeColorB = isFrost && colorB === "#fbbf24" ? "#d97706" : colorB;

    const areaA = ptsA
      ? `<polygon points="${padX},${padY + innerH} ${ptsA} ${W - padX},${padY + innerH}" fill="${safeColorA}" fill-opacity="0.14" />`
      : "";

    return `
      <svg class="sparkline-svg" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" aria-hidden="true">
        <line x1="${padX}" y1="${padY}" x2="${W - padX}" y2="${padY}" stroke="${gridDash}" stroke-dasharray="3 3" />
        <line x1="${padX}" y1="${padY + innerH / 2}" x2="${W - padX}" y2="${padY + innerH / 2}" stroke="${gridDash}" stroke-dasharray="3 3" />
        <line x1="${padX}" y1="${padY + innerH}" x2="${W - padX}" y2="${padY + innerH}" stroke="${gridBase}" />
        ${areaA}
        ${ptsA ? `<polyline fill="none" stroke="${safeColorA}" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" points="${ptsA}" />` : ""}
        ${ptsB ? `<polyline fill="none" stroke="${safeColorB}" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="4 2" points="${ptsB}" />` : ""}
        <text x="${padX + 2}" y="11" fill="${safeColorA}" font-size="9.5" font-weight="600">${esc(labelA)}: ${esc(Math.round(lastA))}</text>
        ${seriesB ? `<text x="${W - padX - 2}" y="11" text-anchor="end" fill="${safeColorB}" font-size="9.5" font-weight="600">${esc(labelB)}: ${esc(Math.round(lastB))}</text>` : ""}
      </svg>
    `;
  }

  function formatSteadyBadge(sys, settings) {
    const mode = String(settings?.steadyEcoMode || "off").toLowerCase();
    if (sys?.steadyEcoActive) {
      const cnt = sys.isolatedProcessCount || 0;
      return {
        text: cnt > 0 ? `生效中 · 已收敛 ${cnt} 项后台` : "优化生效中",
        live: true
      };
    }
    if (mode === "auto") {
      return { text: "待命中", live: false };
    }
    return { text: "已关闭", live: false };
  }

  function patchLiveTelemetryDom(sys) {
    if (!sys) return;
    const hw = sys.hardwareService || {};
    const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
    const apuText = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W` : "--W";
    const vramUsedGb = ((sys.vramUsedMb || 0) / 1024).toFixed(1);
    const vramTotalGb = sys.vramTotalMb > 0 ? (sys.vramTotalMb / 1024).toFixed(0) : "16";
    const sharedGb = ((sys.sharedVramUsedMb || 0) / 1024).toFixed(1);
    const fpsText = sys.currentFps > 0 ? `${sys.currentFps} FPS` : "待检测游戏";
    const frameTimeText = sys.frameTimeMs > 0
      ? `${sys.frameTimeMs.toFixed(1)} ms · ETW 无钩子`
      : ((hw.daemonVersion || 0) >= 3 ? "DX11 / DX12 / Vulkan 就绪" : "升级硬件服务 v3 解锁");

    const topApuLabel = document.getElementById("topApuLabel");
    if (topApuLabel) {
      const fpsPrefix = sys.currentFps > 0 ? `${sys.currentFps}FPS · ` : "";
      topApuLabel.textContent = `${fpsPrefix}${apuText} · C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}°`;
    }

    const curTdp = state.snapshot?.settings?.currentTdpWatts || 45;
    const pfUi = computePowerFlowUi(sys, curTdp);

    const topSysLabel = document.getElementById("topSysLabel");
    if (topSysLabel) {
      const hzShort = sys.isDynamicRefreshRate ? `动态${sys.refreshRateHz || 120}Hz` : `${sys.refreshRateHz || "--"}Hz`;
      topSysLabel.textContent = `${hzShort} · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC直供"}`;
    }

    const dispRefreshSub = document.getElementById("dispRefreshSub");
    if (dispRefreshSub) {
      dispRefreshSub.textContent = `${formatRefreshTag(sys)}${sys.dynamicRefreshSupported ? " · VRR 自适应" : ""}`;
    }

    const ckFps = document.getElementById("ckFps");
    if (ckFps) ckFps.textContent = sys.currentFps > 0 ? `${sys.currentFps} FPS` : "待机";
    const ckFpsSub = document.getElementById("ckFpsSub");
    if (ckFpsSub) ckFpsSub.textContent = sys.frameTimeMs > 0 ? `${sys.frameTimeMs.toFixed(1)} ms` : "DX12/VK 就绪";
    const ckTemp = document.getElementById("ckTemp");
    if (ckTemp) ckTemp.textContent = `C ${sys.cpuTempC || "--"}° · G ${sys.gpuTempC || "--"}°`;
    const ckCpuSub = document.getElementById("ckCpuSub");
    if (ckCpuSub) ckCpuSub.textContent = `CPU ${sys.cpuUsagePercent || 0}% · ${sys.cpuClockMhz || "--"}MHz`;
    const ckGpuClk = document.getElementById("ckGpuClk");
    if (ckGpuClk) ckGpuClk.textContent = `${sys.gpuUsagePercent || 0}% · ${sys.gfxClockMhz || 0}MHz`;
    const ckVramSub = document.getElementById("ckVramSub");
    if (ckVramSub) ckVramSub.textContent = `显存 ${vramUsedGb}/${vramTotalGb}G`;
    const ckApuBar = document.getElementById("ckApuBar");
    if (ckApuBar) ckApuBar.style.width = `${Math.max(4, Math.min(100, ((sys.apuPowerWatts || 0) / Math.max(15, curTdp)) * 100)).toFixed(0)}%`;
    const ckGpuBar = document.getElementById("ckGpuBar");
    if (ckGpuBar) ckGpuBar.style.width = `${Math.max(4, Math.min(100, sys.gpuUsagePercent || 0))}%`;
    const ckTempBar = document.getElementById("ckTempBar");
    if (ckTempBar) ckTempBar.style.width = `${Math.max(8, Math.min(100, ((maxTemp || 35) / 95) * 100)).toFixed(0)}%`;
    const ckFanBar = document.getElementById("ckFanBar");
    if (ckFanBar) {
      const pwmP = hw.appliedFanPwm > 0 ? Math.round((hw.appliedFanPwm / 244) * 100) : (hw.fanRpm > 0 ? Math.min(100, Math.round((hw.fanRpm / 5200) * 100)) : 12);
      ckFanBar.style.width = `${Math.max(6, pwmP)}%`;
    }

    const qamDispSummary = document.getElementById("qamDispSummary");
    if (qamDispSummary) {
      qamDispSummary.textContent = `${formatRefreshTag(sys)} · ${formatResShort(`${sys.screenWidth}x${sys.screenHeight}`)}`;
    }

    const pfModeBadge = document.getElementById("pfModeBadge");
    if (pfModeBadge) {
      pfModeBadge.textContent = pfUi.modeText;
      pfModeBadge.className = `hh-card-badge ${pfUi.modeClass}`.trim();
    }

    const pfApuVal = document.getElementById("pfApuVal");
    if (pfApuVal) pfApuVal.textContent = pfUi.apuPowerText;

    const pfApuSub = document.getElementById("pfApuSub");
    if (pfApuSub) pfApuSub.textContent = `GPU ${sys.gpuUsagePercent || 0}% · CPU ${sys.cpuTempC || "--"}° / GPU ${sys.gpuTempC || "--"}°C`;

    const pfSysVal = document.getElementById("pfSysVal");
    if (pfSysVal) pfSysVal.textContent = pfUi.sysVal;

    const pfSysSub = document.getElementById("pfSysSub");
    if (pfSysSub) pfSysSub.textContent = pfUi.sysSub;

    const pfBatVal = document.getElementById("pfBatVal");
    if (pfBatVal) {
      pfBatVal.textContent = pfUi.batVal;
      pfBatVal.classList.toggle("warn-amber", !!pfUi.batWarn);
    }

    const pfBatSub = document.getElementById("pfBatSub");
    if (pfBatSub) pfBatSub.textContent = pfUi.batSub;

    const pfEtaVal = document.getElementById("pfEtaVal");
    if (pfEtaVal) pfEtaVal.textContent = pfUi.etaVal;

    const pfEtaSub = document.getElementById("pfEtaSub");
    if (pfEtaSub) pfEtaSub.textContent = pfUi.etaSub;

    const fanRpmBadge = document.getElementById("fanRpmBadge");
    if (fanRpmBadge) {
      const pwmPct = hw.appliedFanPwm > 0 ? Math.round((hw.appliedFanPwm / 244) * 100) : 0;
      fanRpmBadge.textContent = hw.ecReady && hw.fanRpm > 0
        ? `${hw.fanRpm} RPM · PWM ${pwmPct}%`
        : (hw.serviceRunning ? "仅 SMU 就绪" : "EC 未授权");
    }

    const pmReadback = document.getElementById("pmReadbackLine");
    if (pmReadback) {
      if (hw.hwStapmLimitWatts != null) {
        const sStr = hw.hwSlowLimitWatts != null ? hw.hwSlowLimitWatts.toFixed(1) : "--";
        const fStr = hw.hwFastLimitWatts != null ? hw.hwFastLimitWatts.toFixed(1) : "--";
        pmReadback.textContent = `PM 实读 (${hw.pmTableVersion || "SMU"})：SPL ${hw.hwStapmLimitWatts.toFixed(1)}W · sPPT ${sStr}W · fPPT ${fStr}W`;
      }
    }

    const steadyUi = formatSteadyBadge(sys, state.snapshot?.settings);
    const steadyBadge = document.getElementById("steadyStatusBadge");
    if (steadyBadge) {
      steadyBadge.textContent = steadyUi.text;
      steadyBadge.classList.toggle("live", steadyUi.live);
    }
    const qamSteadyBadge = document.getElementById("qamSteadyBadge");
    if (qamSteadyBadge) {
      qamSteadyBadge.textContent = steadyUi.text;
      qamSteadyBadge.classList.toggle("live", steadyUi.live);
    }

    const setEl = (id, text) => {
      const el = document.getElementById(id);
      if (el) el.textContent = text;
    };

    setEl("kpiFps", fpsText);
    setEl("kpiFpsSub", frameTimeText);
    setEl("kpiApuW", sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W");
    setEl("kpiApuSub", `设定 ${state.snapshot?.settings?.currentTdpWatts || 40}W · ${sys.adlAvailable ? "ADL2 采样" : "待命"}`);
    setEl("kpiGpuLoad", `${sys.gpuUsagePercent || 0}%`);
    setEl("kpiGpuSub", `${sys.gfxClockMhz || 0} MHz · 显存频率 ${sys.memClockMhz || 0} MHz`);
    setEl("kpiVram", `${vramUsedGb} / ${vramTotalGb} GB`);
    setEl("kpiVramSub", `专用 ${sys.vramUsedMb || 0}MB · 共享 ${sys.sharedVramUsedMb || 0}MB (${sharedGb}G)`);
    setEl("kpiCpuLoad", `${sys.cpuUsagePercent || 0}%`);
    setEl("kpiCpuSub", sys.cpuClockMhz > 0 ? `${sys.cpuClockMhz} MHz` : (sys.cpuName || "AMD Ryzen"));
    setEl("kpiCpuTemp", `CPU ${sys.cpuTempC || "--"}°C`);
    setEl("kpiGpuTempSub", `GPU ${sys.gpuTempC || "--"}°C · 风扇 ${hw.fanRpm || 0} RPM`);
    setEl("kpiMem", sys.totalRamGb > 0 ? `${sys.usedRamGb} / ${sys.totalRamGb} GB` : "-- GB");
    setEl("kpiBat", pfUi.badge);
    setEl("kpiBatSub", pfUi.kpiBatSub);

    // QAM live tiles & monitor grid
    setEl("qamStripApu", sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W / ${state.snapshot?.settings?.currentTdpWatts || 40}W` : "--W");
    setEl("qamStripBat", formatPowerBadge(sys));
    setEl("qamStripTherm", `C${sys.cpuTempC || "--"}° G${sys.gpuTempC || "--"}° · ${hw.fanRpm || 0}R`);
    if (state.activeSliderDrag !== "bright" && typeof sys.brightnessPercent === "number" && sys.brightnessSupported) {
      updateBrightnessUiImmediate(sys.brightnessPercent);
    }
    if (state.activeSliderDrag !== "vol" && typeof sys.volumePercent === "number") {
      updateVolumeUiImmediate(sys.volumePercent, !!sys.isMuted);
    }
    setEl("qamKpiFps", sys.currentFps > 0 ? `${sys.currentFps} FPS` : "-- FPS");
    setEl("qamKpiApu", sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W");
    setEl("qamKpiGpu", `${sys.gpuUsagePercent || 0}% · ${sys.gfxClockMhz || 0}M`);
    setEl("qamKpiTemp", `C ${sys.cpuTempC || "--"}° / G ${sys.gpuTempC || "--"}°`);
    setEl("qamKpiVram", `${vramUsedGb}/${vramTotalGb}G (+${sharedGb}G)`);
    setEl("qamKpiMem", `${sys.usedRamGb || "--"}G · ${hw.fanRpm || 0}RPM`);

    const chartPower = document.getElementById("svgChartPowerTemp");
    if (chartPower) {
      chartPower.innerHTML = buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 120, 100, "#2dd4bf", "#fbbf24", "APU W", "Temp °C");
    }
    const ckChartPower = document.getElementById("ckChartPowerTemp");
    if (ckChartPower) {
      ckChartPower.innerHTML = buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 100, 95, "#2dd4bf", "#fbbf24", "APU W", "Temp °C");
    }
    const chartLoad = document.getElementById("svgChartGpuCpu");
    if (chartLoad) {
      chartLoad.innerHTML = buildSvgChart(sys.gpuUsageHistory || [], sys.cpuUsageHistory || [], 100, 100, "#38bdf8", "#a78bfa", "GPU %", "CPU %");
    }
    const qamSpark = document.getElementById("qamOsdSparkline");
    if (qamSpark) {
      qamSpark.innerHTML = buildSvgChart(sys.apuWattsHistory || [], sys.gpuUsageHistory || [], 85, 100, "#2dd4bf", "#38bdf8", "APU W", "GPU %");
    }
    const qamSpark2 = document.getElementById("qamOsdSparkline2");
    if (qamSpark2) {
      qamSpark2.innerHTML = buildSvgChart(sys.cpuUsageHistory || [], sys.tempHistory || [], 100, 100, "#a78bfa", "#fbbf24", "CPU %", "Temp °C");
    }
    const qamPowerChip = document.getElementById("qamPowerChip");
    if (qamPowerChip) {
      const fpsBadge = sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : "";
      qamPowerChip.textContent = `${fpsBadge}APU ${apuText}`;
    }
    const qamRibbonLeft = document.getElementById("qamRibbonLeft");
    if (qamRibbonLeft) {
      const fpsBadge = sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : "";
      qamRibbonLeft.textContent = `${fpsBadge}APU ${apuText} / ${curTdp}W`;
    }
    const qamRibbonRight = document.getElementById("qamRibbonRight");
    if (qamRibbonRight) {
      const rpm = hw.fanRpm || 0;
      qamRibbonRight.textContent = `C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}° · ${rpm}R`;
    }
  }

  function formatConfigSummary(cfg, compact = false) {
    if (!cfg) return compact ? "已注入" : "OptiScaler 已生效";
    const srLabels = compact
      ? { fsr4_int8: "FSR4", fsr4_int8_lite: "4 Lite", xess: "XeSS", fsr31: "FSR3.1" }
      : { fsr4_int8: "FSR 4.1.2", fsr4_int8_lite: "FSR 4.1.2 Lite", xess: "XeSS 2.0", fsr31: "FSR 3.1" };
    const fgLabels = { xefg: "XeFG", optifg: "OptiFG", nukems: "Nukem", nofg: "无插帧" };
    const sr = srLabels[cfg.srMode] || cfg.srMode || "FSR";
    let fg = fgLabels[cfg.fgType] || cfg.fgType || "XeFG";
    if ((cfg.fgType || "xefg") === "xefg") {
      const mult = Math.min(4, Math.max(2, Number(cfg.xeFgMultiplier) || 2));
      const dmfg = !!cfg.xeFgDynamicMfg;
      const dmfgTarget = dmfg && (Number(cfg.xeFgDynamicTargetFps) || 0) >= 10 ? Number(cfg.xeFgDynamicTargetFps) : 0;
      if (compact) {
        if (dmfg) fg = `XeFG ${mult}X·DMFG`;
        else if (mult > 2) fg = `XeFG ${mult}X`;
      } else {
        if (dmfg) fg = dmfgTarget >= 10 ? `XeFG ${mult}X · DMFG ${dmfgTarget}帧` : `XeFG ${mult}X · DMFG`;
        else if (mult > 2) fg = `XeFG ${mult}X`;
      }
    }
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
    const active = snap?.activeInspectGame || snap?.runningGame || (snap?.games || [])[0] || null;
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

  function resolveGpdSlotState(mode, slots, keyCode, comboCodes) {
    const isMacroMode = mode === 2 || mode === 6;
    const combo = isMacroMode && Array.isArray(comboCodes) && comboCodes.length >= 2
      ? normalizeComboCodesList(comboCodes)
      : [];
    const safeSlots = typeof slots === "number" ? slots : 0;
    const safeKey = typeof keyCode === "number" ? keyCode : 0;
    if (combo.length >= 2) {
      return { key: 0, combo, legacyMode0: false };
    }
    if (isMacroMode) {
      if (safeSlots <= 0 || safeKey === 0) {
        return { key: 0, combo: [], legacyMode0: false };
      }
      if (safeSlots === 1) {
        return { key: safeKey, combo: [], legacyMode0: false };
      }
      // Multi-slot custom/factory macro with gaps or pauses: preserve unless user explicitly edits
      return { key: -1, combo: [], legacyMode0: false };
    }
    // Legacy v0.5.x HaloDeck wrote mode=0; show its key so user can re-apply in fixed V2 mode=2 format
    return { key: safeKey, combo: [], legacyMode0: mode === 0 && safeKey > 0 };
  }

  function syncGpdDraft(gpd) {
    if (!gpd) return;
    if (!gpd.connected) {
      state.gpdDraft = {
        l4Key: -1,
        r4Key: -1,
        l4Combo: [],
        r4Combo: [],
        rumble: typeof gpd.rumbleLevel === "number" ? gpd.rumbleLevel : 1,
        leftDeadzone: typeof gpd.leftDeadzone === "number" ? gpd.leftDeadzone : 0,
        rightDeadzone: typeof gpd.rightDeadzone === "number" ? gpd.rightDeadzone : 0
      };
      return;
    }
    const l4 = resolveGpdSlotState(gpd.l4Mode, gpd.l4Slots, gpd.l4KeyCode, gpd.l4ComboCodes);
    const r4 = resolveGpdSlotState(gpd.r4Mode, gpd.r4Slots, gpd.r4KeyCode, gpd.r4ComboCodes);
    state.gpdDraft = {
      l4Key: l4.key,
      r4Key: r4.key,
      l4Combo: l4.combo,
      r4Combo: r4.combo,
      rumble: typeof gpd.rumbleLevel === "number" ? gpd.rumbleLevel : 1,
      leftDeadzone: typeof gpd.leftDeadzone === "number" ? gpd.leftDeadzone : 0,
      rightDeadzone: typeof gpd.rightDeadzone === "number" ? gpd.rightDeadzone : 0
    };
  }

  function syncThemeDom(rawTheme) {
    const norm = rawTheme === "frost" ? "frost" : "aurora";
    document.body.dataset.theme = norm;
    const found = UI_THEMES.find((t) => t.id === norm) || UI_THEMES[0];
    const lbl = document.getElementById("topThemeLabel");
    if (lbl) lbl.textContent = found.shortLabel;
  }

  function syncLayoutDom(rawLayout) {
    const norm = rawLayout === "cockpit" ? "cockpit" : "bento";
    document.body.dataset.layout = norm;
    const found = UI_LAYOUTS.find((l) => l.id === norm) || UI_LAYOUTS[0];
    const lbl = document.getElementById("topLayoutLabel");
    if (lbl) lbl.textContent = found.shortLabel;
  }

  const PLUGIN_THEME_VAR_ALLOWLIST = [
    "--bg-base", "--bg-elevated", "--bg-card", "--bg-card-solid", "--bg-subtile", "--bg-row", "--bg-well",
    "--text-primary", "--text-secondary", "--text-muted",
    "--border", "--border-strong",
    "--accent", "--accent-hover", "--accent-soft", "--accent-glow", "--accent-grad",
    "--radius-sm", "--radius-md", "--radius-lg", "--radius-card", "--radius-inner",
    "--bg-app", "--bg-panel", "--surface-subtle"
  ];

  function detectColorBrightness(colorStr) {
    if (typeof colorStr !== "string") return null;
    const s = colorStr.trim();
    const hex = /^#([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(s);
    if (hex) {
      return 0.299 * parseInt(hex[1], 16) + 0.587 * parseInt(hex[2], 16) + 0.114 * parseInt(hex[3], 16);
    }
    const rgb = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i.exec(s);
    if (rgb) {
      return 0.299 * Number(rgb[1]) + 0.587 * Number(rgb[2]) + 0.114 * Number(rgb[3]);
    }
    return null;
  }

  function applyPluginThemeOverride(themeObj) {
    const target = document.body.style;
    for (const k of PLUGIN_THEME_VAR_ALLOWLIST) {
      target.removeProperty(k);
    }
    const vars = themeObj?.variables;
    if (!vars || typeof vars !== "object") {
      document.body.classList.remove("has-plugin-theme");
      // Restore user's base Aurora/Frost mode when plugin theme is cleared
      syncThemeDom(state.snapshot?.settings?.uiTheme || "aurora");
      return;
    }
    document.body.classList.add("has-plugin-theme");
    // Automatically align Aurora/Frost surface contrast rules with the plugin skin's base luminance
    // so applying a dark skin while in Frost mode (or light skin in Aurora mode) never produces inverted patches.
    const bgSample = vars["--bg-base"] || vars["--bg-app"];
    const luma = detectColorBrightness(bgSample);
    if (luma !== null) {
      document.body.dataset.theme = luma > 145 ? "frost" : "aurora";
    }
    for (const k of PLUGIN_THEME_VAR_ALLOWLIST) {
      if (typeof vars[k] === "string" && vars[k].trim()) {
        target.setProperty(k, vars[k].trim());
      }
    }
    // Keep --accent-grad synced with custom --accent so active pills/tabs never stay hardcoded blue
    if (vars["--accent"] && !vars["--accent-grad"]) {
      target.setProperty("--accent-grad", vars["--accent"].trim());
    }
  }

  function buildGpdHardwareSig(gpd) {
    if (!gpd) return "";
    return JSON.stringify([
      !!gpd.connected, gpd.l4Mode, gpd.l4Slots, gpd.l4KeyCode, gpd.l4ComboCodes || [],
      gpd.r4Mode, gpd.r4Slots, gpd.r4KeyCode, gpd.r4ComboCodes || [],
      gpd.rumbleLevel, gpd.leftDeadzone, gpd.rightDeadzone
    ]);
  }

  function applySnapshot(snap) {
    state.snapshot = snap;
    syncThemeDom(snap?.settings?.uiTheme || "aurora");
    applyPluginThemeOverride(snap?.activePluginTheme || null);
    syncLayoutDom(snap?.settings?.uiLayout || "bento");
    syncConfigFromActiveGame(snap);
    if (snap?.gpdController) {
      const sig = buildGpdHardwareSig(snap.gpdController);
      if (!state.gpdLoaded || (snap.gpdController.connected && sig !== state.lastSyncedGpdSig)) {
        syncGpdDraft(snap.gpdController);
        state.lastSyncedGpdSig = sig;
        if (snap.gpdController.connected) state.gpdLoaded = true;
      }
    }
    updateControllerChip(snap?.connectedControllers || 0);
    patchLiveTelemetryDom(snap?.systemControl);
    if (snap?.hardwareProfile?.deviceName) {
      const el = document.getElementById("deviceTag");
      const curVer = snap?.appVersion || "0.6.2";
      if (el) el.textContent = `GPD WIN 5 · v${curVer}`;
    }
    if (state.isDraggingRange || Date.now() < state.dragCooldownUntil) {
      state.pendingSnapshot = snap;
      return;
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

  function getRunningGame() {
    const g = state.snapshot?.runningGame;
    return g && g.isCurrentlyRunning ? g : null;
  }

  function getActiveGame() {
    const snap = state.snapshot;
    if (!snap) return null;
    if (snap.activeInspectGame) return snap.activeInspectGame;
    if (snap.runningGame && snap.runningGame.isCurrentlyRunning) return snap.runningGame;
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

  function isMfgUnlockerInstalled() {
    const comps = state.snapshot?.components || [];
    return comps.some((c) => c.slot === "MfgUnlocker" && c.isInstalled);
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
      case "fridge":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="2" x2="12" y2="22"></line><line x1="2" y1="12" x2="22" y2="12"></line><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"></line><line x1="19.07" y1="4.93" x2="4.93" y2="19.07"></line></svg>`;
      case "osk":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"></rect><line x1="6" y1="9" x2="6.01" y2="9"></line><line x1="10" y1="9" x2="10.01" y2="9"></line><line x1="14" y1="9" x2="14.01" y2="9"></line><line x1="18" y1="9" x2="18.01" y2="9"></line><line x1="8" y1="15" x2="16" y2="15"></line></svg>`;
      case "mouse":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="6" y="2" width="12" height="20" rx="6"></rect><line x1="12" y1="6" x2="12" y2="10"></line></svg>`;
      case "shot":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"></path><circle cx="12" cy="13" r="4"></circle></svg>`;
      case "switchwin":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="11" height="11" rx="2"></rect><path d="M10 17v2a2 2 0 0 0 2 2h7a2 2 0 0 0 2-2v-7a2 2 0 0 0-2-2h-2"></path></svg>`;
      case "desktop":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="20" height="14" rx="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line></svg>`;
      case "wifi":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.55a11 11 0 0 1 14.08 0"></path><path d="M1.42 9a16 16 0 0 1 21.16 0"></path><path d="M8.53 16.11a6 6 0 0 1 6.95 0"></path><line x1="12" y1="20" x2="12.01" y2="20"></line></svg>`;
      case "closegame":
        return `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><line x1="9" y1="9" x2="15" y2="15"></line><line x1="15" y1="9" x2="9" y2="15"></line></svg>`;
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

  function renderStepSlider({ idPrefix, value, min = 0, displayText = null, disabled = false, muted = false, muteBtnId = null, iconHtml = "" }) {
    const pct = Math.max(min, Math.min(100, Number(value) || 0));
    const text = displayText || (muted ? "静音" : `${pct}%`);
    return `
      <div class="step-slider cc-slider-wrap">
        <button id="${idPrefix}Dec" class="step-btn cc-step-btn" data-focusable="true" ${disabled ? "disabled" : ""} aria-label="减小">−</button>
        <div class="step-track cc-track" data-slider-track="${esc(idPrefix)}">
          <div id="${idPrefix}Fill" class="step-fill cc-fill" style="width:${muted ? 0 : pct}%;"></div>
          ${iconHtml ? `<span class="cc-track-icon" aria-hidden="true">${iconHtml}</span>` : ""}
          <span id="${idPrefix}Val" class="step-val cc-val">${esc(text)}</span>
          <input id="${idPrefix}Range" class="step-range-input" type="range" min="${min}" max="100" step="1" value="${pct}" ${disabled ? "disabled" : ""} aria-label="${esc(idPrefix)}" />
        </div>
        <button id="${idPrefix}Inc" class="step-btn cc-step-btn" data-focusable="true" ${disabled ? "disabled" : ""} aria-label="增大">+</button>
        ${muteBtnId ? `<button id="${muteBtnId}" class="step-btn cc-mute-btn ${muted ? "active" : ""}" data-focusable="true" title="静音切换">${muted ? "静音" : "🔊"}</button>` : ""}
      </div>
    `;
  }

  function renderKeycaps(items) {
    if (!items || !items.length) return `<span class="keycap-pill">未设置</span>`;
    return `<div class="keycap-combo">${items.map((k) => `<span class="keycap-pill">${esc(k)}</span>`).join(`<span class="keycap-plus">+</span>`)}</div>`;
  }

  // ---------------- Console View 1: Quick Control Center (Bento Tile Mode vs Desktop Cockpit Mode) ----------------
  function renderHandheldView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const frozen = snap.frozenGames || [];
    const active = getRunningGame();
    const layoutMode = settings.uiLayout === "cockpit" ? "cockpit" : "bento";

    const curTdp = settings.currentTdpWatts || 40;
    const customPresets = (settings.customTdpPresets && settings.customTdpPresets.length)
      ? settings.customTdpPresets
      : [10, 15, 20, 25, 30, 45, 60];
    const flatLock = !!settings.flatPowerLock;
    const tdpFillPct = Math.max(0, Math.min(100, ((curTdp - 8) / (120 - 8)) * 100));

    const fanMode = normalizeFanModeUi(settings.fanMode);
    const manualFanPct = settings.manualFanPercent || 55;

    const hzOptions = buildRefreshOptions(sys);
    const activeRefreshVal = sys.isDynamicRefreshRate ? "drr" : String(sys.refreshRateHz || 120);
    const resOptions = (sys.supportedResolutions && sys.supportedResolutions.length)
      ? sys.supportedResolutions.slice(0, 3).map((r) => ({ value: r, label: formatResShort(r) }))
      : [{ value: "1920x1080", label: "1080p" }, { value: "1600x900", label: "900p" }, { value: "1280x720", label: "720p" }];
    const curRes = `${sys.screenWidth || 1920}x${sys.screenHeight || 1080}`;

    const pfUi = computePowerFlowUi(sys, curTdp);
    const osdLevel = settings.overlayLevel ?? 0;
    const osdPos = settings.overlayPosition || "top-center";
    const osdPosLabel = osdPos === "top-left" ? "左上" : osdPos === "top-right" ? "右上" : "顶中";

    const perGameProfiles = settings.perGameProfiles || {};
    const gamePerf = active ? (perGameProfiles[active.id] || {
      gameId: active.id,
      gameName: active.title,
      enabled: false,
      tdpWatts: curTdp,
      refreshRateHz: 0,
      cpuMaxFreqMhz: 0
    }) : null;

    const daemonVer = hw.daemonVersion || 0;
    const hwDriverMissing = hw.serviceRunning && !hw.smuReady;
    const showSvcAlert = !hw.serviceRunning || hwDriverMissing || daemonVer < 4;
    const svcAlertText = !hw.serviceRunning
      ? "当前普通用户态运行（开启硬件服务可免 UAC 直控 8–120W 功耗与双风扇）"
      : hwDriverMissing
        ? (hw.lastError || "PawnIO 内核驱动未就绪，请先安装或启动官方 PawnIO 驱动后重试")
        : "检测到新版 PawnIO 硬件协议 v4，点击右侧可热升级";
    const svcAlertHtml = !showSvcAlert ? "" : `
      <div class="bento-svc-strip">
        <span>${esc(svcAlertText)}</span>
        <div style="display:flex;gap:6px;flex-shrink:0;">
          ${hwDriverMissing ? `<button id="btnOpenPawnIo" class="btn btn-sm" data-focusable="true">下载 PawnIO</button>` : ""}
          <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" data-focusable="true">${!hw.serviceRunning ? "一键启用直控" : (hwDriverMissing ? "重试启动驱动" : "热升级 v4")}</button>
        </div>
      </div>
    `;

    const pwmPctNow = hw.appliedFanPwm > 0 ? Math.round((hw.appliedFanPwm / 244) * 100) : 0;
    const fanBadgeText = hw.ecReady && hw.fanRpm > 0
      ? `${hw.fanRpm} RPM`
      : (hw.serviceRunning ? "SMU 就绪" : "待授权");
    const boostMode = sys.cpuBoostMode || "off";
    const boostBadgeText = boostMode === "off" ? "已关睿频" : (boostMode === "efficient" ? "智能平衡" : "积极满血");
    const savedProfileCount = Object.values(perGameProfiles).filter((p) => p && p.enabled).length;

    const sunSvg = `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="4"></circle><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"></path></svg>`;
    const volSvg = `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path></svg>`;

    if (layoutMode === "cockpit") {
      const fanCurves = (settings.fanCurves && settings.fanCurves.length) ? settings.fanCurves : getDefaultFanCurves();
      const activeCurve = getActiveFanCurve(settings);
      const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
      const curTempEval = maxTemp > 0 ? maxTemp : 42;
      const curveEvalPct = evaluateFanCurvePct(curTempEval, activeCurve.points);
      const vramUsedGb = ((sys.vramUsedMb || 0) / 1024).toFixed(1);
      const vramTotalGb = sys.vramTotalMb > 0 ? (sys.vramTotalMb / 1024).toFixed(0) : "16";
      const apuBarPct = Math.max(5, Math.min(100, ((sys.apuPowerWatts || 0) / Math.max(15, curTdp)) * 100));
      const sysBarPct = Math.max(6, Math.min(100, ((pfUi.sysDrawNow || 0) / 110) * 100));
      const gpuBarPct = Math.max(5, Math.min(100, sys.gpuUsagePercent || 0));
      const tempBarPct = Math.max(8, Math.min(100, ((maxTemp || 35) / 95) * 100));
      const fanBarPct = Math.max(8, pwmPctNow > 0 ? pwmPctNow : (hw.fanRpm > 0 ? Math.min(100, Math.round((hw.fanRpm / 5200) * 100)) : 18));
      const inj = active?.injection;

      return `
        <div class="cockpit-home-shell">
          ${svcAlertHtml}
          <div class="cockpit-telemetry-ribbon">
            <div class="ck-cell">
              <div class="ck-cell-top">
                <span class="ck-label">实时帧率</span>
                <span id="ckFpsSub" class="ck-mini-sub">${sys.frameTimeMs > 0 ? `${sys.frameTimeMs.toFixed(1)} ms` : "DX12/VK 就绪"}</span>
              </div>
              <strong id="ckFps" class="ck-val accent-blue">${sys.currentFps > 0 ? `${sys.currentFps} FPS` : "待机"}</strong>
              <div class="ck-meter"><div class="ck-meter-fill blue" style="width:${sys.currentFps > 0 ? Math.min(100, Math.round((sys.currentFps / 120) * 100)) : 12}%;"></div></div>
            </div>

            <div class="ck-cell">
              <div class="ck-cell-top">
                <span class="ck-label">APU 封装功耗</span>
                <span class="ck-mini-sub">上限 ${curTdp}W</span>
              </div>
              <strong id="pfApuVal" class="ck-val">${esc(pfUi.apuPowerText || "-- W")}</strong>
              <div class="ck-meter"><div id="ckApuBar" class="ck-meter-fill teal" style="width:${apuBarPct.toFixed(0)}%;"></div></div>
            </div>

            <div class="ck-cell">
              <div class="ck-cell-top">
                <span class="ck-label">整机功耗</span>
                <span id="pfEtaVal" class="ck-mini-sub accent-teal">${esc(pfUi.etaVal)}</span>
              </div>
              <strong id="pfSysVal" class="ck-val">${esc(pfUi.sysVal)}</strong>
              <span id="pfBatVal" style="display:none;">${esc(pfUi.batVal)}</span>
              <span id="pfModeBadge" class="hh-card-badge ${pfUi.modeClass}" style="display:none;">${esc(pfUi.modeText)}</span>
              <div class="ck-meter"><div class="ck-meter-fill teal" style="width:${sysBarPct.toFixed(0)}%;"></div></div>
            </div>

            <div class="ck-cell">
              <div class="ck-cell-top">
                <span class="ck-label">8060S 核显频率</span>
                <span id="ckVramSub" class="ck-mini-sub">显存 ${vramUsedGb}/${vramTotalGb}G</span>
              </div>
              <strong id="ckGpuClk" class="ck-val">${sys.gpuUsagePercent || 0}% · ${sys.gfxClockMhz || 0}MHz</strong>
              <div class="ck-meter"><div id="ckGpuBar" class="ck-meter-fill blue" style="width:${gpuBarPct}%;"></div></div>
            </div>

            <div class="ck-cell">
              <div class="ck-cell-top">
                <span class="ck-label">核心温度 / CPU</span>
                <span id="ckCpuSub" class="ck-mini-sub">CPU ${sys.cpuUsagePercent || 0}%</span>
              </div>
              <strong id="ckTemp" class="ck-val">C ${sys.cpuTempC || "--"}° · G ${sys.gpuTempC || "--"}°</strong>
              <div class="ck-meter"><div id="ckTempBar" class="ck-meter-fill amber" style="width:${tempBarPct.toFixed(0)}%;"></div></div>
            </div>

            <div class="ck-cell">
              <div class="ck-cell-top">
                <span class="ck-label">双风扇转速</span>
                <span class="ck-mini-sub">${esc(formatFanModeLabel(settings.fanMode))}</span>
              </div>
              <strong id="fanRpmBadge" class="ck-val">${hw.ecReady && hw.fanRpm > 0 ? `${hw.fanRpm} RPM` : fanBadgeText}</strong>
              <div class="ck-meter"><div id="ckFanBar" class="ck-meter-fill teal" style="width:${fanBarPct}%;"></div></div>
            </div>
          </div>

          <div class="cockpit-matrix-3">
            <!-- Col 1: Power Engine, CPU Boost & Live 60s Power/Temp Sparkline -->
            <section class="hh-card ck-panel">
              <div class="hh-card-head" style="margin-bottom:0;">
                <span class="hh-card-title">功耗引擎与核心睿频</span>
                <span id="consoleTdpBadge" class="hh-card-badge live">${hw.hwStapmLimitWatts != null ? `PM实读 ${hw.hwStapmLimitWatts.toFixed(0)}W` : `目标 ${curTdp}W`}</span>
              </div>

              <div class="tdp-chip-grid compact">
                ${customPresets.map((w) => `
                  <button class="tdp-chip-btn ${curTdp === w ? "active" : ""}" data-tdp-watt="${w}" data-focusable="true">${w}W</button>
                `).join("")}
              </div>

              <div class="ck-tdp-bar">
                <button id="btnTdpDec" class="step-btn ck-step-h" data-focusable="true">−1W</button>
                <div class="bento-tdp-track-wrap ck-tdp-track">
                  <div id="tdpTrackFill" class="bento-tdp-track-fill" style="width:${tdpFillPct.toFixed(1)}%;"></div>
                  <span class="bento-tdp-track-hint">滑动直调 8–120W</span>
                  <input id="tdpRangeInput" class="bento-tdp-range-overlay" type="range" min="8" max="120" step="1" value="${curTdp}" aria-label="目标 TDP 滑块" />
                </div>
                <input id="tdpNumberInput" class="num-mini-input ck-num-h" type="number" min="8" max="120" value="${curTdp}" aria-label="目标瓦数" />
                <button id="btnTdpInc" class="step-btn ck-step-h" data-focusable="true">+1W</button>
              </div>

              <div class="ck-group-box">
                <div class="ck-group-row">
                  <span class="ck-seg-label">CPU 睿频模式</span>
                  ${renderSegmented("data-cpu-boost", [
                    { value: "off", label: "关睿频" },
                    { value: "efficient", label: "平衡" },
                    { value: "aggressive", label: "满血" }
                  ], boostMode, "compact")}
                </div>
                <div class="ck-group-row">
                  <div>
                    <span class="ck-seg-label">插拔电自动切功耗</span>
                    <span class="ck-muted" style="margin-left:5px;">电池 ${settings.batteryTdpWatts || 25}W / 插电 ${settings.acTdpWatts || 55}W</span>
                  </div>
                  ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
                </div>
                <div class="ck-group-row">
                  <div>
                    <span class="ck-seg-label">稳态功耗锁</span>
                    <span class="ck-muted" style="margin-left:5px;">SPL = sPPT = fPPT</span>
                  </div>
                  ${renderSwitch("swFlatLock", flatLock)}
                </div>
              </div>

              <div class="ck-chart-widget">
                <div class="ck-widget-head">
                  <span>60s APU 功耗与核心温度趋势</span>
                  <span id="pmReadbackLine" class="ck-muted">${hw.hwStapmLimitWatts != null ? `SPL ${hw.hwStapmLimitWatts.toFixed(0)}W · fPPT ${(hw.hwFastLimitWatts ?? curTdp).toFixed(0)}W` : `目标 ${curTdp}W`}</span>
                </div>
                <div id="ckChartPowerTemp" class="ck-spark-wrap">
                  ${buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 100, 95, "#2dd4bf", "#fbbf24", "APU W", "Temp °C")}
                </div>
              </div>
            </section>

            <!-- Col 2: Display, Audio, OSD & Live Dual-Fan Curve Preview -->
            <section class="hh-card ck-panel">
              <div class="hh-card-head" style="margin-bottom:0;">
                <span class="hh-card-title">显示 · 声光 · 双风扇温控</span>
                <span id="dispRefreshSub" class="ck-muted">${esc(formatRefreshTag(sys))}</span>
              </div>

              <div class="ck-group-box">
                <div class="ck-group-row">
                  <span class="ck-seg-label">刷新率</span>
                  ${renderSegmented("data-refresh-hz", hzOptions, activeRefreshVal, "compact")}
                </div>
                <div class="ck-group-row">
                  <span class="ck-seg-label">分辨率</span>
                  ${renderSegmented("data-res-mode", resOptions, curRes, "compact")}
                </div>
                <div class="ck-group-row">
                  <div style="display:flex;align-items:center;gap:5px;">
                    <span class="ck-seg-label">悬浮窗</span>
                    <button id="btnCycleOsdPos" class="bento-link-pill" data-focusable="true">${osdPosLabel} ▸</button>
                  </div>
                  <div class="seg-group compact">
                    ${[
                      { v: 0, label: "关闭" },
                      { v: 1, label: "极简" },
                      { v: 2, label: "紧凑" },
                      { v: 3, label: "详细" },
                      { v: 4, label: "图表" }
                    ].map((o) => `
                      <button class="seg-btn ${osdLevel === o.v ? "active" : ""}" data-osd-level="${o.v}" data-focusable="true">${o.label}</button>
                    `).join("")}
                  </div>
                </div>
              </div>

              <div class="ck-sliders-pair">
                ${renderStepSlider({
                  idPrefix: "bright",
                  value: sys.brightnessPercent ?? 70,
                  min: 5,
                  displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "外接屏",
                  disabled: !sys.brightnessSupported,
                  iconHtml: sunSvg
                })}
                ${renderStepSlider({
                  idPrefix: "vol",
                  value: sys.volumePercent ?? 50,
                  min: 0,
                  muted: !!sys.isMuted,
                  muteBtnId: "btnToggleMute",
                  iconHtml: volSvg
                })}
              </div>

              <div class="ck-fan-widget">
                <div class="ck-widget-head">
                  <div style="display:flex;align-items:center;gap:6px;">
                    <span>风扇控制</span>
                    <span class="hh-card-badge live">${curTempEval}°C → ${fanMode === "manual" ? `${manualFanPct}%` : `${curveEvalPct}%`}</span>
                  </div>
                  <div class="seg-group compact">
                    ${[
                      { value: "auto", label: "自动静音" },
                      { value: "curve", label: "温控曲线" },
                      { value: "manual", label: "手动定速" }
                    ].map((opt) => `
                      <button class="seg-btn ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
                    `).join("")}
                  </div>
                </div>

                ${fanMode === "manual" ? `
                  <div class="ck-fan-manual-body">
                    <div style="display:flex;align-items:center;gap:6px;">
                      <button id="btnFanPctDec" class="step-btn" data-focusable="true">−5%</button>
                      <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" style="flex:1;" />
                      <strong id="fanManualVal" style="min-width:36px;text-align:right;">${manualFanPct}%</strong>
                      <button id="btnFanPctInc" class="step-btn" data-focusable="true">+5%</button>
                    </div>
                    <div style="display:grid;grid-template-columns:repeat(6,1fr);gap:5px;">
                      ${[30, 45, 60, 75, 90, 100].map((p) => `
                        <button class="pill-btn small ${manualFanPct === p ? "active" : ""}" data-fan-quick-pct="${p}" data-focusable="true">${p}%</button>
                      `).join("")}
                    </div>
                  </div>
                ` : `
                  <div class="ck-fan-curve-body">
                    <div class="ck-curve-pills">
                      ${fanCurves.map((c) => `
                        <button class="bento-link-pill ${activeCurve.id === c.id ? "is-sel" : ""}" data-select-fan-curve="${esc(c.id)}" data-focusable="true">${esc(c.name.split(" ")[0])}</button>
                      `).join("")}
                    </div>
                    <div class="ck-fan-svg-wrap">
                      ${renderFanCurveSvg(activeCurve, curTempEval, curveEvalPct)}
                    </div>
                  </div>
                `}
              </div>
            </section>

            <!-- Col 3: Running Game Status & 2x4 Quick Actions -->
            <section class="hh-card ck-panel">
              <div class="hh-card-head" style="margin-bottom:0;">
                <span class="hh-card-title">前台游戏与掌机工具箱</span>
                <span class="hh-card-badge ${active ? "live" : ""}">${active ? "游戏运行中" : "桌面待命"}</span>
              </div>

              ${active && gamePerf ? `
                <div class="ck-game-hero-box">
                  <div class="ck-game-hero-top">
                    ${active.coverImageUrl
                      ? `<img class="ck-game-poster" src="${esc(active.coverImageUrl)}" alt="" onerror="this.style.display='none'" />`
                      : `<div class="ck-game-poster placeholder">🎮</div>`}
                    <div class="ck-game-hero-meta">
                      <strong class="ck-game-title">${esc(active.title)}</strong>
                      <span class="ck-muted">${inj?.isInjected ? `已注入 ${formatConfigSummary(inj.appliedConfig || state.config, true)}` : `${active.platform} · 原生未注入`}</span>
                    </div>
                    <div style="display:flex;align-items:center;gap:5px;flex-shrink:0;">
                      <button id="btnHomeGoOpti" class="pill-btn small active" data-focusable="true">超分 ▸</button>
                      ${renderSwitch("swGamePerfEnabled", !!gamePerf.enabled)}
                    </div>
                  </div>
                  <div class="ck-game-hero-ctrl">
                    <div class="bento-inline-stepper">
                      <button id="btnGamePerfTdpDec" class="step-btn" data-focusable="true">−</button>
                      <span>${gamePerf.tdpWatts || curTdp}W</span>
                      <button id="btnGamePerfTdpInc" class="step-btn" data-focusable="true">+</button>
                    </div>
                    ${renderSegmented("data-game-perf-hz", [
                      { value: "0", label: "跟随全局" },
                      { value: "60", label: "60Hz" },
                      { value: "120", label: "120Hz" },
                      { value: "-1", label: "动态" }
                    ], String(gamePerf.refreshRateHz || 0), "compact")}
                  </div>
                </div>
              ` : `
                <div class="ck-game-hero-box" style="justify-content:center;gap:6px;">
                  <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;">
                    <div>
                      <strong style="font-size:13px;">当前处于桌面待命状态</strong>
                      <span class="ck-muted" style="display:block;margin-top:2px;">启动游戏后可在此绑定独立功耗与刷新率${savedProfileCount > 0 ? `（已保存 ${savedProfileCount} 款专属方案）` : ""}</span>
                    </div>
                    <button id="btnHomeGoOpti" class="pill-btn small active" data-focusable="true">游戏与超分 ▸</button>
                  </div>
                </div>
              `}

              <div class="ck-tools-widget">
                <div class="ck-widget-head">
                  <span>掌机快捷操作</span>
                  <span class="ck-muted">挂起 · 键盘 · 截屏 · 窗口</span>
                </div>
                <div class="ck-tools-grid-4">
                  <button class="toolbox-tile compact ${frozen.length > 0 ? "active" : ""}" data-toolbox="toggle_fridge" data-focusable="true">${svgIcon("fridge")}<span>${frozen.length > 0 ? `解冻(${frozen.length})` : "游戏冰箱"}</span></button>
                  <button class="toolbox-tile compact" data-toolbox="osk" data-focusable="true">${svgIcon("osk")}<span>屏幕键盘</span></button>
                  <button class="toolbox-tile compact" data-toolbox="screenshot" data-focusable="true">${svgIcon("shot")}<span>区域截屏</span></button>
                  <button id="btnToolboxMouse" class="toolbox-tile compact ${settings.gamepadMouseModeEnabled ? "active" : ""}" data-focusable="true">${svgIcon("mouse")}<span>手柄鼠标</span></button>
                  <button class="toolbox-tile compact" data-toolbox="task_view" data-focusable="true">${svgIcon("switchwin")}<span>切换窗口</span></button>
                  <button class="toolbox-tile compact" data-toolbox="show_desktop" data-focusable="true">${svgIcon("desktop")}<span>返回桌面</span></button>
                  <button id="btnToggleOfflineWifi" class="toolbox-tile compact ${sys.wifiRadioOff ? "active" : ""}" data-focusable="true" ${sys.wifiRadioSupported === false ? "disabled" : ""}>${svgIcon("wifi")}<span>${sys.wifiRadioOff ? "开启 Wi-Fi" : "关闭 Wi-Fi"}</span></button>
                  <button id="btnKillGame" class="toolbox-tile compact ${state.killArmed ? "armed" : ""}" data-focusable="true">${svgIcon("closegame")}<span>${state.killArmed ? "确认关闭" : "关闭游戏"}</span></button>
                </div>
              </div>
            </section>
          </div>
        </div>
      `;
    }

    // Default Layout: Handheld Bento Tile Deck
    return `
      <div class="bento-home-shell">
        ${svcAlertHtml}

        <div class="bento-balanced-grid">
          <!-- Left Column: Power Deck + Dedicated Running Game Card -->
          <div class="bento-col-left">
            <section class="hh-card bento-power-card">
              <div class="bento-power-top">
                <div class="bento-tdp-hero">
                  <span class="bento-eyebrow">TDP 目标封装功耗</span>
                  <div class="bento-tdp-hero-line">
                    <strong id="bentoTdpHeroVal" class="bento-big-watts">${curTdp}</strong>
                    <span class="bento-big-unit">W</span>
                    <span id="consoleTdpBadge" class="hh-card-badge live">${esc(resolveTdpSceneLabel(curTdp))}</span>
                  </div>
                </div>

                <div class="bento-live-metrics">
                  <div class="bento-metric">
                    <span class="bento-metric-label">APU 实测</span>
                    <strong id="pfApuVal" class="bento-metric-val">${esc(pfUi.apuPowerText || "-- W")}</strong>
                  </div>
                  <div class="bento-metric">
                    <span class="bento-metric-label">整机功耗</span>
                    <strong id="pfSysVal" class="bento-metric-val">${esc(pfUi.sysVal)}</strong>
                  </div>
                  <div class="bento-metric">
                    <span class="bento-metric-label">供电状态</span>
                    <strong id="pfEtaVal" class="bento-metric-val accent-teal">${esc(pfUi.etaVal)}</strong>
                    <span id="pfBatVal" style="display:none;">${esc(pfUi.batVal)}</span>
                    <span id="pfModeBadge" class="hh-card-badge ${pfUi.modeClass}" style="display:none;">${esc(pfUi.modeText)}</span>
                  </div>
                </div>
              </div>

              <div class="bento-watt-row">
                ${customPresets.map((w) => `
                  <button class="bento-watt-chip tdp-chip-btn ${curTdp === w ? "active" : ""}" data-tdp-watt="${w}" data-focusable="true">${w}W</button>
                `).join("")}
              </div>

              <div class="bento-tdp-slider-bar">
                <button id="btnTdpDec" class="step-btn bento-step-lg" data-focusable="true" title="降低 1W">−1W</button>
                <div class="bento-tdp-track-wrap">
                  <div id="tdpTrackFill" class="bento-tdp-track-fill" style="width:${tdpFillPct.toFixed(1)}%;"></div>
                  <span class="bento-tdp-track-hint">滑动调节功耗 (8–120W)</span>
                  <input id="tdpRangeInput" class="bento-tdp-range-overlay" type="range" min="8" max="120" step="1" value="${curTdp}" aria-label="目标 TDP 滑块" />
                </div>
                <button id="btnTdpInc" class="step-btn bento-step-lg" data-focusable="true" title="提高 1W">+1W</button>
              </div>

              <div class="bento-power-subbar">
                <div class="bento-subbar-item">
                  <span class="bento-subbar-label">插拔电自动切 (${settings.batteryTdpWatts || 25}W/${settings.acTdpWatts || 55}W)</span>
                  ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
                </div>
                <div class="bento-subbar-divider"></div>
                <div class="bento-subbar-item">
                  <span class="bento-subbar-label" title="锁定 SPL = sPPT = fPPT">稳态功耗锁</span>
                  ${renderSwitch("swFlatLock", flatLock)}
                </div>
              </div>
            </section>

            <section class="hh-card bento-game-card">
              ${active && gamePerf ? `
                <div class="bento-game-card-top">
                  ${active.coverImageUrl
                    ? `<img class="bento-game-poster" src="${esc(active.coverImageUrl)}" alt="" onerror="this.style.display='none'" />`
                    : `<div class="bento-game-poster placeholder">🎮</div>`}
                  <div class="bento-game-meta">
                    <div class="bento-game-title-row">
                      <strong class="bento-game-fulltitle">${esc(active.title)}</strong>
                      <span class="hh-card-badge live">正在运行</span>
                    </div>
                    <span class="bento-game-desc">${gamePerf.enabled
                      ? `已绑定专属方案：${gamePerf.tdpWatts}W${gamePerf.refreshRateHz === -1 ? " · 动态刷新率" : (gamePerf.refreshRateHz > 0 ? ` · ${gamePerf.refreshRateHz}Hz` : "")}，退游恢复全局`
                      : "调整下方瓦数或刷新率即可为当前游戏自动保存专属方案"}</span>
                  </div>
                  <div class="bento-game-top-actions">
                    <button id="btnHomeGoOpti" class="pill-btn small active" data-focusable="true">超分与插帧 ▸</button>
                    ${renderSwitch("swGamePerfEnabled", !!gamePerf.enabled)}
                  </div>
                </div>

                <div class="bento-game-card-controls">
                  <div class="bento-game-ctrl-left">
                    <div class="bento-inline-stepper">
                      <button id="btnGamePerfTdpDec" class="step-btn" data-focusable="true">−</button>
                      <span>${gamePerf.tdpWatts || curTdp}W</span>
                      <button id="btnGamePerfTdpInc" class="step-btn" data-focusable="true">+</button>
                    </div>
                    ${renderSegmented("data-game-perf-hz", [
                      { value: "0", label: "跟随全局" },
                      { value: "60", label: "60Hz" },
                      { value: "120", label: "120Hz" },
                      { value: "-1", label: "动态" }
                    ], String(gamePerf.refreshRateHz || 0), "compact")}
                  </div>
                </div>
              ` : `
                <div class="bento-game-empty">
                  <div>
                    <strong class="hh-card-title">桌面待命 · 当前未运行游戏</strong>
                    <span class="bento-game-desc" style="display:block;margin-top:4px;">进入游戏后此处自动识别并可绑定该游戏独立功耗与刷新率${savedProfileCount > 0 ? `（已保存 ${savedProfileCount} 款游戏专属方案）` : "；也可前往「游戏与超分」提前配置 FSR 4 超分"}</span>
                  </div>
                  <button id="btnHomeGoOpti" class="pill-btn small active" data-focusable="true">游戏与超分 ▸</button>
                </div>
              `}
            </section>
          </div>

          <!-- Right Column: Display & Audio + Clean Horizontal Modes Card (OSD + CPU Boost + Fan) + 2x4 Handheld Toolbox -->
          <div class="bento-col-right">
            <section class="hh-card bento-av-card">
              <div class="bento-av-head">
                <div>
                  <span class="hh-card-title">显示与音量</span>
                  <span id="dispRefreshSub" class="bento-inline-sub">${esc(formatRefreshTag(sys))}${sys.dynamicRefreshSupported ? " · VRR" : ""}</span>
                </div>
                ${renderSegmented("data-refresh-hz", hzOptions, activeRefreshVal, "compact")}
              </div>

              <div class="bento-sliders-pair">
                ${renderStepSlider({
                  idPrefix: "bright",
                  value: sys.brightnessPercent ?? 70,
                  min: 5,
                  displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "外接屏",
                  disabled: !sys.brightnessSupported,
                  iconHtml: sunSvg
                })}
                ${renderStepSlider({
                  idPrefix: "vol",
                  value: sys.volumePercent ?? 50,
                  min: 0,
                  muted: !!sys.isMuted,
                  muteBtnId: "btnToggleMute",
                  iconHtml: volSvg
                })}
              </div>
            </section>

            <section class="hh-card bento-modes-card">
              <div class="bento-ctrl-row">
                <div class="bento-ctrl-meta">
                  <span class="bento-ctrl-title">性能悬浮窗</span>
                  <button id="btnCycleOsdPos" class="bento-link-pill" data-focusable="true">停靠: ${osdPosLabel} ▸</button>
                </div>
                <div class="seg-group compact">
                  ${[
                    { v: 0, label: "关闭" },
                    { v: 1, label: "极简" },
                    { v: 2, label: "紧凑" },
                    { v: 3, label: "详细" },
                    { v: 4, label: "图表" }
                  ].map((o) => `
                    <button class="seg-btn ${osdLevel === o.v ? "active" : ""}" data-osd-level="${o.v}" data-focusable="true">${o.label}</button>
                  `).join("")}
                </div>
              </div>

              <div class="bento-ctrl-row">
                <div class="bento-ctrl-meta">
                  <span class="bento-ctrl-title">CPU 睿频</span>
                  <span class="hh-card-badge ${boostMode !== "off" ? "live" : ""}">${boostBadgeText}</span>
                </div>
                ${renderSegmented("data-cpu-boost", [
                  { value: "off", label: "关闭睿频" },
                  { value: "efficient", label: "智能平衡" },
                  { value: "aggressive", label: "积极满血" }
                ], boostMode, "compact")}
              </div>

              <div class="bento-ctrl-row">
                <div class="bento-ctrl-meta">
                  <span class="bento-ctrl-title">风扇控制</span>
                  <span id="fanRpmBadge" class="hh-card-badge ${hw.ecReady ? "live" : ""}">${fanBadgeText}</span>
                </div>
                <div class="seg-group compact">
                  ${[
                    { value: "auto", label: "自动静音" },
                    { value: "curve", label: "温控曲线" },
                    { value: "manual", label: "手动定速" }
                  ].map((opt) => `
                    <button class="seg-btn ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
                  `).join("")}
                </div>
                ${fanMode === "manual" ? `
                  <div style="display:flex;align-items:center;gap:6px;margin-top:6px;">
                    <button id="btnFanPctDec" class="step-btn" data-focusable="true">−5%</button>
                    <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" style="flex:1;" />
                    <strong id="fanManualVal" style="font-size:12px;min-width:34px;text-align:right;">${manualFanPct}%</strong>
                    <button id="btnFanPctInc" class="step-btn" data-focusable="true">+5%</button>
                  </div>
                ` : ""}
              </div>
            </section>

            <section class="hh-card bento-toolbox-card">
              <div class="bento-toolbox-head">
                <span class="hh-card-title">掌机快捷操作</span>
                <span class="bento-inline-sub">游戏挂起 · 屏幕键盘 · 截屏 · 窗口管理</span>
              </div>
              <div class="bento-toolbox-grid">
                <button class="toolbox-tile bento-action-btn ${frozen.length > 0 ? "active" : ""}" data-toolbox="toggle_fridge" data-focusable="true">${svgIcon("fridge")}<span>${frozen.length > 0 ? `解冻游戏 (${frozen.length})` : "游戏冰箱"}</span></button>
                <button class="toolbox-tile bento-action-btn" data-toolbox="osk" data-focusable="true">${svgIcon("osk")}<span>屏幕键盘</span></button>
                <button class="toolbox-tile bento-action-btn" data-toolbox="screenshot" data-focusable="true">${svgIcon("shot")}<span>区域截屏</span></button>
                <button id="btnToolboxMouse" class="toolbox-tile bento-action-btn ${settings.gamepadMouseModeEnabled ? "active" : ""}" data-focusable="true">${svgIcon("mouse")}<span>手柄鼠标</span></button>
                <button class="toolbox-tile bento-action-btn" data-toolbox="task_view" data-focusable="true">${svgIcon("switchwin")}<span>切换窗口</span></button>
                <button class="toolbox-tile bento-action-btn" data-toolbox="show_desktop" data-focusable="true">${svgIcon("desktop")}<span>返回桌面</span></button>
                <button id="btnToggleOfflineWifi" class="toolbox-tile bento-action-btn ${sys.wifiRadioOff ? "active" : ""}" data-focusable="true" ${sys.wifiRadioSupported === false ? "disabled" : ""}>${svgIcon("wifi")}<span>${sys.wifiRadioOff ? "开启 Wi-Fi" : "关闭 Wi-Fi"}</span></button>
                <button id="btnKillGame" class="toolbox-tile bento-action-btn ${state.killArmed ? "armed" : ""}" data-focusable="true">${svgIcon("closegame")}<span>${state.killArmed ? "确认关闭" : "关闭游戏"}</span></button>
              </div>
            </section>
          </div>
        </div>
      </div>
    `;
  }

  // ---------------- Console View 3: Deep Hardware Tuning & Live Sensor Monitor ----------------
  function renderMonitorView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const level = settings.overlayLevel ?? 0;
    const pos = settings.overlayPosition || "top-center";
    const opacity = settings.overlayOpacity ?? 0.86;
    const osdRenderer = settings.overlayRenderer || "native";
    const rtssInstalled = !!sys.rtssInstalled;
    const rtssRunning = !!sys.rtssRunning;
    const activeBackend = sys.activeOverlayBackend || "native";
    const daemonVer = hw.daemonVersion || 0;
    const vramUsedGb = ((sys.vramUsedMb || 0) / 1024).toFixed(1);
    const vramTotalGb = sys.vramTotalMb > 0 ? (sys.vramTotalMb / 1024).toFixed(0) : "16";
    const sharedGb = ((sys.sharedVramUsedMb || 0) / 1024).toFixed(1);
    const fpsText = sys.currentFps > 0 ? `${sys.currentFps} FPS` : "待检测游戏";
    const fpsSubText = sys.frameTimeMs > 0
      ? `${sys.frameTimeMs.toFixed(1)} ms · ETW 无钩子`
      : (daemonVer >= 3 ? "DX11 / DX12 / Vulkan 就绪" : "升级硬件服务 v4 解锁");

    const curTdp = settings.currentTdpWatts || 40;
    const fanMode = normalizeFanModeUi(settings.fanMode);
    const manualFanPct = settings.manualFanPercent || 55;
    const fanCurves = (settings.fanCurves && settings.fanCurves.length) ? settings.fanCurves : getDefaultFanCurves();
    const activeCurve = getActiveFanCurve(settings);
    const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
    const curTempEval = maxTemp > 0 ? maxTemp : 48;
    const curveEvalPct = evaluateFanCurvePct(curTempEval, activeCurve.points);
    const pwmPctNow = hw.appliedFanPwm > 0 ? Math.round((hw.appliedFanPwm / 244) * 100) : 0;

    const steadyMode = String(settings.steadyEcoMode || "off").toLowerCase();
    const steadyCpuOn = settings.steadyEcoCpuEnabled !== false;
    const steadyBgOn = settings.steadyEcoBackgroundEnabled !== false;
    const steadyPerOn = settings.steadyEcoPeripheralEnabled !== false;
    const steadyEpp = settings.steadyEcoCpuEpp || 70;
    const steadyEppSnap = steadyEpp <= 66 ? "65" : (steadyEpp >= 77 ? "80" : "70");
    const unlockThreeStage = !!settings.unlockThreeStagePower;
    const flatLock = !!settings.flatPowerLock;
    const slowPpt = settings.slowPptWatts || curTdp;
    const fastPpt = settings.fastPptWatts || (flatLock ? curTdp : Math.min(140, curTdp + 2));

    const previewLines = level === 0
      ? ["悬浮窗已关闭（开启后鼠标点击完全穿透）"]
      : level === 1
        ? [`${sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : ""}APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W · C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}° · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`]
        : [
            `${getActiveGame()?.title || "HaloDeck"} · ${fpsText}`,
            `APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W · GPU ${sys.gpuUsagePercent || 0}% (${sys.gpuTempC || "--"}°C) · CPU ${sys.cpuUsagePercent || 0}% (${sys.cpuTempC || "--"}°C)`,
            `VRAM ${vramUsedGb}/${vramTotalGb}G (共享${sharedGb}G) · ${formatPowerBadge(sys)}`
          ];

    return `
      <div class="page-scroll">
        <!-- Top Row: Fan 5-Point Curve Studio + CPU Sub-Base & 3-Stage Power Precision -->
        <div class="mon-top-grid">
          <div class="hh-card">
            <div class="hh-card-head">
              <span class="hh-card-title">双风扇智能温控与 5 点曲线</span>
              <span id="fanRpmBadge" class="hh-card-badge ${hw.ecReady ? "live" : ""}">${hw.ecReady && hw.fanRpm > 0 ? `${hw.fanRpm} RPM · PWM ${pwmPctNow}%` : "EC 待命"}</span>
            </div>

            <div class="fan-mode-pills" style="grid-template-columns:repeat(3,minmax(0,1fr));margin-bottom:8px;">
              ${[
                { value: "auto", label: "EC 自动静音" },
                { value: "curve", label: "温度曲线" },
                { value: "manual", label: "手动定速 %" }
              ].map((opt) => `
                <button class="pill-btn ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
              `).join("")}
            </div>

            ${fanMode === "manual" ? `
              <div class="fan-direct-box is-active">
                <div class="slider-header">
                  <span>定速风扇占空比</span>
                  <div style="display:flex;align-items:center;gap:6px;">
                    <button id="btnFanPctDec" class="step-btn" data-focusable="true">−5%</button>
                    <strong id="fanManualVal">${manualFanPct}%</strong>
                    <button id="btnFanPctInc" class="step-btn" data-focusable="true">+5%</button>
                  </div>
                </div>
                <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" />
                <div class="fan-quick-grid">
                  ${[30, 45, 60, 75, 90, 100].map((p) => `
                    <button class="pill-btn small ${manualFanPct === p ? "active" : ""}" data-fan-quick-pct="${p}" data-focusable="true">${p}%</button>
                  `).join("")}
                </div>
              </div>
            ` : `
              <div class="fan-curve-box is-active" style="margin-bottom:0;">
                <div class="fan-curve-head">
                  <div class="fan-curve-selector" style="margin:0;">
                    ${fanCurves.map((c) => `
                      <button class="pill-btn small ${activeCurve.id === c.id ? "active" : ""}" data-select-fan-curve="${esc(c.id)}" data-focusable="true">${esc(c.name.split(" ")[0])}</button>
                    `).join("")}
                  </div>
                  <div style="display:flex;align-items:center;gap:5px;">
                    <span class="hh-card-badge live">${curTempEval}°C → ${curveEvalPct}%</span>
                    <button id="btnToggleCurveNodes" class="pill-btn small" data-focusable="true">${state.fanNodesOpen ? "收起节点 ▴" : "编辑5点 ▾"}</button>
                  </div>
                </div>
                ${renderFanCurveSvg(activeCurve, curTempEval, curveEvalPct)}
                ${state.fanNodesOpen ? `
                  <div style="display:flex;justify-content:flex-end;gap:6px;margin:6px 0;">
                    <button id="btnAddFanCurve" class="pill-btn small" data-focusable="true">+ 新建曲线</button>
                    ${fanCurves.length > 1 ? `<button id="btnDelFanCurve" class="pill-btn small" data-focusable="true">删除当前</button>` : ""}
                  </div>
                  <div class="fan-node-list">
                    ${(activeCurve.points || []).map((pt, idx) => `
                      <div class="fan-node-row">
                        <span class="fan-node-idx">P${idx + 1}</span>
                        <div class="fan-node-ctrl">
                          <button class="step-btn" data-curve-node-adj="${idx}:tempC:-2" data-focusable="true">−2°</button>
                          <strong>${pt.tempC}°C</strong>
                          <button class="step-btn" data-curve-node-adj="${idx}:tempC:2" data-focusable="true">+2°</button>
                        </div>
                        <span style="color:var(--text-muted);font-size:11px;">→</span>
                        <div class="fan-node-ctrl">
                          <button class="step-btn" data-curve-node-adj="${idx}:fanPercent:-5" data-focusable="true">−5%</button>
                          <strong>${pt.fanPercent}%</strong>
                          <button class="step-btn" data-curve-node-adj="${idx}:fanPercent:5" data-focusable="true">+5%</button>
                        </div>
                      </div>
                    `).join("")}
                  </div>
                ` : ""}
              </div>
            `}
          </div>

          <div class="hh-card">
            <div class="hh-card-head">
              <span class="hh-card-title">CPU 睿频模式与三段功耗策略</span>
              <span class="hh-card-badge">${esc(sys.cpuTopologyBadge || "单CCD 8核 · 32MB L3")}</span>
            </div>

            <div class="cw-group">
              <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">CPU 睿频模式</span>
                  <span class="cw-row-sub">掌机推荐关闭睿频，省电降温并将封装功耗优先分配给 8060S 核显</span>
                </div>
                ${renderSegmented("data-cpu-boost", [
                  { value: "off", label: "关闭睿频" },
                  { value: "efficient", label: "智能平衡" },
                  { value: "aggressive", label: "积极满血" }
                ], sys.cpuBoostMode || "off", "compact")}
              </div>

              <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">拔插电源自动切功耗</span>
                  <span class="cw-row-sub">电池续航 ${settings.batteryTdpWatts || 25}W · 外接电源 ${settings.acTdpWatts || 55}W</span>
                </div>
                ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
              </div>

              <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">SMU 功耗策略 (${flatLock ? `稳态锁定 ${curTdp}W` : `SPL ${curTdp}W / fPPT ${fastPpt}W`})</span>
                </div>
                <div style="display:flex;align-items:center;gap:8px;">
                  <span style="font-size:11px;color:var(--text-secondary);">稳态功耗锁</span>
                  ${renderSwitch("swFlatLock", flatLock)}
                  <span style="font-size:11px;color:var(--text-secondary);">独立三段调节</span>
                  ${renderSwitch("swUnlockThreeStage", unlockThreeStage)}
                </div>
              </div>

              ${unlockThreeStage ? `
                <div class="cw-disclosure-body" style="padding:8px 12px;">
                  <div class="tdp-three-stage">
                    <div class="stage-row">
                      <span>sPPT 慢速升压</span>
                      <div style="display:flex;align-items:center;gap:6px;">
                        <button class="step-btn" data-stage-step="slow:-1" data-focusable="true">−1</button>
                        <strong id="slowPptVal">${slowPpt} W</strong>
                        <button class="step-btn" data-stage-step="slow:1" data-focusable="true">+1</button>
                      </div>
                    </div>
                    <input id="slowPptRange" class="hh-range" type="range" min="8" max="130" step="1" value="${slowPpt}" />
                    <div class="stage-row" style="margin-top:4px;">
                      <span>fPPT 瞬时峰值</span>
                      <div style="display:flex;align-items:center;gap:6px;">
                        <button class="step-btn" data-stage-step="fast:-1" data-focusable="true">−1</button>
                        <strong id="fastPptVal">${fastPpt} W</strong>
                        <button class="step-btn" data-stage-step="fast:1" data-focusable="true">+1</button>
                      </div>
                    </div>
                    <input id="fastPptRange" class="hh-range" type="range" min="8" max="140" step="1" value="${fastPpt}" />
                  </div>
                </div>
              ` : ""}
            </div>
          </div>
        </div>

        <!-- 8-KPI Live Hardware Sensor Matrix -->
        <div class="mon-kpi-grid">
          <div class="mon-kpi-card">
            <span class="pf-label">当前游戏帧率</span>
            <span id="kpiFps" class="mon-kpi-val accent-blue">${esc(fpsText)}</span>
            <span id="kpiFpsSub" class="mon-kpi-sub">${esc(fpsSubText)}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">APU 封装功耗</span>
            <span id="kpiApuW" class="mon-kpi-val">${sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W"}</span>
            <span id="kpiApuSub" class="mon-kpi-sub">设定 ${curTdp}W · ${hw.hwStapmLimitWatts != null ? `PM ${hw.hwStapmLimitWatts.toFixed(0)}W` : "ADL2"}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">8060S 负载与频率</span>
            <span id="kpiGpuLoad" class="mon-kpi-val">${sys.gpuUsagePercent || 0}%</span>
            <span id="kpiGpuSub" class="mon-kpi-sub">${sys.gfxClockMhz || 0} MHz · 显存 ${sys.memClockMhz || 0} MHz</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">显存占用 (专+享)</span>
            <span id="kpiVram" class="mon-kpi-val">${vramUsedGb} / ${vramTotalGb} GB</span>
            <span id="kpiVramSub" class="mon-kpi-sub">专用 ${sys.vramUsedMb || 0}MB · 共享 ${sharedGb}G</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">CPU 占用与主频</span>
            <span id="kpiCpuLoad" class="mon-kpi-val">${sys.cpuUsagePercent || 0}%</span>
            <span id="kpiCpuSub" class="mon-kpi-sub">${sys.cpuClockMhz > 0 ? `${sys.cpuClockMhz} MHz` : "Zen 5"}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">CPU / GPU 温度</span>
            <span id="kpiCpuTemp" class="mon-kpi-val">CPU ${sys.cpuTempC || "--"}°C</span>
            <span id="kpiGpuTempSub" class="mon-kpi-sub">GPU ${sys.gpuTempC || "--"}°C · ${hw.fanRpm || 0} RPM</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">系统内存 (UMA)</span>
            <span id="kpiMem" class="mon-kpi-val">${sys.totalRamGb > 0 ? `${sys.usedRamGb} / ${sys.totalRamGb} GB` : "-- GB"}</span>
            <span class="mon-kpi-sub">提交上限 ${snap.memoryHealth?.commitLimitGb || "--"} GB</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">整机电源状态</span>
            <span id="kpiBat" class="mon-kpi-val" style="font-size:15px;">${esc(formatPowerBadge(sys))}</span>
            <span id="kpiBatSub" class="mon-kpi-sub">${sys.estimatedChargerWatts != null ? `外接输入 ≈ ${sys.estimatedChargerWatts.toFixed(1)}W` : "实时电源估算"}</span>
          </div>
        </div>

        <!-- 60s Live Sparklines -->
        <div class="charts-grid">
          <div class="chart-card">
            <div class="hh-card-head" style="margin-bottom:4px;"><span class="hh-card-title">60s APU 功耗 (W) & 温度 (°C)</span></div>
            <div id="svgChartPowerTemp">
              ${buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 120, 100, "#2dd4bf", "#fbbf24", "APU W", "Temp °C")}
            </div>
          </div>
          <div class="chart-card">
            <div class="hh-card-head" style="margin-bottom:4px;"><span class="hh-card-title">60s GPU (%) & CPU (%) 利用率</span></div>
            <div id="svgChartGpuCpu">
              ${buildSvgChart(sys.gpuUsageHistory || [], sys.cpuUsageHistory || [], 100, 100, "#38bdf8", "#a78bfa", "GPU %", "CPU %")}
            </div>
          </div>
        </div>

        <!-- OSD Overlay Position, Opacity & Live Preview -->
        <div class="mon-top-grid">
          <div class="hh-card">
            <div class="hh-card-head">
              <span class="hh-card-title">OSD 性能悬浮窗样式设置</span>
              <span class="hh-card-badge ${level > 0 ? "live" : ""}">${level === 0 ? "已关闭" : `档位 ${level}${activeBackend === "rtss" ? " · RTSS" : ""}`}</span>
            </div>
            <div class="osd-level-pills" style="margin-bottom:8px;">
              ${[
                { v: 0, label: "关闭" },
                { v: 1, label: "极简" },
                { v: 2, label: "紧凑" },
                { v: 3, label: "详细" },
                { v: 4, label: "图表" }
              ].map((o) => `
                <button class="pill-btn ${level === o.v ? "active" : ""}" data-osd-level="${o.v}" data-focusable="true">${o.label}</button>
              `).join("")}
            </div>
            <div class="cw-group">
              <div class="cw-row" style="min-height:42px;padding:7px 12px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">渲染通道（全屏穿透）</span>
                  <span class="cw-row-sub">${rtssRunning ? "RTSS 运行中 · 支持独占全屏穿透" : (rtssInstalled ? "已安装 RTSS（未启动）· 点击下方模式自动启动" : "未安装 RTSS · 点击穿透模式将自动安装并启动")}</span>
                </div>
                <div style="display:flex;align-items:center;gap:6px;">
                  ${!rtssRunning && osdRenderer !== "native" ? `<button id="btnEnsureRtss" class="btn btn-sm btn-primary" data-focusable="true">${rtssInstalled ? "启动 RTSS" : "安装 RTSS"}</button>` : ""}
                  ${renderSegmented("data-osd-renderer", [
                    { value: "native", label: "原生悬浮窗" },
                    { value: "auto_rtss", label: "智能回退" },
                    { value: "rtss", label: "RTSS 穿透" }
                  ], osdRenderer, "compact")}
                </div>
              </div>
              <div class="cw-row" style="min-height:40px;padding:7px 12px;">
                <span class="cw-row-title">停靠位置</span>
                ${renderSegmented("data-osd-pos", [
                  { value: "top-left", label: "左上角" },
                  { value: "top-center", label: "顶部居中" },
                  { value: "top-right", label: "右上角" }
                ], pos, "compact")}
              </div>
              <div class="cw-row" style="min-height:40px;padding:7px 12px;">
                <span class="cw-row-title">背景不透明度</span>
                ${renderSegmented("data-osd-opacity", [
                  { value: "0.55", label: "55%" },
                  { value: "0.75", label: "75%" },
                  { value: "0.88", label: "88%" },
                  { value: "1", label: "100%" }
                ], String(opacity >= 0.95 ? 1 : opacity >= 0.82 ? 0.88 : opacity >= 0.65 ? 0.75 : 0.55), "compact")}
              </div>
              <div class="cw-row" style="min-height:42px;padding:7px 12px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">管理员游戏侧栏 / OSD 穿透 <span class="hh-card-badge ${snap.isElevated ? "live" : ""}" style="margin-left:4px;">${snap.isElevated ? "当前管理员" : "普通权限"}</span></span>
                  <span class="cw-row-sub">平时保持普通权限即可兼容 Steam 大屏幕；仅游玩《终末地》《异环》等提权游戏出现侧栏/OSD 穿透异常时按需点击提权</span>
                </div>
                ${!snap.isElevated ? `<button id="btnRelaunchAdmin" class="btn btn-sm btn-primary" data-focusable="true">临时提权重启</button>` : `<span class="hh-card-badge live">已提权</span>`}
              </div>
            </div>
          </div>

          <div class="hh-card">
            <div class="hh-card-head">
              <span class="hh-card-title">悬浮窗实时预览</span>
              <span class="hh-card-badge">${esc(pos)} · ${Math.round(opacity * 100)}%</span>
            </div>
            <div class="osd-preview-box pos-${esc(pos)}">
              <div class="osd-preview-hud" style="opacity:${opacity};">
                ${previewLines.map((line) => `<span>${esc(line)}</span>`).join("")}
              </div>
            </div>
          </div>
        </div>
      </div>
    `;
  }

  function ensureGpdControllerLoaded() {
    if (state.gpdLoaded || state.gpdFetching) return;
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
        if ((!IS_QAM && state.tab === "controls") || (IS_QAM && state.qamTab === "controls")) {
          render();
        }
      }
    }, 30);
  }

  // ---------------- Console View 4: Controller Mapping, Appearance Studio & System Settings ----------------
  function hasPendingGpdChanges(gpd, draft) {
    if (!gpd?.connected) return false;
    const origL4 = resolveGpdSlotState(gpd.l4Mode, gpd.l4Slots, gpd.l4KeyCode, gpd.l4ComboCodes);
    const origR4 = resolveGpdSlotState(gpd.r4Mode, gpd.r4Slots, gpd.r4KeyCode, gpd.r4ComboCodes);
    const comboEq = (a, b) => JSON.stringify(normalizeComboCodesList(a || [])) === JSON.stringify(b || []);
    return (
      origL4.legacyMode0 ||
      origR4.legacyMode0 ||
      draft.l4Key !== origL4.key ||
      draft.r4Key !== origR4.key ||
      !comboEq(draft.l4Combo, origL4.combo) ||
      !comboEq(draft.r4Combo, origR4.combo) ||
      draft.rumble !== (gpd.rumbleLevel ?? 1) ||
      draft.leftDeadzone !== (gpd.leftDeadzone ?? 0) ||
      draft.rightDeadzone !== (gpd.rightDeadzone ?? 0)
    );
  }

  function renderControlsView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const gpd = snap.gpdController || {};
    const draft = state.gpdDraft;

    ensureGpdControllerLoaded();

    const consoleButtons = Array.isArray(settings.consoleGamepadCombo?.buttons) ? settings.consoleGamepadCombo.buttons : ["BACK", "LB"];
    const qamButtons = Array.isArray(settings.qamGamepadCombo?.buttons) ? settings.qamGamepadCombo.buttons : ["BACK", "RB"];
    const osdButtons = Array.isArray(settings.osdGamepadCombo?.buttons) ? settings.osdGamepadCombo.buttons : ["BACK", "Y"];
    const insButtons = Array.isArray(settings.insertGamepadCombo?.buttons) ? settings.insertGamepadCombo.buttons : ["BACK", "START"];
    const interceptOsk = settings.interceptPhysicalOskButton !== false;
    const interceptMenuBtn = settings.interceptPhysicalMenuButton !== false;
    const interceptGuideBtn = settings.interceptGuideMenuButton !== false;
    const dirtyGpd = hasPendingGpdChanges(gpd, draft);

    const curTheme = settings.uiTheme === "frost" ? "frost" : "aurora";
    const curLayout = settings.uiLayout === "cockpit" ? "cockpit" : "bento";
    const curTdp = settings.currentTdpWatts || 40;
    const customPresets = (settings.customTdpPresets && settings.customTdpPresets.length)
      ? settings.customTdpPresets
      : [10, 15, 20, 25, 30, 45, 60];
    const hasCurrentPreset = customPresets.includes(curTdp);

    return `
      <div class="page-scroll">
        <div class="controls-layout">
          <!-- Left: GPD Back Buttons, Rumble, Mouse & Physical OSK Key -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">手柄背键与震动 (L4 / R4)</span>
                <div style="display:flex;align-items:center;gap:6px;">
                  <span class="hh-card-badge ${gpd.connected ? "live" : ""}">${gpd.connected ? `已连接 · FW X${gpd.firmwareX || "?"}${gpd.firmwareK ? ` / K${gpd.firmwareK}` : ""}` : "未检测到手柄"}</span>
                  <button id="btnResetGpdFactory" class="btn btn-sm" data-focusable="true" ${!gpd.connected || state.gpdSaving ? "disabled" : ""} title="清空 L4/R4 映射并恢复原厂震动与死区">恢复原厂</button>
                  <button id="btnReadGpd" class="btn btn-sm" data-focusable="true" ${state.gpdSaving ? "disabled" : ""}>刷新</button>
                </div>
              </div>

              <div class="backkey-slot-list">
                <div class="backkey-slot-row">
                  <div class="backkey-slot-meta">
                    <strong>L4 左背键</strong>
                    <span>全键盘 / 手柄键 / 实时录制 / 自由组合</span>
                  </div>
                  <div class="comp-actions">
                    <span id="l4SlotBadge" class="pill-badge">${esc(resolveBackSlotLabel(draft.l4Key, draft.l4Combo))}</span>
                    <button id="btnPickL4" class="btn btn-sm" data-focusable="true">修改 ▸</button>
                  </div>
                </div>
                <div class="backkey-slot-row">
                  <div class="backkey-slot-meta">
                    <strong>R4 右背键</strong>
                    <span>全键盘 / 手柄键 / 实时录制 / 自由组合</span>
                  </div>
                  <div class="comp-actions">
                    <span id="r4SlotBadge" class="pill-badge">${esc(resolveBackSlotLabel(draft.r4Key, draft.r4Combo))}</span>
                    <button id="btnPickR4" class="btn btn-sm" data-focusable="true">修改 ▸</button>
                  </div>
                </div>
              </div>

              <div class="cw-row" style="padding:10px 0 6px;">
                <span class="cw-row-title">马达震动</span>
                ${renderSegmented("data-gpd-rumble", [
                  { value: "0", label: "关闭" },
                  { value: "1", label: "弱" },
                  { value: "2", label: "强" }
                ], String(draft.rumble), "compact")}
              </div>

              <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;padding-top:8px;border-top:1px solid var(--border);">
                <span style="font-size:11px;color:var(--text-muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">硬件当前：L4 [${esc(gpd.l4KeyLabel || "未知")}] · R4 [${esc(gpd.r4KeyLabel || "未知")}]</span>
                <button id="btnSaveGpd" class="btn btn-sm btn-primary" data-focusable="true" ${!gpd.connected || state.gpdSaving ? "disabled" : ""}>${state.gpdSaving ? "写入中..." : (dirtyGpd ? "应用修改到手柄" : "应用到手柄")}</button>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">输入辅助与快捷呼出</span>
                <div style="display:flex;align-items:center;gap:6px;">
                  <button id="btnResetGamepadCombos" class="btn btn-sm" data-focusable="true" title="恢复默认手柄组合键与键盘热键">恢复默认</button>
                  <button id="btnTestOsk" class="btn btn-sm btn-primary" data-focusable="true">呼出屏幕键盘</button>
                </div>
              </div>

              <div class="cw-group">
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">手柄桌面鼠标模式</span>
                    <span class="cw-row-sub">左摇杆光标 · A左键 · B右键 · 右摇杆滚轮</span>
                  </div>
                  ${renderSwitch("swMouseMode", !!settings.gamepadMouseModeEnabled)}
                </div>
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">接管机身键盘键</span>
                    <span class="cw-row-sub">呼出 Win11 触摸/手柄键盘，替代老旧传统键盘</span>
                  </div>
                  ${renderSwitch("swInterceptOsk", interceptOsk)}
                </div>
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">接管机身快捷菜单键 (Vendor HID / F13)</span>
                    <span class="cw-row-sub">直连 GPD 0xFF00 HID + F13 + RawInput，兼容全批次 Win 5 固件短按呼出侧栏</span>
                  </div>
                  ${renderSwitch("swInterceptMenuBtn", interceptMenuBtn)}
                </div>
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">接管手柄菜单 / Guide 键 (0x0400)</span>
                    <span class="cw-row-sub">兼容原厂 GPDTool 双通道，单独短按呼出侧栏（不拦截 Steam 组合键与长按）</span>
                  </div>
                  ${renderSwitch("swInterceptGuideBtn", interceptGuideBtn)}
                </div>
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">侧栏开启时屏蔽游戏后台手柄</span>
                    <span class="cw-row-sub">防《仁王 3》等后台串键；如《剑星》切回失灵请保持关闭</span>
                  </div>
                  ${renderSwitch("swMuteGameGamepad", !!settings.muteGameGamepadInOverlay)}
                </div>
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">接管背键 F14–F16 呼出 HaloDeck</span>
                    <span class="cw-row-sub">默认关闭以防 L4/R4 原生固件 F14/F15 误触 OSD/超分菜单</span>
                  </div>
                  ${renderSwitch("swBackButtonHotkeys", !!settings.enableBackButtonHotkeys)}
                </div>
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">实体菜单键信号抓包诊断</span>
                    <span class="cw-row-sub">5 秒实时抓取 GPD 0xFF00 HID / RawInput / XInputEx / 钩子原始字节，支持一键复制排查</span>
                  </div>
                  <button id="btnMenuKeyDiag" class="btn btn-sm ${state.menuKeyDiagRunning ? "btn-primary" : ""}" data-focusable="true" ${state.menuKeyDiagRunning ? "disabled" : ""}>
                    ${state.menuKeyDiagRunning ? `抓包中 (${state.menuKeyDiagSecLeft || 5}s)... 请按菜单键` : "开始 5 秒抓包"}
                  </button>
                </div>
              </div>

              ${(() => {
                const rep = state.menuKeyDiagReport;
                if (!rep && !state.menuKeyDiagRunning) return "";
                if (state.menuKeyDiagRunning && !rep) {
                  return `
                    <div class="diag-capture-panel" id="menuKeyDiagPanel">
                      <div class="diag-capture-head">
                        <span class="diag-capture-title">正在监听实体按键原始信号（剩余 ${state.menuKeyDiagSecLeft || 5}s）...</span>
                      </div>
                      <div class="diag-empty-hint">请在 5 秒内短按一次机身快捷菜单键（或 L4/R4），抓包期间已自动暂停侧栏弹出以免遮挡。</div>
                    </div>
                  `;
                }
                const env = rep.environment || {};
                const evs = rep.events || [];
                return `
                  <div class="diag-capture-panel" id="menuKeyDiagPanel">
                    <div class="diag-capture-head">
                      <span class="diag-capture-title">抓包诊断结果 · 捕获 ${evs.length} 条事件 (${rep.durationMs || 5000} ms)</span>
                      <div style="display:flex;gap:6px;">
                        <button id="btnCopyMenuKeyDiag" class="btn btn-sm btn-primary" data-focusable="true">复制诊断报告</button>
                        <button id="btnClearMenuKeyDiag" class="btn btn-sm" data-focusable="true">收起</button>
                      </div>
                    </div>
                    <div class="diag-capture-chips">
                      <span class="diag-chip ${env.gpdVendorHidConnected ? "ok" : "warn"}">0xFF00 HID: ${env.gpdVendorHidConnected ? esc(env.gpdVendorHidDevice || "已连接") : "未连接"}</span>
                      <span class="diag-chip ${env.registerHotKeyF13Ok ? "ok" : "warn"}">HotKey(F13): ${env.registerHotKeyF13Ok ? "就绪" : `占用(${env.registerHotKeyF13Error || 0})`}</span>
                      <span class="diag-chip">XInput: ${esc(env.xInputBackend || "unknown")} (${env.connectedXInputCount ?? 0})</span>
                      <span class="diag-chip ${env.gpdToolRunning ? "warn" : "ok"}">GPDTool: ${env.gpdToolRunning ? (env.gpdToolElevated ? "运行中(管理员)" : "运行中") : "未运行"}</span>
                      <span class="diag-chip">${env.selfElevated ? "HaloDeck: 管理员" : "HaloDeck: 普通权限"}</span>
                    </div>
                    <div class="diag-capture-log">
                      ${evs.length === 0
                        ? `<div class="diag-empty-hint">5 秒内未检测到任何信号；请确认按键时未处于长按/锁键盘状态，并将以上状态与「复制诊断报告」发给开发者排查。</div>`
                        : evs.slice(0, 60).map((ev) => `
                            <div class="diag-log-row">
                              <span class="diag-log-offset">+${ev.offsetMs}ms</span>
                              <span class="diag-log-chan">${esc(ev.channel)}</span>
                              <span>${esc(ev.summary)}${ev.rawHex ? ` · RAW=${esc(ev.rawHex)}` : ""}</span>
                              ${ev.wouldTriggerQam ? `<span class="diag-log-trigger">可触发侧栏</span>` : ""}
                            </div>
                          `).join("")}
                    </div>
                  </div>
                `;
              })()}

              <div class="hotkey-list" style="margin-top:8px;">
                <div class="hotkey-row">
                  <div class="cw-row-title">呼出前端主界面</div>
                  <div class="comp-actions">
                    ${renderKeycaps(consoleButtons)}
                    <button id="btnRecordConsoleCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "console" ? "请按键..." : "录制"}</button>
                    <button id="btnClearConsoleCombo" class="btn btn-sm" data-focusable="true" ${consoleButtons.length === 0 ? "disabled" : ""}>取消</button>
                  </div>
                </div>
                <div class="hotkey-row">
                  <div class="cw-row-title">呼出快捷侧栏</div>
                  <div class="comp-actions">
                    ${renderKeycaps(qamButtons)}
                    <button id="btnRecordQamCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "qam" ? "请按键..." : "录制"}</button>
                    <button id="btnClearQamCombo" class="btn btn-sm" data-focusable="true" ${qamButtons.length === 0 ? "disabled" : ""}>取消</button>
                  </div>
                </div>
                <div class="hotkey-row">
                  <div class="cw-row-title">切换帧率悬浮窗</div>
                  <div class="comp-actions">
                    ${renderKeycaps(osdButtons)}
                    <button id="btnRecordOsdCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "osd" ? "请按键..." : "录制"}</button>
                    <button id="btnClearOsdCombo" class="btn btn-sm" data-focusable="true" ${osdButtons.length === 0 ? "disabled" : ""}>取消</button>
                  </div>
                </div>
                <div class="hotkey-row">
                  <div class="cw-row-title">呼出超分菜单</div>
                  <div class="comp-actions">
                    ${renderKeycaps(insButtons)}
                    <button id="btnRecordInsCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "insert" ? "请按键..." : "录制"}</button>
                    <button id="btnClearInsCombo" class="btn btn-sm" data-focusable="true" ${insButtons.length === 0 ? "disabled" : ""}>取消</button>
                  </div>
                </div>
              </div>

              <button id="btnToggleControlsAdv" class="btn btn-sm" style="width:100%;margin-top:8px;" data-focusable="true">
                ${state.controlsAdvancedOpen ? "收起摇杆死区与键盘热键 ▴" : "展开摇杆死区与键盘热键 ▾"}
              </button>

              ${state.controlsAdvancedOpen ? `
                <div class="cw-group" style="margin-top:8px;">
                  <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                    <span class="cw-row-title">左 / 右摇杆中心死区 (-10..10)</span>
                    <div style="display:flex;align-items:center;gap:5px;">
                      <button id="btnGpdLdzDec" class="step-btn" data-focusable="true">L−</button>
                      <span style="font-size:11.5px;min-width:30px;text-align:center;">${draft.leftDeadzone}</span>
                      <button id="btnGpdLdzInc" class="step-btn" data-focusable="true">L+</button>
                      <button id="btnGpdRdzDec" class="step-btn" data-focusable="true">R−</button>
                      <span style="font-size:11.5px;min-width:30px;text-align:center;">${draft.rightDeadzone}</span>
                      <button id="btnGpdRdzInc" class="step-btn" data-focusable="true">R+</button>
                    </div>
                  </div>
                  <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                    <span class="cw-row-title">键盘全局热键</span>
                    <div style="display:flex;gap:6px;flex-wrap:wrap;">
                      <button id="btnHotkeyConsole" class="btn btn-sm" data-focusable="true">主界面: ${esc(settings.consoleHotkey?.displayText || "Ctrl+Shift+O")}</button>
                      <button id="btnHotkeyQam" class="btn btn-sm" data-focusable="true">侧栏: ${esc(settings.qamHotkey?.displayText || "Ctrl+Alt+O")}</button>
                      <button id="btnHotkeyOsd" class="btn btn-sm" data-focusable="true">OSD: ${esc(settings.osdHotkey?.displayText || "Ctrl+Alt+M")}</button>
                      <button id="btnHotkeyIns" class="btn btn-sm" data-focusable="true">菜单: ${esc(settings.insertHotkey?.displayText || "Ctrl+Alt+I")}</button>
                    </div>
                  </div>
                </div>
              ` : ""}
            </div>
          </div>

          <!-- Right: Appearance & Layout Studio + System / Power Preferences -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">界面外观与布局风格</span>
                <span class="hh-card-badge">主控与侧栏实时同步</span>
              </div>

              <div class="style-studio-label">1. 选择功能布局形态</div>
              <div class="layout-choice-grid">
                ${UI_LAYOUTS.map((l) => `
                  <button class="layout-choice-card ${curLayout === l.id ? "active" : ""}" data-ui-layout="${l.id}" data-focusable="true">
                    <div class="layout-choice-head">
                      <strong>${esc(l.label)}</strong>
                      <span class="hh-card-badge ${curLayout === l.id ? "live" : ""}">${curLayout === l.id ? "当前使用" : "点击切换"}</span>
                    </div>
                    <span class="layout-choice-desc">${esc(l.desc)}</span>
                  </button>
                `).join("")}
              </div>

              <div class="style-studio-label" style="margin-top:10px;">2. 选择主题配色</div>
              <div class="theme-choice-grid">
                ${UI_THEMES.map((t) => `
                  <button class="theme-choice-card skin-swatch-btn ${curTheme === t.id ? "active" : ""}" data-ui-theme="${t.id}" data-focusable="true">
                    <span class="skin-dot ${t.id}"></span>
                    <div class="theme-choice-copy">
                      <strong>${esc(t.label)}</strong>
                      <span>${esc(t.desc)}</span>
                    </div>
                  </button>
                `).join("")}
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">全局系统与电源偏好</span>
                <span class="hh-card-badge ${hw.serviceRunning ? "live" : ""}">${hw.serviceRunning ? `PawnIO v${hw.daemonVersion || 4}` : "用户态"}</span>
              </div>

              <div class="cw-group">
                <div class="cw-row" style="min-height:42px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">插拔电自动切换 TDP</span>
                    <span class="cw-row-sub">离电 ${settings.batteryTdpWatts || 25}W · 插电 ${settings.acTdpWatts || 55}W</span>
                  </div>
                  <div style="display:flex;align-items:center;gap:5px;">
                    <button id="btnBatTdpDec" class="step-btn" data-focusable="true" title="离电 -1W">电−</button>
                    <button id="btnBatTdpInc" class="step-btn" data-focusable="true" title="离电 +1W">电+</button>
                    <button id="btnAcTdpDec" class="step-btn" data-focusable="true" title="插电 -1W">AC−</button>
                    <button id="btnAcTdpInc" class="step-btn" data-focusable="true" title="插电 +1W">AC+</button>
                    ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
                  </div>
                </div>

                <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                  <span class="cw-row-title">快捷瓦数档位收藏 (当前 ${curTdp}W)</span>
                  <div style="display:flex;gap:6px;">
                    ${!hasCurrentPreset
                      ? `<button id="btnAddTdpPreset" class="pill-btn small active" data-focusable="true">+ 收藏 ${curTdp}W 档</button>`
                      : (customPresets.length > 1 ? `<button id="btnDelTdpPreset" class="pill-btn small" data-focusable="true">− 移除 ${curTdp}W 档</button>` : `<span class="cw-row-sub">已收藏</span>`)}
                  </div>
                </div>

                <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                  <span class="cw-row-title">开机自动启动（最小化驻留托盘）</span>
                  ${renderSwitch("setStartup", !!settings.launchAtWindowsStartup)}
                </div>

                <div class="cw-row" style="min-height:44px;padding:8px 12px;">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">管理员提权穿透（按需启用） <span class="hh-card-badge ${snap.isElevated ? "live" : ""}" style="margin-left:4px;">${snap.isElevated ? "当前管理员" : "普通权限"}</span></span>
                    <span class="cw-row-sub">默认普通权限打开以防 Steam 大屏幕串线；仅游玩《终末地》《异环》等提权游戏时点右侧「临时提权重启」</span>
                  </div>
                  <div style="display:flex;align-items:center;gap:6px;">
                    ${!snap.isElevated ? `<button id="btnRelaunchAdmin" class="btn btn-sm btn-primary" data-focusable="true">临时提权重启</button>` : ""}
                    ${renderSwitch("swRunAsAdmin", !!settings.runAsAdministrator)}
                  </div>
                </div>

                <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                  <span class="cw-row-title">启动时默认全屏掌机模式</span>
                  ${renderSwitch("setFullscreenBoot", !!settings.launchConsoleFullscreen)}
                </div>

                <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                  <span class="cw-row-title">后台自动注入 OptiScaler</span>
                  <div style="display:flex;align-items:center;gap:6px;">
                    <button id="btnRunAutoInject" class="btn btn-sm" data-focusable="true">立即扫描</button>
                    ${renderSwitch("setAutoInject", !!settings.backgroundAutoInjectEnabled)}
                  </div>
                </div>

                ${(() => {
                  const curVer = snap.appVersion || "0.6.2";
                  const upd = snap.latestUpdate;
                  const hasUpd = !!(upd && upd.hasUpdate);
                  const prog = state.appUpdateProgress;
                  const badgeHtml = hasUpd
                    ? `<span class="hh-card-badge live" style="margin-left:4px;">可更新至 v${esc(upd.latestVersion)}${upd.isPrerelease ? " RC" : ""}</span>`
                    : (upd ? `<span class="hh-card-badge" style="margin-left:4px;">已是最新</span>` : "");
                  return `
                    <div class="cw-row" style="min-height:46px;padding:8px 12px;">
                      <div class="cw-row-copy">
                        <span class="cw-row-title">HaloDeck 版本与在线更新 <span class="hh-card-badge" style="margin-left:4px;">v${esc(curVer)}</span>${badgeHtml}</span>
                        <span class="cw-row-sub">${hasUpd ? `发现新版 v${esc(upd.latestVersion)}（下载自动校验 SHA-256，保留全部本地配置与游戏档案）` : "支持从 GitHub Releases 检查正式版或测试版并一键原地更新"}</span>
                      </div>
                      <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;justify-content:flex-end;">
                        <button id="btnTogglePrereleaseCheck" class="pill-btn small ${state.appUpdateIncludePrerelease ? "active" : ""}" data-focusable="true" title="开启后也会检查 -rc / -beta 预发布测试版">${state.appUpdateIncludePrerelease ? "含测试版" : "仅正式版"}</button>
                        <button id="btnCheckAppUpdate" class="btn btn-sm" data-focusable="true" ${state.appUpdateChecking ? "disabled" : ""}>${state.appUpdateChecking ? "检查中..." : "检查更新"}</button>
                        ${hasUpd && upd.downloadUrl ? `<button id="btnApplyAppUpdate" class="btn btn-sm btn-primary" data-focusable="true" ${prog ? "disabled" : ""}>${prog ? (prog.status === "applying" ? "正在更新..." : `下载中 ${Math.round(prog.percent || 0)}%`) : `更新 v${esc(upd.latestVersion)}`}</button>` : ""}
                        ${hasUpd && upd.htmlUrl ? `<button id="btnOpenUpdateNotes" class="btn btn-sm" data-focusable="true">更新日志</button>` : ""}
                      </div>
                    </div>
                  `;
                })()}

                <div class="cw-row" style="min-height:40px;padding:8px 12px;">
                  <span class="cw-row-title">系统服务与配置文件</span>
                  <div style="display:flex;gap:6px;flex-wrap:wrap;">
                    <button id="btnSysEnableVrr" class="pill-btn small ${snap.vrrWindowedEnabled ? "active" : ""}" data-focusable="true">${snap.vrrWindowedEnabled ? "窗口化VRR已开" : "开启窗口化VRR"}</button>
                    ${hw.gpdServiceExists ? `<button id="btnToggleGpdSvc" class="pill-btn small" data-focusable="true">${hw.gpdServiceRunning ? "暂停GPDTools" : "恢复GPDTools"}</button>` : ""}
                    <button id="btnOpenSettingsJson" class="pill-btn small" data-focusable="true">编辑 JSON</button>
                    <button id="btnReloadSettingsJson" class="pill-btn small" data-focusable="true">重载</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `;
  }

  // ---------------- Console View 2: Unified Game Hardware Profile + OptiScaler Suite ----------------
  function renderTuneView() {
    const snap = state.snapshot;
    const settings = snap?.settings || {};
    const games = snap?.games || [];
    const active = getActiveGame();
    const presets = snap?.presets || [];
    const kernel = getActiveKernelInfo();
    const cfg = state.config;

    const curTdp = settings.currentTdpWatts || 40;
    const perGameProfiles = settings.perGameProfiles || {};
    const gamePerf = active ? (perGameProfiles[active.id] || {
      gameId: active.id,
      gameName: active.title,
      enabled: false,
      tdpWatts: curTdp,
      refreshRateHz: 0,
      cpuMaxFreqMhz: 0
    }) : null;

    const inj = active?.injection;
    const isUninjectedPreview = !inj?.isInjected && !state._userModifiedConfig;
    const injBadge = inj?.isInjected
      ? `<span class="pill-badge blue">已注入 · ${esc(formatConfigSummary(inj.appliedConfig || cfg, true))}</span>`
      : `<span class="pill-badge">原生未注入</span>`;

    const hasCustomAdvanced = cfg.fgInputMode !== "auto" || cfg.proxyDllName !== "auto" || cfg.fsr4Watermark;
    const kernelShort = kernel.slot === "OptiScalerNightly" ? "Nightly v10+" : "Stable v0.9.4";
    const mfgUnlockerInstalled = isMfgUnlockerInstalled();
    const xeFgMult = Math.min(4, Math.max(2, Number(cfg.xeFgMultiplier) || 2));
    const xeFgDmfg = !!cfg.xeFgDynamicMfg;
    const xeFgDmfgTarget = (Number(cfg.xeFgDynamicTargetFps) || 0) >= 10 ? Number(cfg.xeFgDynamicTargetFps) : 0;
    const needsMfgUnlocker = cfg.fgType === "xefg" && (xeFgMult > 2 || xeFgDmfg);
    const showXeFgStableHint = needsMfgUnlocker && kernel.slot === "OptiScalerStable";
    const showMissingMfgUnlockerHint = needsMfgUnlocker && !mfgUnlockerInstalled;
    const canLiveToggleFg = !!inj?.isInjected && ((inj.appliedConfig?.fgType || cfg.fgType) !== "nofg");

    return `
      <div class="tune-layout">
        <div class="tune-left">
          <div class="hero-card">
            <div class="hero-banner" style="height:100px;">
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

            <div class="hero-body" style="padding:8px 12px 10px;gap:7px;">
              <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;">
                <div class="cap-strip">${active ? renderCapabilities(active) : ""}</div>
                <div style="display:flex;gap:5px;flex-shrink:0;">
                  <button id="btnOpenConsoleGamePicker" class="btn btn-sm btn-primary" data-focusable="true">切换游戏 (${games.length}) ▸</button>
                  <button id="btnAddExeInline" class="btn btn-sm" data-focusable="true">+ EXE</button>
                </div>
              </div>
            </div>
          </div>

          <!-- Unified Per-Game Hardware Performance Profile Card -->
          ${active && gamePerf ? `
            <div class="hh-card" style="padding:9px 11px;display:flex;flex-direction:column;gap:6px;">
              <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
                <span class="hh-card-title">当前游戏专属硬件配置</span>
                <div style="display:flex;align-items:center;gap:5px;flex-shrink:0;">
                  <button class="pill-btn small" style="white-space:nowrap;" data-quick-game-scene="vn" data-focusable="true">10W 省电</button>
                  <button class="pill-btn small" style="white-space:nowrap;" data-quick-game-scene="act" data-focusable="true">25W 动作</button>
                  ${renderSwitch("swGamePerfEnabled", !!gamePerf.enabled)}
                </div>
              </div>

              <div class="game-perf-tune-stack">
                <div class="game-perf-cell">
                  <span>功耗与刷新率</span>
                  <div style="display:flex;align-items:center;gap:5px;flex-shrink:0;">
                    <div class="bento-inline-stepper">
                      <button id="btnGamePerfTdpDec" class="step-btn" data-focusable="true">−</button>
                      <span>${gamePerf.tdpWatts || curTdp}W</span>
                      <button id="btnGamePerfTdpInc" class="step-btn" data-focusable="true">+</button>
                    </div>
                    ${renderSegmented("data-game-perf-hz", [
                      { value: "0", label: "跟随全局" },
                      { value: "60", label: "60Hz" },
                      { value: "120", label: "120Hz" },
                      { value: "-1", label: "动态" }
                    ], String(gamePerf.refreshRateHz || 0), "compact")}
                  </div>
                </div>
              </div>
            </div>
          ` : ""}

          <div class="action-dock" style="padding:10px 12px;gap:7px;">
            <button id="btnInject" class="btn btn-primary" data-focusable="true" ${!active || state.busy ? "disabled" : ""}>
              ${svgIcon("bolt")}
              <span>${inj?.isInjected ? "应用超分设置并重新注入" : "一键注入推荐超分"}</span>
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
                <div class="mem-alert-title">检测到虚拟内存偏小（提交上限 ${esc(state.snapshot.memoryHealth.commitLimitGb)} GB）</div>
                <div class="mem-alert-sub">大型 DX12 游戏易报显存不足，建议一键扩容分页文件。</div>
              </div>
              <button class="mem-alert-btn" id="btnExpandPagefile" data-focusable="true">扩容至 24–40GB</button>
            </div>` : ""}
          <div>
            <div class="section-label">
              <span>超分预设方案 (${esc(kernelShort)})</span>
              <span>点击自动填入</span>
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
              <span>OptiScaler 超分辨率与插帧参数</span>
              <span>${isUninjectedPreview ? "待注入预览" : `已生效：${esc(formatConfigSummary(inj?.appliedConfig || cfg))}`}</span>
            </div>
            <div class="cw-group ${isUninjectedPreview ? "uninjected-preview" : ""}">
              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">超分辨率模型 (SR)</span>
                  <span class="cw-row-sub">8060S 推荐 FSR 4.1.2；追求更高帧可选 Lite</span>
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
                  <span class="cw-row-sub">支持 DLSS-G 选 XeFG；无原生插帧选 OptiFG</span>
                </div>
                ${renderSegmented("data-fg-type", [
                  { value: "xefg", label: "XeFG 帧生成" },
                  { value: "optifg", label: "OptiFG" },
                  { value: "nofg", label: "关闭插帧" }
                ], cfg.fgType)}
              </div>

              ${cfg.fgType === "xefg" ? `
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">XeFG 插帧倍率 (MFG)</span>
                    <span class="cw-row-sub">2X 甜点 · 3X 推荐多帧 · 4X 算力开销更高（改固定倍率需「注入并重开」）</span>
                  </div>
                  ${renderSegmented("data-xefg-mult", [
                    { value: "2", label: "2X" },
                    { value: "3", label: "3X" },
                    { value: "4", label: "4X" }
                  ], String(xeFgMult), "compact")}
                </div>

                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">动态多帧 (DMFG)</span>
                    <span class="cw-row-sub">游戏内按目标帧率在 1X–${xeFgMult}X 间自动实时升降</span>
                  </div>
                  ${renderSwitch("swXeFgDmfg", xeFgDmfg)}
                </div>

                ${xeFgDmfg ? `
                  <div class="cw-row">
                    <div class="cw-row-copy">
                      <span class="cw-row-title">DMFG 目标帧率</span>
                      <span class="cw-row-sub">${xeFgDmfgTarget > 0 && ![60, 90, 120].includes(xeFgDmfgTarget) ? `当前自定义 ${xeFgDmfgTarget} FPS · ` : ""}自动贴合屏幕刷新率或帧率上限（如 120Hz → 116 FPS）</span>
                    </div>
                    ${renderSegmented("data-xefg-dmfg-fps", [
                      { value: "0", label: "自动贴合" },
                      { value: "60", label: "60" },
                      { value: "90", label: "90" },
                      { value: "120", label: "120" }
                    ], String(xeFgDmfgTarget), "compact")}
                  </div>
                ` : ""}

                ${showMissingMfgUnlockerHint ? `
                  <div class="xefg-mfg-hint xefg-mfg-hint-row">
                    <span>AMD 8060S 原版 <code>libxess_fg.dll</code> 最高仅支持 2X，解锁 3X/4X 与 DMFG 需安装 <strong>MFG Unlocker (by dashdogy)</strong>（单机专用）。</span>
                    <button id="btnGoMfgUnlockerComp" class="btn btn-sm btn-primary" data-focusable="true">前往组件中心下载</button>
                  </div>
                ` : ""}

                ${showXeFgStableHint ? `
                  <div class="xefg-mfg-hint">
                    当前内核为 Stable v0.9.4，3X/4X 多帧与 DMFG 需切换至 <strong>Nightly v10+</strong> 内核与 XeSS 3 (<code>libxess_fg.dll</code>)
                  </div>
                ` : ""}
              ` : ""}

              ${canLiveToggleFg ? `
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">游戏内实时插帧开关 (免菜单 / 免重启)</span>
                    <span class="cw-row-sub">直接触发 OptiScaler 原生热键 (End / FGShortcutKey) 实时开关 FG</span>
                  </div>
                  <button id="btnToggleLiveFg" class="btn btn-sm xefg-live-fg-btn" data-focusable="true">实时开关 FG</button>
                </div>
              ` : ""}

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">平滑帧率上限 (Reflex / XeLL)</span>
                  <span class="cw-row-sub">开 VRR 推荐 90/116；文字/60Hz 推荐 60 帧</span>
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
                  <span class="cw-row-title">AMD Anti-Lag 2 低延迟映射</span>
                </div>
                ${renderSwitch("swFakenvapi", !!cfg.enableFakenvapi)}
              </div>

              <div class="cw-row">
                <div class="cw-row-copy">
                  <span class="cw-row-title">防撕裂同步 (V-Sync / VRR)</span>
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
              <span>底层兼容参数 ${hasCustomAdvanced ? "(已自定义)" : ""}</span>
              <span style="display:inline-flex;align-items:center;gap:8px;">
                <span class="cw-disclosure-summary">
                  FGInput: ${esc(cfg.fgInputMode)} · Proxy: ${esc(cfg.proxyDllName)}
                </span>
                ${svgIcon("chevron")}
              </span>
            </button>
            ${state.advancedOpen ? `
              <div class="cw-disclosure-body">
                <div class="cw-row">
                  <span class="cw-row-title">插帧运动源 (FGInput)</span>
                  ${renderSegmented("data-fg-input", [
                    { value: "auto", label: "智能自动" },
                    { value: "dlssg", label: "DLSS-G" },
                    { value: "upscaler", label: "通用光流" }
                  ], cfg.fgInputMode, "compact")}
                </div>
                <div class="cw-row">
                  <span class="cw-row-title">DLL 注入代理名</span>
                  ${renderSegmented("data-proxy-dll", [
                    { value: "auto", label: "auto" },
                    { value: "dxgi.dll", label: "dxgi.dll" },
                    { value: "version.dll", label: "version.dll" },
                    { value: "winmm.dll", label: "winmm.dll" }
                  ], cfg.proxyDllName, "compact")}
                </div>
                <div class="cw-row">
                  <span class="cw-row-title">FSR 4.1.2 验证水印</span>
                  ${renderSwitch("swWatermark", !!cfg.fsr4Watermark)}
                </div>
                <div class="cw-row">
                  <span class="cw-row-title">OptiPatcher 运行时解锁</span>
                  ${renderSwitch("swOptiPatcher", !!cfg.enableOptiPatcher)}
                </div>
                <div class="cw-row">
                  <span class="cw-row-title">DXGI 显卡身份伪装 (10GB)</span>
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
            <button id="btnLibBackToTune" class="btn btn-sm" data-focusable="true">‹ 返回当前调优</button>
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
            ${renderSegmented("data-lib-layout", [
              { value: "posters", label: "海报墙" },
              { value: "list", label: "列表" }
            ], state.libraryLayout, "compact")}
            <button id="btnScanGames" class="btn btn-sm" data-focusable="true">重新扫描目录</button>
            <button id="btnAddExeLib" class="btn btn-sm btn-primary" data-focusable="true">+ 添加本地 EXE</button>
          </div>
        </div>

        ${state.libraryLayout === "list" ? `
          <div class="lib-list-grid">
            ${filtered.length === 0 ? `
              <div class="cw-group" style="padding:28px;text-align:center;color:var(--text-secondary);">
                未找到匹配的游戏。点击右上角「重新扫描目录」或手动添加游戏主程序 (.exe)。
              </div>
            ` : filtered.map((g) => {
              const caps = g.nativeUpscalers || {};
              const capTag = caps.hasDlssG ? "DLSS-G" : caps.hasDlss ? "DLSS" : caps.hasXeSs ? "XeSS" : "常规";
              const isSel = active && active.id === g.id;
              return `
                <button class="lib-list-row ${isSel ? "active" : ""}" data-open-game="${esc(g.id)}" data-focusable="true">
                  <div class="lib-list-main">
                    <strong>${esc(g.title)}</strong>
                    <span class="lib-list-path">${esc(g.platform)} · ${esc(g.engine)} · ${esc(g.targetRenderDirectory)}</span>
                  </div>
                  <div class="lib-list-badges">
                    <span class="pill-badge ${caps.hasDlss ? "blue" : ""}">${capTag}</span>
                    ${g.isCurrentlyRunning ? `<span class="pill-badge live">运行中</span>` : ""}
                    <span class="pill-badge ${g.injection?.isInjected ? "blue" : ""}">${g.injection?.isInjected ? "已注入" : "原生未注入"}</span>
                  </div>
                </button>
              `;
            }).join("")}
          </div>
        ` : `
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
        </div>`}
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
                    ${c.description ? `<span class="comp-desc">${esc(c.description)}</span>` : ""}
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
          <div class="sub-nav-bar" role="tablist" aria-label="游戏与超分子功能">
            <button class="sub-tab-btn ${state.optiSubTab === "tune" ? "active" : ""}" data-opti-sub="tune" data-focusable="true">游戏配置与超分</button>
            <button class="sub-tab-btn ${state.optiSubTab === "library" ? "active" : ""}" data-opti-sub="library" data-focusable="true">游戏海报库</button>
            <button class="sub-tab-btn ${state.optiSubTab === "components" ? "active" : ""}" data-opti-sub="components" data-focusable="true">核心组件</button>
          </div>
          <span style="font-size:11.5px;color:var(--text-muted);">专属功耗 · 刷新率 · FSR 4.1.2 · XeFG 一站式配置</span>
        </div>
        <div style="flex:1;min-height:0;display:flex;flex-direction:column;">
          ${subContent}
        </div>
      </div>
    `;
  }

  // ---------------- QAM Overlay Views (Strictly <= 440px total width, Tab 1 Zero-Scroll) ----------------
  function renderQamView() {
    const snap = state.snapshot || {};
    const sys = snap.systemControl || {};
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const frozen = snap.frozenGames || [];
    const active = getActiveGame();
    const games = snap.games || [];
    const daemonVer = hw.daemonVersion || 0;
    const vramUsedGb = ((sys.vramUsedMb || 0) / 1024).toFixed(1);
    const vramTotalGb = sys.vramTotalMb > 0 ? (sys.vramTotalMb / 1024).toFixed(0) : "16";
    const sharedGb = ((sys.sharedVramUsedMb || 0) / 1024).toFixed(1);

    const sunSvg = `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="4"></circle><path d="M12 2v2M12 20v2M2 12h2M20 12h2"></path></svg>`;
    const volSvg = `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path></svg>`;

    if (state.qamTab === "handheld") {
      const curTdp = settings.currentTdpWatts || 40;
      const customPresets = (settings.customTdpPresets && settings.customTdpPresets.length)
        ? settings.customTdpPresets
        : [10, 15, 20, 25, 30, 45, 60];
      const fanMode = normalizeFanModeUi(settings.fanMode);
      const manualFanPct = settings.manualFanPercent || 55;
      const boostMode = sys.cpuBoostMode || "off";
      const osdLevel = settings.overlayLevel ?? 0;
      const apuStr = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W` : "--W";
      const rpm = hw.fanRpm || 0;
      const hzOptions = buildRefreshOptions(sys);
      const activeRefreshVal = sys.isDynamicRefreshRate ? "drr" : String(sys.refreshRateHz || 120);
      const runningNow = getRunningGame();

      const perGameProfiles = settings.perGameProfiles || {};
      const gamePerf = runningNow ? (perGameProfiles[runningNow.id] || {
        gameId: runningNow.id,
        gameName: runningNow.title,
        enabled: false,
        tdpWatts: curTdp,
        refreshRateHz: 0,
        cpuMaxFreqMhz: 0
      }) : null;

      return `
        <div class="qam-body qam-zero-scroll">
          <div class="qam-live-ribbon">
            <span id="qamRibbonLeft" style="color:var(--text-primary);">${sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : ""}APU ${apuStr} / ${curTdp}W</span>
            <span id="qamRibbonRight" style="color:var(--text-secondary);">C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}° · ${rpm}R</span>
          </div>

          ${!hw.serviceRunning ? `
            <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" style="width:100%;" data-focusable="true">一键启用 TDP / 风扇直控</button>
          ` : !hw.smuReady ? `
            <div style="display:flex;gap:6px;width:100%;">
              <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" style="flex:1;" data-focusable="true">重试启动 PawnIO 驱动</button>
              <button id="btnOpenPawnIo" class="btn btn-sm" data-focusable="true">下载驱动</button>
            </div>
          ` : ""}

          <!-- 1. TDP Power Slider -->
          <div class="qam-section compact-pad">
            <div class="qam-section-title">
              <span>TDP 功耗设定</span>
              <strong id="qamTdpReadout">${curTdp}W · ${esc(resolveTdpSceneLabel(curTdp))}</strong>
            </div>
            <div class="qam-preset-row">
              ${customPresets.slice(0, 6).map((w) => `
                <button class="pill-btn qam-pill ${curTdp === w ? "active" : ""}" data-tdp-watt="${w}" data-focusable="true">${w}W</button>
              `).join("")}
            </div>
            <div style="display:flex;align-items:center;gap:6px;">
              <button id="btnTdpDec" class="step-btn qam-step" data-focusable="true">−1W</button>
              <input id="tdpRangeInput" class="hh-range" type="range" min="8" max="120" step="1" value="${curTdp}" style="flex:1;" />
              <button id="btnTdpInc" class="step-btn qam-step" data-focusable="true">+1W</button>
            </div>
          </div>

          <!-- 2. Active Running Game Instant Binding -->
          ${runningNow && gamePerf ? `
            <div class="qam-section compact-pad qam-game-bind-card">
              <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;margin-bottom:4px;">
                <strong style="font-size:11.5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">《${esc(runningNow.title)}》专属配置</strong>
                ${renderSwitch("swGamePerfEnabled", !!gamePerf.enabled)}
              </div>
              <div style="display:flex;align-items:center;justify-content:space-between;gap:5px;">
                <div class="bento-inline-stepper">
                  <button id="btnGamePerfTdpDec" class="step-btn" data-focusable="true">−</button>
                  <span>${gamePerf.tdpWatts || curTdp}W</span>
                  <button id="btnGamePerfTdpInc" class="step-btn" data-focusable="true">+</button>
                </div>
                ${renderSegmented("data-game-perf-hz", [
                  { value: "0", label: "跟随全局" },
                  { value: "60", label: "60Hz" },
                  { value: "-1", label: "动态" }
                ], String(gamePerf.refreshRateHz || 0), "compact")}
              </div>
            </div>
          ` : ""}

          <!-- 3. Refresh Rate + Brightness & Volume Sliders -->
          <div class="qam-section compact-pad">
            <div class="qam-section-title">
              <span>显示与音量</span>
              <span id="qamDispSummary" style="font-size:10.5px;color:var(--text-secondary);">${esc(formatRefreshTag(sys))}</span>
            </div>
            <div style="display:flex;flex-direction:column;gap:5px;">
              ${renderSegmented("data-refresh-hz", hzOptions, activeRefreshVal, "compact full")}
              ${renderStepSlider({ idPrefix: "bright", value: sys.brightnessPercent ?? 70, min: 5, displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "外接屏", disabled: !sys.brightnessSupported, iconHtml: sunSvg })}
              ${renderStepSlider({ idPrefix: "vol", value: sys.volumePercent ?? 50, min: 0, muted: !!sys.isMuted, muteBtnId: "btnToggleMute", iconHtml: volSvg })}
            </div>
          </div>

          <!-- 4. Clean 3-Row Labeled Segmented Controls: OSD + CPU Boost + Fan -->
          <div class="qam-section compact-pad qam-modes-list">
            <div class="qam-labeled-row">
              <span class="qam-row-label">性能悬浮窗</span>
              <div class="seg-group compact">
                ${[
                  { v: 0, label: "关闭" },
                  { v: 1, label: "极简" },
                  { v: 2, label: "紧凑" },
                  { v: 3, label: "详细" },
                  { v: 4, label: "图表" }
                ].map((o) => `
                  <button class="seg-btn ${osdLevel === o.v ? "active" : ""}" data-osd-level="${o.v}" data-focusable="true">${o.label}</button>
                `).join("")}
              </div>
            </div>

            <div class="qam-labeled-row">
              <span class="qam-row-label">CPU 睿频</span>
              ${renderSegmented("data-cpu-boost", [
                { value: "off", label: "关睿频" },
                { value: "efficient", label: "平衡" },
                { value: "aggressive", label: "满血" }
              ], boostMode, "compact")}
            </div>

            <div class="qam-labeled-row">
              <span class="qam-row-label">风扇控制</span>
              <div class="seg-group compact">
                ${[
                  { value: "auto", label: "自动静音" },
                  { value: "curve", label: "温控曲线" },
                  { value: "manual", label: "手动定速" }
                ].map((opt) => `
                  <button class="seg-btn ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
                `).join("")}
              </div>
            </div>
            ${fanMode === "manual" ? `
              <div style="display:flex;align-items:center;gap:6px;margin-top:4px;">
                <button id="btnFanPctDec" class="step-btn qam-step" data-focusable="true">−5%</button>
                <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" style="flex:1;" />
                <span id="fanManualVal" style="font-size:12px;font-weight:700;min-width:34px;text-align:right;">${manualFanPct}%</span>
                <button id="btnFanPctInc" class="step-btn qam-step" data-focusable="true">+5%</button>
              </div>
            ` : ""}
          </div>

          <!-- 5. Full 2x4 Handheld Quick Actions (Game Fridge Front & Center!) -->
          <div class="qam-quick-grid-8">
            <button class="toolbox-tile qam-action-tile ${frozen.length > 0 ? "active" : ""}" data-toolbox="toggle_fridge" data-focusable="true">${svgIcon("fridge")}<span>${frozen.length > 0 ? `解冻(${frozen.length})` : "游戏冰箱"}</span></button>
            <button class="toolbox-tile qam-action-tile" data-toolbox="osk" data-focusable="true">${svgIcon("osk")}<span>屏幕键盘</span></button>
            <button class="toolbox-tile qam-action-tile" data-toolbox="screenshot" data-focusable="true">${svgIcon("shot")}<span>区域截屏</span></button>
            <button id="btnToolboxMouse" class="toolbox-tile qam-action-tile ${settings.gamepadMouseModeEnabled ? "active" : ""}" data-focusable="true">${svgIcon("mouse")}<span>手柄鼠标</span></button>
            <button class="toolbox-tile qam-action-tile" data-toolbox="task_view" data-focusable="true">${svgIcon("switchwin")}<span>切换窗口</span></button>
            <button class="toolbox-tile qam-action-tile" data-toolbox="show_desktop" data-focusable="true">${svgIcon("desktop")}<span>返回桌面</span></button>
            <button id="btnToggleOfflineWifi" class="toolbox-tile qam-action-tile ${sys.wifiRadioOff ? "active" : ""}" data-focusable="true" ${sys.wifiRadioSupported === false ? "disabled" : ""}>${svgIcon("wifi")}<span>${sys.wifiRadioOff ? "开 Wi-Fi" : "关 Wi-Fi"}</span></button>
            <button id="btnKillGame" class="toolbox-tile qam-action-tile ${state.killArmed ? "armed" : ""}" data-focusable="true">${svgIcon("closegame")}<span>${state.killArmed ? "确认关闭" : "关闭游戏"}</span></button>
          </div>
        </div>
      `;
    }

    if (state.qamTab === "osd") {
      const level = settings.overlayLevel ?? 0;
      const pos = settings.overlayPosition || "top-center";
      const opacity = settings.overlayOpacity ?? 0.86;
      const osdRenderer = settings.overlayRenderer || "native";
      const rtssInstalled = !!sys.rtssInstalled;
      const rtssRunning = !!sys.rtssRunning;
      const manualFanPct = settings.manualFanPercent || 55;
      const fanMode = normalizeFanModeUi(settings.fanMode);
      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title"><span>实时传感器指标</span></div>
            <div class="qam-kpi-grid">
              <div class="qam-kpi-card">
                <span class="pf-label">当前帧率</span>
                <strong id="qamKpiFps" class="accent-blue">${sys.currentFps > 0 ? `${sys.currentFps} FPS` : "-- FPS"}</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">APU 功耗</span>
                <strong id="qamKpiApu">${sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W"}</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">8060S 频率</span>
                <strong id="qamKpiGpu">${sys.gpuUsagePercent || 0}% · ${sys.gfxClockMhz || 0}M</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">核心温度</span>
                <strong id="qamKpiTemp">C ${sys.cpuTempC || "--"}° / G ${sys.gpuTempC || "--"}°</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">显存占用</span>
                <strong id="qamKpiVram">${vramUsedGb}/${vramTotalGb}G (+${sharedGb}G)</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">内存 · 风扇</span>
                <strong id="qamKpiMem">${sys.usedRamGb || "--"}G · ${hw.fanRpm || 0}RPM</strong>
              </div>
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>CPU 睿频与定速风扇</span></div>
            <div style="display:flex;flex-direction:column;gap:7px;">
              ${renderSegmented("data-cpu-boost", [
                { value: "off", label: "关闭睿频" },
                { value: "efficient", label: "智能平衡" },
                { value: "aggressive", label: "积极满血" }
              ], sys.cpuBoostMode || "off", "compact full")}
              ${fanMode === "manual" ? `
                <div style="display:flex;align-items:center;gap:6px;">
                  <button id="btnFanPctDec" class="step-btn qam-step" data-focusable="true">−5%</button>
                  <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" style="flex:1;" />
                  <span id="fanManualVal" style="font-size:12px;font-weight:700;min-width:34px;text-align:right;">${manualFanPct}%</span>
                  <button id="btnFanPctInc" class="step-btn qam-step" data-focusable="true">+5%</button>
                </div>
              ` : ""}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>60s 功耗与负载曲线</span></div>
            <div id="qamOsdSparkline" style="margin-bottom:6px;">
              ${buildSvgChart(sys.apuWattsHistory || [], sys.gpuUsageHistory || [], 85, 100, "#2dd4bf", "#38bdf8", "APU W", "GPU %")}
            </div>
            <div id="qamOsdSparkline2">
              ${buildSvgChart(sys.cpuUsageHistory || [], sys.tempHistory || [], 100, 100, "#a78bfa", "#fbbf24", "CPU %", "Temp °C")}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>OSD 渲染通道与样式</span>
              <span style="font-size:10.5px;color:${rtssRunning ? "var(--accent-green,#4ade80)" : "var(--text-muted)"};">${rtssRunning ? "RTSS 已就绪" : (rtssInstalled ? "RTSS 未启动" : "未装 RTSS")}</span>
            </div>
            <div style="display:flex;flex-direction:column;gap:6px;">
              ${renderSegmented("data-osd-renderer", [
                { value: "native", label: "原生窗" },
                { value: "auto_rtss", label: "智能回退" },
                { value: "rtss", label: "RTSS穿透" }
              ], osdRenderer, "compact full")}
              ${!rtssRunning && osdRenderer !== "native" ? `
                <button id="btnEnsureRtss" class="btn btn-sm btn-primary" style="width:100%;" data-focusable="true">${rtssInstalled ? "立即启动本机 RTSS" : "一键安装并启动 RTSS"}</button>
              ` : ""}
              ${renderSegmented("data-osd-pos", [
                { value: "top-left", label: "左上" },
                { value: "top-center", label: "顶中" },
                { value: "top-right", label: "右上" }
              ], pos, "compact full")}
              ${renderSegmented("data-osd-opacity", [
                { value: "0.55", label: "55%" },
                { value: "0.75", label: "75%" },
                { value: "0.88", label: "88%" },
                { value: "1", label: "100%" }
              ], String(opacity >= 0.95 ? 1 : opacity >= 0.82 ? 0.88 : opacity >= 0.65 ? 0.75 : 0.55), "compact full")}
            </div>
          </div>
        </div>
      `;
    }

    if (state.qamTab === "plugins") {
      return renderQamPluginsView();
    }

    if (state.qamTab === "controls") {
      ensureGpdControllerLoaded();
      const gpd = snap.gpdController || {};
      const draft = state.gpdDraft;
      const interceptOsk = settings.interceptPhysicalOskButton !== false;
    const interceptMenuBtn = settings.interceptPhysicalMenuButton !== false;
      const dirtyGpd = hasPendingGpdChanges(gpd, draft);
      const curTheme = settings.uiTheme === "frost" ? "frost" : "aurora";
      const curLayout = settings.uiLayout === "cockpit" ? "cockpit" : "bento";

      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>手柄背键 (L4 / R4)</span>
              <span class="hh-card-badge ${gpd.connected ? "live" : ""}">${gpd.connected ? `FW X${gpd.firmwareX || "?"}${gpd.firmwareK ? ` / K${gpd.firmwareK}` : ""}` : "未检测手柄"}</span>
            </div>
            <div class="backkey-slot-list compact" style="margin-bottom:8px;">
              <button id="btnPickL4" class="backkey-slot-row btn-like" data-focusable="true">
                <strong>L4 左背键</strong>
                <span id="l4SlotBadge" class="pill-badge">${esc(resolveBackSlotLabel(draft.l4Key, draft.l4Combo))} ▸</span>
              </button>
              <button id="btnPickR4" class="backkey-slot-row btn-like" data-focusable="true">
                <strong>R4 右背键</strong>
                <span id="r4SlotBadge" class="pill-badge">${esc(resolveBackSlotLabel(draft.r4Key, draft.r4Combo))} ▸</span>
              </button>
            </div>
            <div style="display:flex;gap:6px;">
              <button id="btnSaveGpd" class="btn btn-sm btn-primary" style="flex:1;" data-focusable="true" ${!gpd.connected || state.gpdSaving ? "disabled" : ""}>${state.gpdSaving ? "写入中..." : (dirtyGpd ? "应用修改到手柄" : "应用到手柄")}</button>
              <button id="btnResetGpdFactory" class="btn btn-sm" data-focusable="true" ${!gpd.connected || state.gpdSaving ? "disabled" : ""} title="清空 L4/R4 恢复原厂">恢复原厂</button>
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>输入辅助与系统联动</span>
              ${renderSwitch("swQamMouseMode", !!settings.gamepadMouseModeEnabled)}
            </div>
            <button id="btnTestOsk" class="btn btn-sm btn-primary" style="width:100%;margin-bottom:7px;" data-focusable="true">呼出 / 收起屏幕键盘</button>
            <div class="cw-row qam-compact-row">
              <span class="cw-row-title">接管机身键盘键</span>
              ${renderSwitch("swInterceptOsk", interceptOsk)}
            </div>
            <div class="cw-row qam-compact-row">
              <span class="cw-row-title">插拔电自动切 (${settings.batteryTdpWatts || 25}W / ${settings.acTdpWatts || 55}W)</span>
              ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
            </div>
            ${!snap.isElevated ? `
            <div class="cw-row qam-compact-row">
              <span class="cw-row-title">提权游戏侧栏 / OSD 穿透（本次临时）</span>
              <button id="btnRelaunchAdmin" class="btn btn-sm btn-primary" data-focusable="true">临时提权</button>
            </div>` : ""}
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>布局风格与深浅配色</span></div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-bottom:6px;">
              ${UI_LAYOUTS.map((l) => `
                <button class="pill-btn qam-pill ${curLayout === l.id ? "active" : ""}" data-ui-layout="${l.id}" data-focusable="true">${esc(l.label)}</button>
              `).join("")}
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;">
              ${UI_THEMES.map((t) => `
                <button class="skin-swatch-btn ${curTheme === t.id ? "active" : ""}" data-ui-theme="${t.id}" data-focusable="true">
                  <span class="skin-dot ${t.id}"></span>
                  <span>${esc(t.label)}</span>
                </button>
              `).join("")}
            </div>
          </div>
        </div>
      `;
    }

    // QAM Tab 2: Unified Game Profile + OptiScaler Tuning <-> Game Picker Drill-Down
    if (state.qamOptiSubView === "games") {
      return `
        <div class="qam-body">
          <div class="qam-game-picker-view">
            <div class="qam-picker-head">
              <button id="btnQamBackToTune" class="btn btn-sm" data-focusable="true">‹ 返回调优</button>
              <strong style="font-size:12.5px;">选择目标 (${games.length})</strong>
              <button id="btnQamRescan" class="btn btn-sm" data-focusable="true">扫描</button>
            </div>
            <div class="qam-game-list">
              ${games.length === 0 ? `
                <div style="padding:18px;text-align:center;color:var(--text-secondary);font-size:12px;">
                  暂无游戏，请点击右上角「扫描」或在主控台添加本地 EXE。
                </div>
              ` : games.map((g) => {
                const isSel = active && active.id === g.id;
                const gInj = g.injection;
                const gSub = gInj?.isInjected
                  ? `已注入 · ${formatConfigSummary(gInj.appliedConfig || DEFAULT_CONFIG, true)}`
                  : `${g.platform} · ${g.engine} · 原生未注入`;
                return `
                  <button class="qam-game-row ${isSel ? "active" : ""}" data-select-game="${esc(g.id)}" data-focusable="true">
                    <div class="qam-game-row-top">
                      <span class="qam-game-row-title">${esc(g.title)}</span>
                      <div style="display:flex;gap:4px;flex-shrink:0;">
                        ${g.isCurrentlyRunning ? `<span class="hh-card-badge live">运行中</span>` : ""}
                        <span class="hh-card-badge ${gInj?.isInjected ? "live" : "warn"}">${gInj?.isInjected ? "已注入" : "未注入"}</span>
                      </div>
                    </div>
                    <span class="qam-game-row-sub">${esc(gSub)}</span>
                  </button>
                `;
              }).join("")}
            </div>
          </div>
        </div>
      `;
    }

    const inj = active?.injection;
    const cfg = state.config;
    const presets = snap.presets || [];
    const kernel = getActiveKernelInfo();
    const mfgUnlockerInstalled = isMfgUnlockerInstalled();
    const xeFgMult = Math.min(4, Math.max(2, Number(cfg.xeFgMultiplier) || 2));
    const xeFgDmfg = !!cfg.xeFgDynamicMfg;
    const xeFgDmfgTarget = (Number(cfg.xeFgDynamicTargetFps) || 0) >= 10 ? Number(cfg.xeFgDynamicTargetFps) : 0;
    const needsMfgUnlocker = cfg.fgType === "xefg" && (xeFgMult > 2 || xeFgDmfg);
    const showXeFgStableHint = needsMfgUnlocker && kernel.slot === "OptiScalerStable";
    const showMissingMfgUnlockerHint = needsMfgUnlocker && !mfgUnlockerInstalled;
    const canLiveToggleFg = !!inj?.isInjected && ((inj.appliedConfig?.fgType || cfg.fgType) !== "nofg");
    const statusText = inj?.isInjected
      ? `已注入 · ${formatConfigSummary(inj.appliedConfig || cfg, true)}`
      : "当前目标原生未注入";

    return `
      <div class="qam-body">
        <div id="qamStatusBanner" class="qam-target-card ${inj?.isInjected ? "injected" : "uninjected"}">
          <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
            <strong class="strong-title" style="font-size:13.5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${esc(active?.title || "未选择游戏")}</strong>
            <span class="hh-card-badge ${inj?.isInjected ? "live" : "warn"}">${inj?.isInjected ? "已注入" : "原生未注入"}</span>
          </div>
          <span style="font-size:11px;color:var(--text-secondary);">${esc(statusText)}</span>
          <div style="display:flex;gap:6px;margin-top:4px;">
            <button class="pill-btn small" data-quick-game-scene="vn" data-focusable="true">10W 省电</button>
            <button class="pill-btn small" data-quick-game-scene="act" data-focusable="true">25W 动作</button>
            <button id="btnQamOpenGamePicker" class="btn btn-sm" style="flex:1;justify-content:space-between;" data-focusable="true">
              <span>切换 (${games.length})</span>
              <span>▸</span>
            </button>
          </div>
        </div>

        <div class="qam-section">
          <div class="qam-section-title"><span>预设与超分参数</span></div>
          <div class="qam-preset-row">
            ${presets.slice(0, 3).map((p) => `
              <button class="pill-btn qam-pill ${state.selectedPresetId === p.id ? "active" : ""}" data-preset="${esc(p.id)}" data-focusable="true">${esc(p.name.split("·")[0].trim())}</button>
            `).join("")}
          </div>
          <div style="display:flex;flex-direction:column;gap:7px;margin-top:6px;">
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
            ${cfg.fgType === "xefg" ? `
              <div class="qam-xefg-mfg-row">
                ${renderSegmented("data-xefg-mult", [
                  { value: "2", label: "2X" },
                  { value: "3", label: "3X" },
                  { value: "4", label: "4X" }
                ], String(xeFgMult), "compact")}
                <button id="swXeFgDmfg" class="pill-btn small qam-dmfg-btn ${xeFgDmfg ? "active" : ""}" role="switch" aria-checked="${xeFgDmfg}" data-focusable="true">DMFG ${xeFgDmfg ? "开" : "关"}</button>
              </div>
              ${xeFgDmfg ? renderSegmented("data-xefg-dmfg-fps", [
                { value: "0", label: "DMFG 自动" },
                { value: "60", label: "60" },
                { value: "90", label: "90" },
                { value: "120", label: "120" }
              ], String(xeFgDmfgTarget), "compact") : ""}
              ${showXeFgStableHint ? `<div class="xefg-mfg-hint is-qam">需 Nightly v10+ 内核与 XeSS 3 (libxess_fg.dll)</div>` : ""}
              ${showMissingMfgUnlockerHint ? `<div class="xefg-mfg-hint is-qam">AMD &gt;2X 需在主控台「组件中心」安装 MFG Unlocker (by dashdogy)</div>` : ""}
            ` : ""}
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

        <div style="display:flex;flex-direction:column;gap:7px;">
          <button id="btnInject" class="btn btn-primary" data-focusable="true" ${!active || state.busy ? "disabled" : ""}>
            ${svgIcon("bolt")}
            <span>${inj?.isInjected ? "应用参数并重新注入" : "一键注入推荐配置"}</span>
          </button>
          ${canLiveToggleFg ? `
            <div class="qam-live-fg-row ${active?.isCurrentlyRunning ? "has-restart" : ""}">
              <button id="btnToggleLiveFg" class="btn btn-sm xefg-live-fg-btn" data-focusable="true">${svgIcon("bolt")}<span>实时开关 FG (免菜单)</span></button>
              ${active?.isCurrentlyRunning ? `<button id="btnInjectRestart" class="btn btn-sm" data-focusable="true" ${state.busy ? "disabled" : ""}>应用并重开</button>` : ""}
            </div>
            <div class="xefg-mfg-note">开关 FG 游戏内即时生效；改固定倍率需重开，开 DMFG 自动实时变倍</div>
          ` : ""}
          <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:6px;">
            <button id="btnLaunchGame" class="btn btn-sm" data-focusable="true" ${!active ? "disabled" : ""}>启动</button>
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
      const inGamePicker = state.qamTab === "opti" && state.qamOptiSubView === "games";
      const showXInject = state.qamTab === "opti" && !inGamePicker;
      legend.innerHTML = `
        <div class="legend-group" style="gap:10px;">
          <span class="legend-item"><span class="gp-btn-glyph a">A</span>${inGamePicker ? "选定" : "确认"}</span>
          <span class="legend-item"><span class="gp-btn-glyph b">B</span>${inGamePicker ? "返回" : "关闭"}</span>
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

    const runningNow = getRunningGame();
    const inspectTarget = state.tab === "opti" ? getActiveGame() : runningNow;
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
        <span>${inspectTarget ? (runningNow ? `正在运行: ${esc(runningNow.title)}` : `超分目标: ${esc(inspectTarget.title)}`) : "状态: 桌面待命"}</span>
      </div>
    `;
  }

  function getScrollRouteKey() {
    return IS_QAM
      ? `qam:${state.qamTab}:${state.qamOptiSubView}`
      : `con:${state.tab}:${state.optiSubTab}`;
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
      const pluginCountEl = document.getElementById("topPluginCount");
      if (pluginCountEl) {
        const enabledCount = (state.snapshot?.plugins || []).filter((p) => p.enabled && p.isValid).length;
        pluginCountEl.textContent = String(enabledCount);
        pluginCountEl.classList.toggle("hidden", enabledCount === 0);
      }
      const topVerBtn = document.getElementById("btnTopAppVersion");
      const topVerLabel = document.getElementById("topAppUpdateLabel");
      if (topVerBtn) {
        const curVer = state.snapshot?.appVersion || "0.6.2";
        const upd = state.snapshot?.latestUpdate;
        const hasUpd = !!(upd && upd.hasUpdate);
        topVerBtn.classList.toggle("has-update", hasUpd);
        topVerBtn.title = hasUpd
          ? `发现新版本 v${upd.latestVersion}，点击查看更新日志并一键更新`
          : `当前版本 v${curVer}，点击检查在线更新`;
        const labelText = state.appUpdateChecking
          ? "检查中..."
          : (hasUpd ? `更新 v${upd.latestVersion}` : "检查更新");
        if (topVerLabel) topVerLabel.textContent = labelText;
        else topVerBtn.textContent = labelText;
        const devTag = document.getElementById("deviceTag");
        if (devTag) devTag.textContent = `GPD WIN 5 · v${curVer}`;
      }
    }

    const mainView = document.getElementById("mainView");
    const routeKey = getScrollRouteKey();
    const sameRoute = state.lastScrollRouteKey === routeKey;

    if (mainView && sameRoute && mainView.children.length > 0) {
      state.scrollPositions[routeKey] = {
        mainView: mainView.scrollTop,
        pageScroll: mainView.querySelector(".page-scroll")?.scrollTop || 0,
        qamBody: mainView.querySelector(".qam-body")?.scrollTop || 0,
        qamList: mainView.querySelector(".qam-game-list")?.scrollTop || 0
      };
    }

    if (mainView) {
      // Preserve live mounted plugin <iframe> across background stateChanged telemetry ticks!
      // Otherwise rebuilding innerHTML every 1-2s destroys <input type="color">, <select> popups, and form focus.
      const isViewingPlugin =
        !!state.activePluginId &&
        ((IS_QAM && state.qamTab === "plugins") || (!IS_QAM && state.tab === "plugins"));
      const existingFrame = isViewingPlugin ? document.getElementById("activePluginFrame") : null;
      const activePluginStillValid = isViewingPlugin
        ? (state.snapshot?.plugins || []).some((p) =>
            p.id === state.activePluginId && p.enabled && p.isValid && !p.revokedByStore &&
            (!IS_QAM || (p.mount || []).includes("qam")))
        : false;
      const keepMountedPluginFrame =
        !!existingFrame &&
        activePluginStillValid &&
        existingFrame.dataset.pluginId === state.activePluginId &&
        existingFrame.dataset.reloadNonce === String(state.pluginReloadNonce);

      if (keepMountedPluginFrame) {
        pushThemeToActivePluginFrame();
        renderFooterLegend();
        return;
      }

      if (IS_QAM) {
        mainView.innerHTML = renderQamView();
      } else if (state.tab === "handheld") {
        mainView.innerHTML = renderHandheldView();
      } else if (state.tab === "monitor") {
        mainView.innerHTML = renderMonitorView();
      } else if (state.tab === "controls") {
        mainView.innerHTML = renderControlsView();
      } else if (state.tab === "plugins") {
        mainView.innerHTML = renderPluginsConsoleView();
      } else {
        mainView.innerHTML = renderOptiContainerView();
      }

      const saved = sameRoute ? state.scrollPositions[routeKey] : null;
      if (saved) {
        mainView.scrollTop = saved.mainView;
        const ps = mainView.querySelector(".page-scroll");
        if (ps) ps.scrollTop = saved.pageScroll;
        const qb = mainView.querySelector(".qam-body");
        if (qb) qb.scrollTop = saved.qamBody;
        const ql = mainView.querySelector(".qam-game-list");
        if (ql) ql.scrollTop = saved.qamList;
      } else {
        mainView.scrollTop = 0;
      }
      state.lastScrollRouteKey = routeKey;
    }

    renderFooterLegend();
    bindViewEvents();
    bindPluginViewEvents();
    applySpatialFocus(false);
  }

  // ---------------- Decky-Style Community Plugin Center & Sandbox Host ----------------

  const PLUGIN_PERM_META = {
    "games:read": { label: "读取游戏库", tier: "t1", desc: "读取已扫描游戏列表、安装路径、运行状态及反作弊标识（只读）。" },
    "telemetry:read": { label: "读取硬件遥测", tier: "t1", desc: "读取实时 FPS、功耗、温度、显存、风扇转速与电量（只读）。" },
    "events:game": { label: "订阅游戏启停事件", tier: "t1", desc: "接收游戏启动与退出通知，用于计时、备忘联动等。" },
    "storage": { label: "插件私有存储", tier: "t1", desc: "读写本插件隔离的数据文件，无法访问其他插件或系统设置。" },
    "shell:openFolder": { label: "打开插件数据目录", tier: "t1", desc: "在系统资源管理器中打开本插件专属数据文件夹。" },
    "shell:openUrl": { label: "打开安全外部链接", tier: "t1", desc: "在系统默认浏览器中打开安全的 https:// 网页（如攻略或项目主页）。" },
    "ui:theme": { label: "自定义界面皮肤", tier: "t1", desc: "安全覆写白名单 CSS 主题变量，停用或卸载自动恢复原样。" },
    "backend": { label: "独立后端进程", tier: "t2", desc: "运行隔离的本地子进程（随主程序退出销毁），具备本地脚本/程序执行能力。" },
    "input:sendKey": { label: "向游戏发送功能键", tier: "t3", desc: "向前台游戏发送白名单功能键（Insert / Home / End / F1–F12）。" }
  };

  const PLUGIN_CATEGORY_TABS = [
    { id: "all", label: "全部" },
    { id: "theme", label: "🎨 主题皮肤" },
    { id: "utility", label: "🧰 实用工具" },
    { id: "monitor", label: "📊 监控诊断" },
    { id: "developer", label: "🛠 开发模板" }
  ];

  function renderPluginTrustBadge(obj) {
    const perms = Array.isArray(obj?.permissions) ? obj.permissions : [];
    const hasBackend = !!obj?.hasBackend || perms.includes("backend");
    const hasInput = perms.includes("input:sendKey");
    const badges = [];
    if (obj?.revokedByStore || obj?.isRevoked) {
      badges.push(`<span class="plugin-trust-pill is-revoked">⚠ 已安全熔断下架</span>`);
    } else if (hasBackend) {
      badges.push(`<span class="plugin-trust-pill is-backend" title="包含独立本地子进程，请确认来源开源可信">含本地后端</span>`);
    } else if (hasInput) {
      badges.push(`<span class="plugin-trust-pill is-backend" title="申请了向游戏发送功能键权限">含输入控制</span>`);
    } else {
      badges.push(`<span class="plugin-trust-pill is-sandbox" title="纯前端运行在受限 iframe 沙箱，无法读写硬盘或执行系统命令">纯前端沙箱</span>`);
    }
    if (obj?.sourceCommit && obj?.isOfficialSource) {
      badges.push(`<span class="plugin-trust-pill is-verified" title="开源 Commit: ${esc(obj.sourceCommit)}">✓ 源码编译</span>`);
    }
    return badges.join("");
  }

  function collectPluginThemePayload() {
    const cs = getComputedStyle(document.body);
    const pick = (name, fallback) => (cs.getPropertyValue(name) || "").trim() || fallback;
    const theme = document.body.dataset.theme === "frost" ? "frost" : "aurora";
    const isLight = theme === "frost";
    return {
      __halodeckPluginTheme: true,
      theme,
      surface: IS_QAM ? "qam" : "console",
      cssVars: {
        "--bg-base": pick("--bg-base", isLight ? "#ebf1f9" : "#0e1014"),
        "--bg-app": pick("--bg-base", isLight ? "#ebf1f9" : "#0e1014"),
        "--bg-elevated": pick("--bg-elevated", isLight ? "rgba(255,255,255,0.92)" : "rgba(20,23,29,0.88)"),
        "--bg-card": pick("--bg-card-solid", isLight ? "#ffffff" : "rgba(24,28,35,0.86)"),
        "--bg-row": pick("--bg-row", isLight ? "rgba(232,239,249,0.92)" : "rgba(31,36,45,0.72)"),
        "--bg-subtile": pick("--bg-subtile", isLight ? "rgba(232,239,249,0.88)" : "rgba(31,36,45,0.72)"),
        "--bg-well": pick("--bg-well", isLight ? "rgba(224,233,245,0.88)" : "rgba(11,13,17,0.88)"),
        "--text-primary": pick("--text-primary", isLight ? "#0f172a" : "#f2f5fa"),
        "--text-secondary": pick("--text-secondary", isLight ? "#475569" : "#a7b1c2"),
        "--text-muted": pick("--text-muted", isLight ? "#64748b" : "#6c7688"),
        "--border": pick("--border", isLight ? "rgba(15,23,42,0.11)" : "rgba(255,255,255,0.09)"),
        "--border-strong": pick("--border-strong", isLight ? "rgba(15,23,42,0.20)" : "rgba(255,255,255,0.16)"),
        "--accent": pick("--accent", isLight ? "#2563eb" : "#1a8cff"),
        "--accent-hover": pick("--accent-hover", isLight ? "#1d4ed8" : "#3399ff"),
        "--accent-soft": pick("--accent-soft", isLight ? "rgba(37,99,235,0.14)" : "rgba(26,140,255,0.16)"),
        "--radius-md": pick("--radius-md", "10px"),
        "--radius-lg": pick("--radius-lg", "14px"),
        "--success": "#22c55e",
        "--warn": "#f59e0b",
        "--danger": "#ef4444"
      }
    };
  }

  let lastPushedPluginThemeSig = "";

  function pushThemeToActivePluginFrame(force = false) {
    const iframe = document.getElementById("activePluginFrame");
    if (!iframe?.contentWindow) return;
    const payload = collectPluginThemePayload();
    const sig = `${iframe.dataset.pluginId || ""}|${iframe.dataset.reloadNonce || ""}|${JSON.stringify(payload)}`;
    if (!force && sig === lastPushedPluginThemeSig) return;
    lastPushedPluginThemeSig = sig;
    try {
      iframe.contentWindow.postMessage(payload, "*");
    } catch {}
  }

  function forwardEventToPluginFrame(pluginId, eventName, data) {
    const iframe = document.getElementById("activePluginFrame");
    if (!iframe?.contentWindow || state.activePluginId !== pluginId) return;
    try {
      iframe.contentWindow.postMessage({ __halodeckPluginEvent: true, eventName, data }, "*");
    } catch {}
  }

  function forwardGamepadToPluginFrame(action) {
    const iframe = document.getElementById("activePluginFrame");
    if (!iframe?.contentWindow) return false;
    try {
      iframe.contentWindow.postMessage({ __halodeckPluginGamepad: true, action }, "*");
      return true;
    } catch {
      return false;
    }
  }

  function buildPluginIframeSrc(plugin) {
    const theme = state.snapshot?.settings?.uiTheme || "aurora";
    const surface = IS_QAM ? "qam" : "console";
    const sep = plugin.entryUrl.includes("?") ? "&" : "?";
    return `${plugin.entryUrl}${sep}surface=${encodeURIComponent(surface)}&theme=${encodeURIComponent(theme)}&v=${state.pluginReloadNonce}`;
  }

  function renderPluginPermBadges(perms) {
    if (!perms || !perms.length) {
      return `<span class="plugin-perm-pill t1">无特殊权限</span>`;
    }
    return perms.map((p) => {
      const meta = PLUGIN_PERM_META[p] || { label: p, tier: "t2" };
      return `<span class="plugin-perm-pill ${meta.tier}">${esc(meta.label)}</span>`;
    }).join("");
  }

  function renderPluginSandboxShell(plugin, backLabel) {
    const devMode = !!state.snapshot?.settings?.pluginDeveloperMode;
    return `
      <div class="plugin-frame-shell ${IS_QAM ? "is-qam" : ""}">
        <div class="plugin-frame-bar">
          <div style="display:flex;align-items:center;gap:8px;min-width:0;">
            <button id="btnPluginBack" class="btn btn-sm" data-focusable="true">← ${esc(backLabel)} [B]</button>
            <strong class="plugin-frame-title">${esc(plugin.name)}</strong>
            <span class="hh-card-badge">v${esc(plugin.version)}</span>
          </div>
          <div style="display:flex;align-items:center;gap:6px;">
            ${plugin.hasBackend && devMode ? `<button id="btnPluginFrameRestartBackend" class="btn btn-sm" data-focusable="true">重启后端</button>` : ""}
            ${plugin.hasBackend ? `<button id="btnPluginFrameLogs" class="btn btn-sm" data-focusable="true">日志</button>` : ""}
            <button id="btnPluginFrameReload" class="btn btn-sm" data-focusable="true">重载</button>
          </div>
        </div>
        <iframe
          id="activePluginFrame"
          class="plugin-iframe"
          data-plugin-id="${esc(plugin.id)}"
          data-reload-nonce="${state.pluginReloadNonce}"
          sandbox="allow-scripts allow-forms"
          src="${esc(buildPluginIframeSrc(plugin))}"
          title="${esc(plugin.name)}">
        </iframe>
      </div>
    `;
  }

  async function ensurePluginStoreLoaded(force = false) {
    if (state.pluginStoreLoading) return;
    if (!force && state.pluginStoreData) return;
    state.pluginStoreLoading = true;
    render();
    try {
      state.pluginStoreData = await rpc("fetchPluginStore", {});
    } catch (err) {
      showToast(err.message || "拉取插件商店失败", "error");
    } finally {
      state.pluginStoreLoading = false;
      render();
    }
  }

  function renderPluginsConsoleView() {
    const plugins = state.snapshot?.plugins || [];
    if (state.activePluginId) {
      const active = plugins.find((p) => p.id === state.activePluginId && p.enabled && p.isValid);
      if (active) {
        return renderPluginSandboxShell(active, "插件中心");
      }
      state.activePluginId = null;
    }

    const sub = state.pluginsSubTab || "installed";
    const storeData = state.pluginStoreData;
    const settings = state.snapshot?.settings || {};
    const customUrls = settings.pluginStoreUrls || [];
    const devMode = !!settings.pluginDeveloperMode;
    const mirrorValue = state.pluginMirrorDraft !== null ? state.pluginMirrorDraft : (settings.pluginDownloadMirrorPrefix || "");
    const dlProg = state.pluginDownloadProgress;

    if (sub === "store" && !storeData && !state.pluginStoreLoading) {
      setTimeout(() => ensurePluginStoreLoaded(false), 0);
    }

    const updatableCount = (storeData?.items || []).filter((x) => x.hasUpdate && !x.isRevoked).length;
    const catFilter = state.pluginStoreCategory || "all";
    const q = (state.pluginStoreSearch || "").trim().toLowerCase();
    const storeItems = (storeData?.items || []).filter((item) => {
      if (catFilter !== "all" && (item.category || "utility").toLowerCase() !== catFilter) return false;
      if (!q) return true;
      return (item.name || "").toLowerCase().includes(q) ||
             (item.id || "").toLowerCase().includes(q) ||
             (item.description || "").toLowerCase().includes(q) ||
             (item.author || "").toLowerCase().includes(q);
    });

    return `
      <div class="page-scroll">
        <div class="plugins-page">
          <div class="hh-card">
            <div class="hh-card-head">
              <div>
                <span class="hh-card-title">社区插件中心 · Decky 沙箱模式</span>
                <div class="cw-row-sub" style="margin-top:3px;">零服务器静态源商店 + 隔离前端与独立后端进程；支持开源 Commit 锁包与 SHA-256 校验。</div>
              </div>
              <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                <button id="btnTogglePluginDevMode" class="btn btn-sm ${devMode ? "btn-primary" : ""}" data-focusable="true" title="开启后显示本地目录直导、重启后端与快捷目录按钮">${devMode ? "🛠 开发者模式：开" : "🛠 开发者模式"}</button>
                ${devMode ? `<button id="btnInstallPluginFolder" class="btn btn-sm" data-focusable="true">导入本地目录</button>` : ""}
                <button id="btnInstallPluginZip" class="btn btn-sm btn-primary" data-focusable="true">导入本地 .zip</button>
                <button id="btnOpenPluginsFolder" class="btn btn-sm" data-focusable="true">插件目录</button>
                <button id="btnRefreshPlugins" class="btn btn-sm" data-focusable="true">刷新</button>
              </div>
            </div>

            <div class="plugin-subtabs">
              <button class="pill-btn small ${sub === "installed" ? "active" : ""}" data-plugin-subtab="installed" data-focusable="true">已安装 (${plugins.length})</button>
              <button class="pill-btn small ${sub === "store" ? "active" : ""}" data-plugin-subtab="store" data-focusable="true">插件商店${storeData?.items?.length ? ` (${storeData.items.length})` : ""}${updatableCount > 0 ? ` · ${updatableCount} 可更新` : ""}</button>
              <button class="pill-btn small ${sub === "url" ? "active" : ""}" data-plugin-subtab="url" data-focusable="true">链接直装与网络加速</button>
            </div>

            <div id="pluginDlProgressBanner" class="plugin-dl-banner ${dlProg ? "" : "hidden"}">
              ${dlProg ? `正在下载：${esc(dlProg.label)} · ${Math.round(dlProg.percent || 0)}%` : ""}
            </div>

            ${sub === "installed" ? (
              plugins.length === 0 ? `
                <div class="plugins-empty">
                  <div class="plugins-empty-icon">🔌</div>
                  <strong>暂未安装任何社区插件</strong>
                  <p>可切到「插件商店」一键安装官方皮肤/笔记插件，或在「链接直装」粘贴社区作者的 GitHub 仓库（<code>owner/repo</code>）。</p>
                </div>
              ` : `
                <div class="plugin-list">
                  ${plugins.map((p) => `
                    <div class="plugin-card ${p.enabled ? "is-enabled" : ""} ${!p.isValid ? "is-invalid" : ""}">
                      <div class="plugin-card-icon">
                        ${p.iconUrl ? `<img src="${esc(p.iconUrl)}" alt="" />` : `<span>🔌</span>`}
                      </div>
                      <div class="plugin-card-main">
                        <div class="plugin-card-head">
                          <strong>${esc(p.name)}</strong>
                          <span class="hh-card-badge">v${esc(p.version || "1.0.0")}</span>
                          ${p.author ? `<span class="cw-row-sub">by ${esc(p.author)}</span>` : ""}
                          ${renderPluginTrustBadge(p)}
                          ${p.hasBackend && p.backendRunning ? `<span class="hh-card-badge live">后端运行中</span>` : ""}
                        </div>
                        <div class="plugin-card-desc">${esc(p.description || p.id)}</div>
                        ${p.revokedByStore ? `<div class="plugin-card-err">安全熔断：${esc(p.revokedReason || "官方商店已下架此插件，已自动停用")}</div>` : !p.isValid ? `<div class="plugin-card-err">校验失败：${esc(p.validationError)}</div>` : `
                          <div class="plugin-perm-row">
                            ${(p.mount || []).map((m) => `<span class="plugin-mount-pill">${m === "qam" ? "侧栏" : "主界面"}</span>`).join("")}
                            ${renderPluginPermBadges(p.permissions)}
                          </div>
                        `}
                      </div>
                      <div class="plugin-card-actions">
                        ${p.isValid && p.enabled && !p.revokedByStore && (p.mount || []).includes("console") ? `<button class="btn btn-sm btn-primary" data-plugin-open="${esc(p.id)}" data-focusable="true">打开</button>` : ""}
                        ${devMode && p.isValid ? `<button class="btn btn-sm" data-plugin-open-dir="${esc(p.id)}" data-focusable="true">目录</button>` : ""}
                        ${devMode && p.isValid && p.hasBackend ? `<button class="btn btn-sm" data-plugin-restart-backend="${esc(p.id)}" data-focusable="true">重启后端</button>` : ""}
                        ${p.isValid && p.hasBackend ? `<button class="btn btn-sm" data-plugin-logs="${esc(p.id)}" data-focusable="true">日志</button>` : ""}
                        ${p.isValid && !p.revokedByStore ? renderSwitch(`swPlugin_${p.id}`, !!p.enabled) : ""}
                        <button class="btn btn-sm btn-danger-soft" data-plugin-uninstall="${esc(p.id)}" data-focusable="true">卸载</button>
                      </div>
                    </div>
                  `).join("")}
                </div>
              `
            ) : sub === "store" ? `
              <div class="plugin-store-toolbar">
                <input id="pluginStoreSearchInput" class="plugin-text-input" type="text" placeholder="搜索商店插件名称、作者或简介..." value="${esc(state.pluginStoreSearch)}" />
                ${updatableCount > 0 ? `<button id="btnUpdateAllPlugins" class="btn btn-sm btn-primary" data-focusable="true">一键更新全部 (${updatableCount})</button>` : ""}
                <button id="btnRefreshPluginStore" class="btn btn-sm" data-focusable="true">${state.pluginStoreLoading ? "拉取中..." : "刷新商店"}</button>
              </div>

              <div class="plugin-category-row">
                ${PLUGIN_CATEGORY_TABS.map((cat) => `
                  <button class="pill-btn small ${catFilter === cat.id ? "active" : ""}" data-store-category="${esc(cat.id)}" data-focusable="true">${esc(cat.label)}</button>
                `).join("")}
              </div>

              <div class="plugin-sources-box">
                <div class="plugin-sources-list">
                  ${(storeData?.sources || []).map((s) => `
                    <span class="plugin-source-chip ${s.error ? "is-err" : (s.isOfficial ? "is-official" : "")}" title="${esc(s.error || s.sourceUrl)}">
                      ${s.isOfficial ? "官方源" : "第三方源"} · ${esc(s.storeName)} (${s.count})${s.error ? " ⚠" : ""}
                      ${!s.isOfficial ? `<button class="plugin-source-del" data-remove-store-url="${esc(s.sourceUrl)}" title="移除该商店源">×</button>` : ""}
                    </span>
                  `).join("")}
                </div>
                <div class="plugin-store-add-row">
                  <input id="pluginAddSourceInput" class="plugin-text-input" type="text" placeholder="添加第三方社区 store.json 源地址 (https://...)" value="${esc(state.pluginCustomSourceDraft)}" />
                  <button id="btnAddPluginStoreUrl" class="btn btn-sm" data-focusable="true">+ 订阅源</button>
                </div>
              </div>

              ${state.pluginStoreLoading && !storeData ? `
                <div class="plugins-empty compact"><p>正在通过 jsDelivr / GitHub 拉取商店目录...</p></div>
              ` : storeItems.length === 0 ? `
                <div class="plugins-empty compact"><p>${storeData?.sources?.some((s) => s.error) ? esc(storeData.sources.find((s) => s.error)?.error || "拉取商店失败") : "暂无匹配的商店插件"}</p></div>
              ` : `
                <div class="plugin-list">
                  ${storeItems.map((item) => `
                    <div class="plugin-card ${item.isInstalled ? "is-enabled" : ""}">
                      <div class="plugin-card-icon"><span>${item.category === "theme" ? "🎨" : item.category === "developer" ? "🛠" : item.category === "monitor" ? "📊" : "🔌"}</span></div>
                      <div class="plugin-card-main">
                        <div class="plugin-card-head">
                          <strong>${esc(item.name)}</strong>
                          <span class="hh-card-badge">v${esc(item.version)}</span>
                          <span class="cw-row-sub">by ${esc(item.author || "社区")}</span>
                          <span class="plugin-mount-pill">${item.isOfficialSource ? "官方纯净源" : esc(item.sourceName)}</span>
                          ${renderPluginTrustBadge(item)}
                        </div>
                        <div class="plugin-card-desc">${esc(item.description || item.id)}</div>
                        <div class="plugin-perm-row">
                          ${(item.mount || []).map((m) => `<span class="plugin-mount-pill">${m === "qam" ? "侧栏" : "主界面"}</span>`).join("")}
                          ${renderPluginPermBadges(item.permissions)}
                        </div>
                      </div>
                      <div class="plugin-card-actions">
                        <button class="btn btn-sm" data-store-detail="${esc(item.id)}" data-focusable="true">详情</button>
                        ${(() => {
                          const inst = plugins.find((x) => x.id === item.id);
                          if (item.isRevoked) {
                            return inst
                              ? `<button class="btn btn-sm btn-danger-soft" data-plugin-uninstall="${esc(inst.id)}" data-focusable="true">立即卸载</button>`
                              : `<span class="plugin-trust-pill is-revoked">禁止安装</span>`;
                          }
                          const updateBtn = item.hasUpdate
                            ? `<button class="btn btn-sm btn-primary" data-store-install="${esc(item.zipUrl)}" data-store-sha="${esc(item.sha256 || "")}" data-store-name="${esc(item.name)}" data-focusable="true">更新 v${esc(item.version)}</button>`
                            : "";
                          if (item.isInstalled && inst) {
                            return `${updateBtn}
                              <button class="btn btn-sm ${inst.enabled ? "" : "btn-primary"}" data-plugin-toggle="${esc(inst.id)}" data-focusable="true">${inst.enabled ? "停用" : "启用"}</button>
                              <button class="btn btn-sm btn-danger-soft" data-plugin-uninstall="${esc(inst.id)}" data-focusable="true">卸载</button>`;
                          }
                          return `<button class="btn btn-sm btn-primary" data-store-install="${esc(item.zipUrl)}" data-store-sha="${esc(item.sha256 || "")}" data-store-name="${esc(item.name)}" data-focusable="true">安装</button>`;
                        })()}
                      </div>
                    </div>
                  `).join("")}
                </div>
              `}
            ` : `
              <div class="plugin-url-panel">
                <div class="plugin-consent-warn">
                  支持直接粘贴任意社区插件的 <code>https://.../*.zip</code> 下载链接，或 GitHub 仓库名（如 <code>owner/repo</code>，自动解析最新 Release 中的 <code>.zip</code>）。安装后默认处于停用状态，确认权限后才会运行。
                </div>
                <div class="plugin-store-add-row" style="margin-top:8px;">
                  <input id="pluginDirectUrlInput" class="plugin-text-input" type="text" placeholder="例如：owner/halodeck-plugin 或 https://github.com/.../plugin.zip" value="${esc(state.pluginDirectUrlDraft)}" />
                  <button id="btnInstallPluginFromUrl" class="btn btn-sm btn-primary" data-focusable="true">下载并安装</button>
                </div>
                <div class="plugin-sources-box" style="margin-top:12px;">
                  <div style="font-size:12px;font-weight:700;margin-bottom:4px;">GitHub Release 下载镜像加速（国内网络可选）</div>
                  <div class="cw-row-sub" style="margin-bottom:8px;">商店目录默认走 jsDelivr CDN；若国内下载插件压缩包超时，可填入 HTTPS 镜像前缀（如 <code>https://gh-proxy.com/</code>）。失败时自动回退直连，且官方商店插件始终强制校验 SHA-256。</div>
                  <div class="plugin-store-add-row" style="margin-bottom:0;">
                    <input id="pluginMirrorPrefixInput" class="plugin-text-input" type="text" placeholder="留空表示直连 GitHub；示例：https://gh-proxy.com/" value="${esc(mirrorValue)}" />
                    <button id="btnSavePluginMirror" class="btn btn-sm btn-primary" data-focusable="true">保存镜像</button>
                    ${mirrorValue ? `<button id="btnClearPluginMirror" class="btn btn-sm" data-focusable="true">恢复直连</button>` : ""}
                  </div>
                </div>
                <div class="cw-row-sub" style="margin-top:8px;">
                  自定义已订阅的第三方商店源（共 ${customUrls.length} 个）：可在「插件商店」页管理增删。
                </div>
              </div>
            `}
          </div>
        </div>
      </div>
    `;
  }

  function renderQamPluginsView() {
    const allPlugins = state.snapshot?.plugins || [];
    const enabledQam = allPlugins.filter((p) => p.enabled && p.isValid && (p.mount || []).includes("qam"));
    if (state.activePluginId) {
      const active = enabledQam.find((p) => p.id === state.activePluginId);
      if (active) {
        return renderPluginSandboxShell(active, "已启用插件");
      }
      state.activePluginId = null;
    }

    const qSub = state.qamPluginSubTab || "enabled";
    const storeData = state.pluginStoreData;
    const dlProg = state.pluginDownloadProgress;
    if (qSub === "store" && !storeData && !state.pluginStoreLoading) {
      setTimeout(() => ensurePluginStoreLoaded(false), 0);
    }

    const q = (state.pluginStoreSearch || "").trim().toLowerCase();
    const storeItems = (storeData?.items || []).filter((item) => {
      if (!q) return true;
      return (item.name || "").toLowerCase().includes(q) ||
             (item.id || "").toLowerCase().includes(q) ||
             (item.description || "").toLowerCase().includes(q) ||
             (item.author || "").toLowerCase().includes(q);
    });

    let bodyHtml = "";
    if (qSub === "enabled") {
      bodyHtml = enabledQam.length === 0 ? `
        <div class="plugins-empty compact">
          <p>${allPlugins.length > 0 ? `已安装 ${allPlugins.length} 个插件但尚未启用。` : "暂无已启用的侧栏插件。"}</p>
          <div style="display:flex;gap:6px;justify-content:center;margin-top:8px;flex-wrap:wrap;">
            ${allPlugins.length > 0 ? `<button class="btn btn-sm btn-primary" data-qam-plugin-subtab="manage" data-focusable="true">去管理启用 (${allPlugins.length})</button>` : ""}
            <button class="btn btn-sm" data-qam-plugin-subtab="store" data-focusable="true">逛插件商店</button>
          </div>
        </div>
      ` : `
        <div class="qam-plugin-list">
          ${enabledQam.map((p) => `
            <button class="qam-plugin-item" data-plugin-open="${esc(p.id)}" data-focusable="true">
              <div class="qam-plugin-icon">${p.iconUrl ? `<img src="${esc(p.iconUrl)}" alt="" />` : "🔌"}</div>
              <div class="qam-plugin-copy">
                <strong>${esc(p.name)}</strong>
                <span>${esc(p.description || `v${p.version}`)}</span>
              </div>
              <span class="qam-plugin-arrow">▸</span>
            </button>
          `).join("")}
        </div>
      `;
    } else if (qSub === "manage") {
      bodyHtml = `
        <div style="display:flex;gap:6px;margin-bottom:8px;">
          <button id="btnInstallPluginZip" class="btn btn-sm btn-primary" style="flex:1;" data-focusable="true">导入 .zip</button>
          <button id="btnRefreshPlugins" class="btn btn-sm" data-focusable="true">刷新</button>
        </div>
        ${allPlugins.length === 0 ? `
          <div class="plugins-empty compact">
            <p>尚未安装任何插件，可切到「商店」一键下载安装。</p>
            <button class="btn btn-sm btn-primary" style="margin-top:8px;" data-qam-plugin-subtab="store" data-focusable="true">浏览插件商店</button>
          </div>
        ` : `
          <div class="qam-plugin-list">
            ${allPlugins.map((p) => `
              <div class="qam-plugin-manage-row ${p.enabled ? "is-enabled" : ""}">
                <div class="qam-plugin-manage-top">
                  <div class="qam-plugin-copy">
                    <strong>${esc(p.name)}</strong>
                    <span>v${esc(p.version || "1.0.0")}${p.hasBackend ? " · 独立后端" : ""}${!p.isValid ? ` · ⚠ ${esc(p.validationError)}` : ""}</span>
                  </div>
                  ${p.isValid ? renderSwitch(`swPlugin_${p.id}`, !!p.enabled) : ""}
                </div>
                <div class="qam-plugin-manage-actions">
                  ${p.isValid && p.enabled && (p.mount || []).includes("qam") ? `<button class="btn btn-sm btn-primary" data-plugin-open="${esc(p.id)}" data-focusable="true">打开</button>` : ""}
                  ${p.isValid && p.hasBackend ? `<button class="btn btn-sm" data-plugin-logs="${esc(p.id)}" data-focusable="true">日志</button>` : ""}
                  <button class="btn btn-sm btn-danger-soft" data-plugin-uninstall="${esc(p.id)}" data-focusable="true">卸载</button>
                </div>
              </div>
            `).join("")}
          </div>
        `}
      `;
    } else {
      bodyHtml = `
        <div class="plugin-store-toolbar" style="margin-bottom:8px;">
          <input id="pluginStoreSearchInput" class="plugin-text-input" type="text" placeholder="搜索商店插件..." value="${esc(state.pluginStoreSearch)}" />
          <button id="btnRefreshPluginStore" class="btn btn-sm" data-focusable="true">${state.pluginStoreLoading ? "..." : "刷新"}</button>
        </div>
        <div id="pluginDlProgressBanner" class="plugin-dl-banner ${dlProg ? "" : "hidden"}">
          ${dlProg ? `下载中：${esc(dlProg.label)} · ${Math.round(dlProg.percent || 0)}%` : ""}
        </div>
        ${state.pluginStoreLoading && !storeData ? `
          <div class="plugins-empty compact"><p>正在拉取插件商店目录...</p></div>
        ` : storeItems.length === 0 ? `
          <div class="plugins-empty compact"><p>${storeData?.sources?.some((s) => s.error) ? esc(storeData.sources.find((s) => s.error)?.error || "拉取商店失败") : "暂无匹配的商店插件"}</p></div>
        ` : `
          <div class="qam-plugin-list">
            ${storeItems.map((item) => `
              <div class="qam-plugin-manage-row ${item.isInstalled ? "is-enabled" : ""}">
                <div class="qam-plugin-manage-top">
                  <div class="qam-plugin-copy">
                    <strong>${item.category === "theme" ? "🎨 " : "🔌 "}${esc(item.name)}</strong>
                    <span>v${esc(item.version)} · ${esc(item.author || "社区")}</span>
                  </div>
                  ${(() => {
                    const inst = allPlugins.find((x) => x.id === item.id);
                    if (item.hasUpdate) {
                      return `<button class="btn btn-sm btn-primary" data-store-install="${esc(item.zipUrl)}" data-store-sha="${esc(item.sha256 || "")}" data-store-name="${esc(item.name)}" data-focusable="true">更新</button>`;
                    }
                    if (item.isInstalled && inst) {
                      return `<div style="display:flex;gap:4px;">
                        <button class="btn btn-sm ${inst.enabled ? "" : "btn-primary"}" data-plugin-toggle="${esc(inst.id)}" data-focusable="true">${inst.enabled ? "停用" : "启用"}</button>
                        <button class="btn btn-sm btn-danger-soft" data-plugin-uninstall="${esc(inst.id)}" data-focusable="true">卸载</button>
                      </div>`;
                    }
                    return `<button class="btn btn-sm btn-primary" data-store-install="${esc(item.zipUrl)}" data-store-sha="${esc(item.sha256 || "")}" data-store-name="${esc(item.name)}" data-focusable="true">安装</button>`;
                  })()}
                </div>
                <div class="qam-plugin-store-desc">${esc(item.description || item.id)}</div>
              </div>
            `).join("")}
          </div>
        `}
      `;
    }

    return `
      <div class="qam-body">
        <div class="qam-section">
          <div class="qam-section-title">
            <span>插件扩展 (Decky)</span>
            <span class="hh-card-badge ${enabledQam.length ? "live" : ""}">${enabledQam.length}/${allPlugins.length} 已启用</span>
          </div>
          <div class="qam-plugin-subtabs">
            <button class="pill-btn qam-pill ${qSub === "enabled" ? "active" : ""}" data-qam-plugin-subtab="enabled" data-focusable="true">已启用 (${enabledQam.length})</button>
            <button class="pill-btn qam-pill ${qSub === "manage" ? "active" : ""}" data-qam-plugin-subtab="manage" data-focusable="true">管理 (${allPlugins.length})</button>
            <button class="pill-btn qam-pill ${qSub === "store" ? "active" : ""}" data-qam-plugin-subtab="store" data-focusable="true">商店</button>
          </div>
          ${bodyHtml}
          <button id="btnQamOpenPluginCenter" class="btn btn-sm" style="width:100%;margin-top:8px;" data-focusable="true">打开主界面「插件扩展」</button>
        </div>
      </div>
    `;
  }

  function openPluginConsentModal(plugin) {
    const modal = document.getElementById("modalBackdrop");
    const title = document.getElementById("modalTitle");
    const sub = document.getElementById("modalSubtitle");
    const body = document.getElementById("modalBody");
    if (!modal || !title || !sub || !body) return;

    title.textContent = `首次启用第三方社区插件：${plugin.name}`;
    sub.textContent = `${plugin.id} · v${plugin.version}${plugin.author ? ` · 作者 ${plugin.author}` : ""}`;
    body.innerHTML = `
      <div style="display:flex;flex-direction:column;gap:12px;">
        <div class="plugin-consent-warn">
          该插件由第三方社区开发者提供，并非 HaloDeck 官方内置功能。启用后请自行甄别其来源与用途，切勿在联机反作弊游戏中使用违规工具。
        </div>
        <div>
          <div style="font-size:12px;font-weight:700;margin-bottom:6px;">该插件申请的权限：</div>
          <div class="plugin-perm-row">${renderPluginPermBadges(plugin.permissions)}</div>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:6px;">
          <button id="btnPluginConsentCancel" class="btn btn-sm" data-focusable="true">取消</button>
          <button id="btnPluginConsentConfirm" class="btn btn-sm btn-primary" data-focusable="true">我已知晓风险并启用</button>
        </div>
      </div>
    `;

    modal.classList.remove("hidden");
    document.getElementById("btnPluginConsentCancel")?.addEventListener("click", () => {
      modal.classList.add("hidden");
      applySpatialFocus();
    });
    document.getElementById("btnPluginConsentConfirm")?.addEventListener("click", async () => {
      try {
        await rpc("setPluginEnabled", { pluginId: plugin.id, enabled: true, consentAccepted: true });
        modal.classList.add("hidden");
        showToast(`已启用插件：${plugin.name}`, "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    state.focusIndex = 0;
    applySpatialFocus();
  }

  async function openPluginLogsModal(pluginId, pluginName) {
    try {
      const res = await rpc("getPluginLogs", { pluginId });
      const modal = document.getElementById("modalBackdrop");
      const title = document.getElementById("modalTitle");
      const sub = document.getElementById("modalSubtitle");
      const body = document.getElementById("modalBody");
      if (!modal || !title || !sub || !body) return;
      title.textContent = `插件后端日志 · ${pluginName || pluginId}`;
      sub.textContent = res?.running ? "独立后端进程运行中" : "后端进程当前未运行（按需懒启动）";
      const lines = Array.isArray(res?.logs) && res.logs.length ? res.logs.join("\n") : "（暂无 stderr 调试输出）";
      body.innerHTML = `<pre class="log-viewer">${esc(lines)}</pre>`;
      modal.classList.remove("hidden");
    } catch (err) {
      showToast(err.message, "error");
    }
  }

  function openPluginDetailModal(item) {
    const modal = document.getElementById("modalBackdrop");
    const title = document.getElementById("modalTitle");
    const sub = document.getElementById("modalSubtitle");
    const body = document.getElementById("modalBody");
    if (!modal || !title || !sub || !body || !item) return;

    const perms = Array.isArray(item.permissions) ? item.permissions : [];
    const repoUrl = item.sourceRepo || item.homepage || "";
    const commitSha = item.sourceCommit || "";
    const commitUrl = repoUrl && commitSha ? `${repoUrl.replace(/\/$/, "")}/commit/${commitSha}` : "";

    title.textContent = `${item.name} · v${item.version}`;
    sub.textContent = `${item.id} · ${item.isOfficialSource ? "官方纯净商店" : (item.sourceName || "第三方社区源")}`;
    body.innerHTML = `
      <div class="plugin-detail-box">
        <div class="plugin-detail-head">
          ${renderPluginTrustBadge(item)}
          <span class="hh-card-badge">作者：${esc(item.author || "社区")}</span>
          ${item.isInstalled ? `<span class="hh-card-badge live">已安装 v${esc(item.installedVersion || item.version)}</span>` : ""}
        </div>
        <p class="plugin-detail-desc">${esc(item.description || "暂无说明")}</p>

        <div class="plugin-detail-meta">
          <div class="plugin-detail-row">
            <span>开源仓库</span>
            ${repoUrl ? `<button class="bento-link-pill" data-open-ext-url="${esc(repoUrl)}" data-focusable="true">${esc(repoUrl)} ↗</button>` : `<span class="cw-row-sub">未提供</span>`}
          </div>
          <div class="plugin-detail-row">
            <span>锁定 Commit</span>
            ${commitSha ? (commitUrl ? `<button class="bento-link-pill" data-open-ext-url="${esc(commitUrl)}" data-focusable="true"><code>${esc(commitSha.slice(0, 12))}</code> ↗</button>` : `<code>${esc(commitSha)}</code>`) : `<span class="cw-row-sub">未锁定</span>`}
          </div>
          <div class="plugin-detail-row">
            <span>SHA-256 校验</span>
            ${item.sha256 ? `<code class="plugin-detail-sha">${esc(item.sha256)}</code>` : `<span class="cw-row-sub">未声明（仅 HTTPS 传输）</span>`}
          </div>
        </div>

        <div class="plugin-detail-perms">
          <div style="font-size:12px;font-weight:700;margin-bottom:6px;">申请权限逐条解读（共 ${perms.length} 项）</div>
          ${perms.length === 0 ? `<div class="cw-row-sub">无需任何特殊系统权限（纯展示沙箱）。</div>` : perms.map((p) => {
            const meta = PLUGIN_PERM_META[p] || { label: p, tier: "t2", desc: "插件自定义权限声明。" };
            return `
              <div class="plugin-detail-perm-item">
                <span class="plugin-perm-pill ${meta.tier}">${esc(meta.label)}</span>
                <span class="cw-row-sub">${esc(meta.desc || "")}</span>
              </div>
            `;
          }).join("")}
        </div>

        <div class="modal-actions" style="margin-top:12px;">
          ${!item.isRevoked && (!item.isInstalled || item.hasUpdate) ? `<button id="btnDetailInstallPlugin" class="btn btn-primary" data-focusable="true">${item.hasUpdate ? `立即更新至 v${esc(item.version)}` : `安装 v${esc(item.version)}`}</button>` : ""}
          <button id="btnDetailClose" class="btn" data-focusable="true">关闭</button>
        </div>
      </div>
    `;
    modal.classList.remove("hidden");

    body.querySelectorAll("[data-open-ext-url]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        try {
          await rpc("openExternalUrl", { url: btn.dataset.openExtUrl });
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.getElementById("btnDetailInstallPlugin")?.addEventListener("click", async () => {
      modal.classList.add("hidden");
      try {
        const res = await rpc("installPluginFromUrl", {
          url: item.zipUrl,
          sha256: item.sha256 || "",
          name: item.name
        });
        showToast(res?.message || "插件安装完成", "success");
        await ensurePluginStoreLoaded(true);
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnDetailClose")?.addEventListener("click", () => {
      modal.classList.add("hidden");
    });
  }

  function bindPluginViewEvents() {
    const iframe = document.getElementById("activePluginFrame");
    if (iframe) {
      iframe.addEventListener("load", () => pushThemeToActivePluginFrame(true));
      document.getElementById("btnPluginBack")?.addEventListener("click", () => {
        state.activePluginId = null;
        state.focusIndex = 0;
        render();
      });
      document.getElementById("btnPluginFrameReload")?.addEventListener("click", () => {
        state.pluginReloadNonce++;
        render();
      });
      document.getElementById("btnPluginFrameRestartBackend")?.addEventListener("click", async () => {
        const p = (state.snapshot?.plugins || []).find((x) => x.id === state.activePluginId);
        if (!p) return;
        try {
          const res = await rpc("restartPluginBackend", { pluginId: p.id });
          showToast(res?.message || "已重启插件后端", "info");
          state.pluginReloadNonce++;
          render();
        } catch (err) {
          showToast(err.message, "error");
        }
      });
      document.getElementById("btnPluginFrameLogs")?.addEventListener("click", () => {
        const p = (state.snapshot?.plugins || []).find((x) => x.id === state.activePluginId);
        if (p) openPluginLogsModal(p.id, p.name);
      });
      return;
    }

    document.getElementById("btnTogglePluginDevMode")?.addEventListener("click", async () => {
      const next = !state.snapshot?.settings?.pluginDeveloperMode;
      try {
        await rpc("updatePluginPreferences", { pluginDeveloperMode: next });
        showToast(next ? "已开启插件开发者模式（显示目录直导、重启后端与源码目录入口）" : "已关闭插件开发者模式", "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnInstallPluginFolder")?.addEventListener("click", async () => {
      try {
        const res = await rpc("installPluginFolder", {});
        if (res?.cancelled) return;
        showToast(res?.message || "已从本地目录同步插件", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-plugin-open-dir]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        try {
          await rpc("openPluginDirectory", { pluginId: btn.dataset.pluginOpenDir });
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.querySelectorAll("[data-plugin-restart-backend]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        try {
          const res = await rpc("restartPluginBackend", { pluginId: btn.dataset.pluginRestartBackend });
          showToast(res?.message || "已重启插件后端", "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.querySelectorAll("[data-store-category]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.pluginStoreCategory = btn.dataset.storeCategory || "all";
        state.focusIndex = 0;
        render();
      });
    });

    document.querySelectorAll("[data-store-detail]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const id = btn.dataset.storeDetail;
        const item = (state.pluginStoreData?.items || []).find((x) => x.id === id);
        if (item) openPluginDetailModal(item);
      });
    });

    document.getElementById("btnUpdateAllPlugins")?.addEventListener("click", async () => {
      try {
        const res = await rpc("updateAllPlugins", {});
        showToast(res?.message || "批量更新完成", res?.ok ? "success" : "warn");
        await ensurePluginStoreLoaded(true);
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("pluginMirrorPrefixInput")?.addEventListener("input", (e) => {
      state.pluginMirrorDraft = e.target.value || "";
    });

    document.getElementById("btnSavePluginMirror")?.addEventListener("click", async () => {
      const prefix = (document.getElementById("pluginMirrorPrefixInput")?.value || "").trim();
      try {
        await rpc("updatePluginPreferences", { pluginDownloadMirrorPrefix: prefix });
        state.pluginMirrorDraft = null;
        showToast(prefix ? `已保存 GitHub 镜像加速前缀：${prefix}` : "已切换为 GitHub 直连下载", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnClearPluginMirror")?.addEventListener("click", async () => {
      try {
        await rpc("updatePluginPreferences", { pluginDownloadMirrorPrefix: "" });
        state.pluginMirrorDraft = null;
        showToast("已恢复 GitHub 直连下载", "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnInstallPluginZip")?.addEventListener("click", async () => {
      try {
        const res = await rpc("installPluginZip", {});
        if (res?.cancelled) return;
        showToast(res?.message || "插件导入成功", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnOpenPluginsFolder")?.addEventListener("click", async () => {
      try {
        await rpc("openPluginsFolder", {});
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnRefreshPlugins")?.addEventListener("click", async () => {
      try {
        const list = await rpc("listPlugins", {});
        if (state.snapshot) state.snapshot.plugins = list || [];
        if (state.pluginsSubTab === "store") {
          await ensurePluginStoreLoaded(true);
        } else {
          render();
        }
        showToast(`已刷新插件列表（已安装 ${(list || []).length} 个）`, "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-plugin-subtab]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.pluginsSubTab = btn.dataset.pluginSubtab || "installed";
        state.focusIndex = 0;
        render();
        if (state.pluginsSubTab === "store" && !state.pluginStoreData) {
          ensurePluginStoreLoaded(false);
        }
      });
    });

    document.querySelectorAll("[data-qam-plugin-subtab]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.qamPluginSubTab = btn.dataset.qamPluginSubtab || "enabled";
        state.activePluginId = null;
        state.focusIndex = 0;
        render();
        if (state.qamPluginSubTab === "store" && !state.pluginStoreData) {
          ensurePluginStoreLoaded(false);
        }
      });
    });

    document.getElementById("pluginStoreSearchInput")?.addEventListener("input", (e) => {
      state.pluginStoreSearch = e.target.value || "";
      render();
      const el = document.getElementById("pluginStoreSearchInput");
      if (el) { el.focus(); el.setSelectionRange(el.value.length, el.value.length); }
    });

    document.getElementById("btnRefreshPluginStore")?.addEventListener("click", () => ensurePluginStoreLoaded(true));

    document.getElementById("pluginAddSourceInput")?.addEventListener("input", (e) => {
      state.pluginCustomSourceDraft = e.target.value || "";
    });

    document.getElementById("btnAddPluginStoreUrl")?.addEventListener("click", async () => {
      const url = (document.getElementById("pluginAddSourceInput")?.value || state.pluginCustomSourceDraft || "").trim();
      if (!url) { showToast("请输入第三方 store.json 地址", "warn"); return; }
      try {
        await rpc("addPluginStoreUrl", { url });
        state.pluginCustomSourceDraft = "";
        showToast("已订阅第三方插件源，正在拉取目录...", "success");
        await ensurePluginStoreLoaded(true);
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-remove-store-url]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const url = btn.dataset.removeStoreUrl;
        try {
          await rpc("removePluginStoreUrl", { url });
          showToast("已移除第三方商店源", "info");
          await ensurePluginStoreLoaded(true);
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.querySelectorAll("[data-store-install]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const url = btn.dataset.storeInstall;
        const sha256 = btn.dataset.storeSha || "";
        const name = btn.dataset.storeName || url;
        try {
          const res = await rpc("installPluginFromUrl", { url, sha256, name });
          showToast(res?.message || "插件安装完成", "success");
          await ensurePluginStoreLoaded(true);
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.getElementById("pluginDirectUrlInput")?.addEventListener("input", (e) => {
      state.pluginDirectUrlDraft = e.target.value || "";
    });

    document.getElementById("btnInstallPluginFromUrl")?.addEventListener("click", async () => {
      const url = (document.getElementById("pluginDirectUrlInput")?.value || state.pluginDirectUrlDraft || "").trim();
      if (!url) { showToast("请输入插件 .zip 链接或 GitHub owner/repo", "warn"); return; }
      try {
        const res = await rpc("installPluginFromUrl", { url, name: url });
        state.pluginDirectUrlDraft = "";
        state.pluginsSubTab = "installed";
        showToast(res?.message || "插件安装完成", "success");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnQamOpenPluginCenter")?.addEventListener("click", () => {
      const subtab = state.qamPluginSubTab === "store" ? "store" : "installed";
      rpc("showConsole", { tab: "plugins", subtab });
    });

    document.querySelectorAll("[data-plugin-open]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.activePluginId = btn.dataset.pluginOpen || null;
        state.focusIndex = 0;
        render();
      });
    });

    document.querySelectorAll("[data-plugin-logs]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const pid = btn.dataset.pluginLogs;
        const p = (state.snapshot?.plugins || []).find((x) => x.id === pid);
        openPluginLogsModal(pid, p?.name || pid);
      });
    });

    document.querySelectorAll("[data-plugin-uninstall]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const pid = btn.dataset.pluginUninstall;
        try {
          await rpc("uninstallPlugin", { pluginId: pid, deleteData: false });
          if (state.activePluginId === pid) state.activePluginId = null;
          if (state.pluginStoreData?.items) {
            state.pluginStoreData.items = state.pluginStoreData.items.map((it) =>
              it.id === pid ? { ...it, isInstalled: false, hasUpdate: false } : it
            );
          }
          showToast(`已卸载插件 ${pid}（私有数据已保留在 plugin_data）`, "info");
          render();
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.querySelectorAll("[data-plugin-toggle]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const pid = btn.dataset.pluginToggle;
        const p = (state.snapshot?.plugins || []).find((x) => x.id === pid);
        if (!p || !p.isValid) return;
        if (!p.enabled && !p.consentAccepted) {
          openPluginConsentModal(p);
          return;
        }
        try {
          await rpc("setPluginEnabled", { pluginId: p.id, enabled: !p.enabled, consentAccepted: true });
          showToast(!p.enabled ? `已启用插件：${p.name}` : `已停用插件：${p.name}`, "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    (state.snapshot?.plugins || []).forEach((p) => {
      if (!p.isValid) return;
      document.getElementById(`swPlugin_${p.id}`)?.addEventListener("click", async () => {
        if (!p.enabled && !p.consentAccepted) {
          openPluginConsentModal(p);
          return;
        }
        try {
          await rpc("setPluginEnabled", { pluginId: p.id, enabled: !p.enabled, consentAccepted: true });
          showToast(!p.enabled ? `已启用插件：${p.name}` : `已停用插件：${p.name}`, "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });
  }

  // Strict sandboxed iframe <-> HaloDeck host bridge (only trusts the currently mounted activePluginFrame)
  window.addEventListener("message", async (event) => {
    const msg = event.data;
    if (!msg || !msg.__halodeckPlugin) return;

    const iframe = document.getElementById("activePluginFrame");
    if (!iframe || event.source !== iframe.contentWindow || !state.activePluginId) {
      return;
    }

    if (msg.action === "sdkLoaded") {
      pushThemeToActivePluginFrame(true);
      return;
    }

    if (msg.action === "toast") {
      showToast(msg.args?.message || "", msg.args?.kind || "info");
      return;
    }

    const pluginId = state.activePluginId;
    try {
      const result = await rpc("pluginHostApi", {
        pluginId,
        action: msg.action,
        args: msg.args || {}
      });
      if (msg.id && iframe.contentWindow) {
        iframe.contentWindow.postMessage({ __halodeckPluginReply: true, id: msg.id, ok: true, result }, "*");
      }
    } catch (err) {
      if (msg.id && iframe.contentWindow) {
        iframe.contentWindow.postMessage({
          __halodeckPluginReply: true,
          id: msg.id,
          ok: false,
          error: err?.message || String(err)
        }, "*");
      }
    }
  });

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
    showToast("再次点击「确认关闭」将在 3.5 秒内结束当前游戏进程", "warn");
    return false;
  }

  const controlCoalescers = new Map();

  function scheduleCoalescedControl(key, rpcFactory, minIntervalMs = 45, immediate = false) {
    let entry = controlCoalescers.get(key);
    if (!entry) {
      entry = { inFlight: false, hasPending: false, pendingFactory: null, timer: null, lastSentAt: 0 };
      controlCoalescers.set(key, entry);
    }
    entry.pendingFactory = rpcFactory;
    entry.hasPending = true;
    if (entry.timer) {
      clearTimeout(entry.timer);
      entry.timer = null;
    }

    const pump = () => {
      if (entry.inFlight || !entry.hasPending) return;
      const elapsed = Date.now() - entry.lastSentAt;
      if (!immediate && elapsed < minIntervalMs) {
        entry.timer = setTimeout(() => {
          entry.timer = null;
          pump();
        }, minIntervalMs - elapsed);
        return;
      }
      const fn = entry.pendingFactory;
      entry.hasPending = false;
      entry.pendingFactory = null;
      entry.inFlight = true;
      entry.lastSentAt = Date.now();
      Promise.resolve()
        .then(() => fn())
        .catch((err) => {
          if (err?.message) showToast(err.message, "error");
        })
        .finally(() => {
          entry.inFlight = false;
          if (entry.hasPending) {
            immediate = false;
            pump();
          }
        });
    };

    pump();
  }

  function updateTdpUiImmediate(watts) {
    const w = Math.max(8, Math.min(120, Math.round(Number(watts) || 40)));
    if (state.snapshot?.settings) {
      state.snapshot.settings.currentTdpWatts = w;
    }
    const rangeEl = document.getElementById("tdpRangeInput");
    if (rangeEl && document.activeElement !== rangeEl) rangeEl.value = String(w);
    const numEl = document.getElementById("tdpNumberInput");
    if (numEl && document.activeElement !== numEl) numEl.value = String(w);
    const heroVal = document.getElementById("bentoTdpHeroVal");
    if (heroVal) heroVal.textContent = String(w);
    const trackFill = document.getElementById("tdpTrackFill");
    if (trackFill) {
      const pct = Math.max(0, Math.min(100, ((w - 8) / (120 - 8)) * 100));
      trackFill.style.width = `${pct.toFixed(1)}%`;
    }

    const hw = state.snapshot?.systemControl?.hardwareService || {};
    const consoleBadge = document.getElementById("consoleTdpBadge");
    if (consoleBadge) {
      const isCockpit = state.snapshot?.settings?.uiLayout === "cockpit";
      consoleBadge.textContent = isCockpit
        ? (hw.smuReady ? `目标 ${w}W` : `设定 ${w}W`)
        : resolveTdpSceneLabel(w);
    }
    const qamReadout = document.getElementById("qamTdpReadout");
    if (qamReadout) {
      qamReadout.textContent = `${w}W · ${resolveTdpSceneLabel(w)}`;
    }
    const apuNow = state.snapshot?.systemControl?.apuPowerWatts;
    const qamStripApu = document.getElementById("qamStripApu");
    if (qamStripApu && apuNow != null) {
      qamStripApu.textContent = `${apuNow.toFixed(1)}W / ${w}W`;
    }
    const kpiApuSub = document.getElementById("kpiApuSub");
    if (kpiApuSub) {
      kpiApuSub.textContent = `设定 ${w}W · ${state.snapshot?.systemControl?.adlAvailable ? "ADL2 采样" : "待命"}`;
    }
    const pmLine = document.getElementById("pmReadbackLine");
    if (pmLine && !state.snapshot?.settings?.unlockThreeStagePower) {
      pmLine.textContent = `SMU 目标：SPL ${w}W · sPPT ${w}W · fPPT ${Math.min(140, w + 2)}W`;
    }
    document.querySelectorAll("[data-tdp-watt]").forEach((btn) => {
      btn.classList.toggle("active", Number(btn.dataset.tdpWatt) === w);
    });
  }

  function updateBrightnessUiImmediate(pct) {
    const p = Math.max(5, Math.min(100, Math.round(Number(pct) || 70)));
    if (state.snapshot?.systemControl) {
      state.snapshot.systemControl.brightnessPercent = p;
    }
    const fill = document.getElementById("brightFill");
    if (fill) fill.style.width = `${p}%`;
    const valEl = document.getElementById("brightVal");
    if (valEl) {
      const supported = state.snapshot?.systemControl?.brightnessSupported !== false;
      valEl.textContent = supported ? `亮度 ${p}%` : "外接屏不支持";
    }
    const rangeEl = document.getElementById("brightRange");
    if (rangeEl && state.activeSliderDrag !== "bright") rangeEl.value = String(p);
  }

  function updateVolumeUiImmediate(pct, isMuted = false) {
    const p = Math.max(0, Math.min(100, Math.round(Number(pct) || 0)));
    if (state.snapshot?.systemControl) {
      state.snapshot.systemControl.volumePercent = p;
      state.snapshot.systemControl.isMuted = !!isMuted;
    }
    const fill = document.getElementById("volFill");
    if (fill) fill.style.width = `${isMuted ? 0 : p}%`;
    const valEl = document.getElementById("volVal");
    if (valEl) valEl.textContent = isMuted ? "静音" : `${p}%`;
    const rangeEl = document.getElementById("volRange");
    if (rangeEl && state.activeSliderDrag !== "vol") rangeEl.value = String(p);
    const muteBtn = document.getElementById("btnToggleMute");
    if (muteBtn) {
      muteBtn.classList.toggle("active", !!isMuted);
      muteBtn.textContent = isMuted ? "静音" : "🔊";
    }
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

    const setTdpWatts = (watts, showFeedback = false) => {
      const clamped = Math.max(8, Math.min(120, Math.round(watts)));
      updateTdpUiImmediate(clamped);
      scheduleCoalescedControl("tdp", async () => {
        const res = await rpc("setTdp", { watts: clamped });
        if (showFeedback) {
          showToast(res?.message || `目标 TDP 已设为 ${clamped}W`, res?.hardwareApplied ? "success" : "info");
        }
      }, 0, true);
    };

    // Handheld Open Power, 3-Stage PPT, Fan Direct % & Curves, Toolbox
    document.querySelectorAll("[data-tdp-watt]").forEach((el) => {
      el.addEventListener("click", () => {
        const watts = Number(el.dataset.tdpWatt);
        if (watts > 0) setTdpWatts(watts, true);
      });
    });

    document.getElementById("btnAddTdpPreset")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings || {};
      const curW = s.currentTdpWatts || 40;
      const list = Array.isArray(s.customTdpPresets) ? [...s.customTdpPresets] : [15, 25, 35, 45, 60, 80, 100];
      if (!list.includes(curW)) list.push(curW);
      list.sort((a, b) => a - b);
      await rpc("savePowerConfig", { customTdpPresets: list });
      showToast(`已将 ${curW}W 添加到自定义档位组`, "success");
    });

    document.getElementById("btnDelTdpPreset")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings || {};
      const curW = s.currentTdpWatts || 40;
      const list = (s.customTdpPresets || []).filter((w) => w !== curW);
      if (list.length > 0) {
        await rpc("savePowerConfig", { customTdpPresets: list });
        showToast(`已从档位组移除 ${curW}W`, "info");
      }
    });

    document.getElementById("btnOpenSettingsJson")?.addEventListener("click", async () => {
      try {
        const res = await rpc("openSettingsJson", {});
        showToast(res?.message || "已在记事本打开 settings.json", "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.getElementById("btnReloadSettingsJson")?.addEventListener("click", async () => {
      try {
        const res = await rpc("reloadSettingsJson", {});
        showToast(res?.message || "已重载 settings.json", "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    const tdpRange = document.getElementById("tdpRangeInput");
    const tdpNum = document.getElementById("tdpNumberInput");
    tdpRange?.addEventListener("input", () => {
      const val = Number(tdpRange.value);
      updateTdpUiImmediate(val);
      scheduleCoalescedControl("tdp", () => rpc("setTdp", { watts: val, silent: true }), 45);
    });
    tdpRange?.addEventListener("change", () => setTdpWatts(Number(tdpRange.value), false));
    tdpNum?.addEventListener("change", () => setTdpWatts(Number(tdpNum.value), true));

    document.getElementById("btnTdpDec")?.addEventListener("click", () => {
      setTdpWatts((state.snapshot?.settings?.currentTdpWatts || 40) - 1, false);
    });
    document.getElementById("btnTdpInc")?.addEventListener("click", () => {
      setTdpWatts((state.snapshot?.settings?.currentTdpWatts || 40) + 1, false);
    });

    const toggleFlatPowerLock = async () => {
      const s = state.snapshot?.settings || {};
      const next = !s.flatPowerLock;
      await rpc("savePowerConfig", { flatPowerLock: next, applyNow: true });
      showToast(next ? `已启用稳态功耗锁：SPL = sPPT = fPPT = ${s.currentTdpWatts || 40}W` : "已恢复默认平滑双段功耗", next ? "success" : "info");
    };
    document.getElementById("swFlatLock")?.addEventListener("click", toggleFlatPowerLock);
    document.getElementById("btnFlatLockQuick")?.addEventListener("click", toggleFlatPowerLock);

    document.getElementById("swUnlockThreeStage")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings || {};
      const next = !s.unlockThreeStagePower;
      await rpc("savePowerConfig", { unlockThreeStagePower: next, applyNow: true });
      showToast(next ? "已解锁独立三段功耗 (SPL / sPPT / fPPT)" : "已恢复联动双段功耗", "info");
    });

    const slowRange = document.getElementById("slowPptRange");
    const fastRange = document.getElementById("fastPptRange");
    slowRange?.addEventListener("input", () => {
      const el = document.getElementById("slowPptVal");
      if (el) el.textContent = `${slowRange.value} W`;
    });
    slowRange?.addEventListener("change", () => rpc("savePowerConfig", { slowPptWatts: Number(slowRange.value), applyNow: true }));
    fastRange?.addEventListener("input", () => {
      const el = document.getElementById("fastPptVal");
      if (el) el.textContent = `${fastRange.value} W`;
    });
    fastRange?.addEventListener("change", () => rpc("savePowerConfig", { fastPptWatts: Number(fastRange.value), applyNow: true }));
    document.querySelectorAll("[data-stage-step]").forEach((el) => {
      el.addEventListener("click", async () => {
        const [kind, dStr] = el.dataset.stageStep.split(":");
        const delta = Number(dStr);
        const s = state.snapshot?.settings || {};
        if (kind === "slow") {
          const next = Math.max(8, Math.min(130, (s.slowPptWatts || 45) + delta));
          await rpc("savePowerConfig", { slowPptWatts: next, applyNow: true });
        } else {
          const next = Math.max(8, Math.min(140, (s.fastPptWatts || 50) + delta));
          await rpc("savePowerConfig", { fastPptWatts: next, applyNow: true });
        }
      });
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
      const bat = isAc ? (s.batteryTdpWatts || 25) : Math.max(8, Math.min(120, (s.batteryTdpWatts || 25) + delta));
      const ac = isAc ? Math.max(8, Math.min(120, (s.acTdpWatts || 55) + delta)) : (s.acTdpWatts || 55);
      await rpc("setPowerLinkage", { enabled: !!s.powerLinkageEnabled, batteryWatts: bat, acWatts: ac });
    };
    document.getElementById("btnBatTdpDec")?.addEventListener("click", () => stepLinkage(false, -1));
    document.getElementById("btnBatTdpInc")?.addEventListener("click", () => stepLinkage(false, 1));
    document.getElementById("btnAcTdpDec")?.addEventListener("click", () => stepLinkage(true, -1));
    document.getElementById("btnAcTdpInc")?.addEventListener("click", () => stepLinkage(true, 1));

    document.querySelectorAll("[data-fan-mode]").forEach((el) => {
      el.addEventListener("click", async () => {
        try {
          const mode = el.dataset.fanMode;
          const s = state.snapshot?.settings || {};
          const res = await rpc("setFanMode", {
            mode,
            manualPercent: s.manualFanPercent || 55,
            curveId: s.activeFanCurveId || "linear_60"
          });
          showToast(res?.message || `风扇模式已切换为 ${formatFanModeLabel(mode)}`, res?.gpdConflict ? "warn" : (res?.hardwareApplied ? "success" : "info"));
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    const applyDirectFanPct = async (pct) => {
      const clamped = Math.max(20, Math.min(100, Math.round(pct)));
      try {
        const res = await rpc("setFanMode", { mode: "manual", manualPercent: clamped });
        showToast(res?.message || `风扇直接百分比已设为 ${clamped}%`, res?.gpdConflict ? "warn" : (res?.hardwareApplied ? "success" : "info"));
      } catch (err) {
        showToast(err.message, "error");
      }
    };

    const fanRange = document.getElementById("fanManualRange");
    const beginFanDrag = () => {
      state.isDraggingRange = true;
      state.dragCooldownUntil = Date.now() + 900;
    };
    const endFanDrag = () => {
      state.isDraggingRange = false;
      state.dragCooldownUntil = Date.now() + 250;
      if (state.pendingSnapshot) {
        const nextSnap = state.pendingSnapshot;
        state.pendingSnapshot = null;
        applySnapshot(nextSnap);
      }
    };
    fanRange?.addEventListener("pointerdown", beginFanDrag);
    fanRange?.addEventListener("mousedown", beginFanDrag);
    fanRange?.addEventListener("touchstart", beginFanDrag, { passive: true });
    fanRange?.addEventListener("input", () => {
      state.isDraggingRange = true;
      state.dragCooldownUntil = Date.now() + 600;
      const val = document.getElementById("fanManualVal");
      if (val) val.textContent = `${fanRange.value}%`;
    });
    fanRange?.addEventListener("change", async () => {
      const targetVal = Number(fanRange.value);
      state.isDraggingRange = false;
      state.dragCooldownUntil = Date.now() + 250;
      await applyDirectFanPct(targetVal);
    });
    fanRange?.addEventListener("pointerup", endFanDrag);
    fanRange?.addEventListener("touchend", endFanDrag);
    document.getElementById("btnFanPctDec")?.addEventListener("click", () => applyDirectFanPct((state.snapshot?.settings?.manualFanPercent || 55) - 5));
    document.getElementById("btnFanPctInc")?.addEventListener("click", () => applyDirectFanPct((state.snapshot?.settings?.manualFanPercent || 55) + 5));
    document.querySelectorAll("[data-fan-quick-pct]").forEach((el) => {
      el.addEventListener("click", () => applyDirectFanPct(Number(el.dataset.fanQuickPct)));
    });

    document.querySelectorAll("[data-select-fan-curve]").forEach((el) => {
      el.addEventListener("click", async () => {
        try {
          const curveId = el.dataset.selectFanCurve;
          const res = await rpc("setFanMode", { mode: "curve", curveId });
          showToast(res?.message || "已应用所选风扇温度曲线", res?.hardwareApplied ? "success" : "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    document.getElementById("btnAddFanCurve")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings || {};
      const curves = (s.fanCurves && s.fanCurves.length ? s.fanCurves : getDefaultFanCurves()).map((c) => ({
        ...c,
        points: (c.points || []).map((p) => ({ ...p }))
      }));
      const base = getActiveFanCurve(s);
      const newId = `custom_${Date.now().toString(36).slice(-4)}`;
      const nextCurve = {
        id: newId,
        name: `自定义曲线 ${curves.length + 1}`,
        points: (base.points || []).map((p) => ({ ...p }))
      };
      curves.push(nextCurve);
      await rpc("saveFanCurves", { curves, activeCurveId: newId, activate: true });
      showToast(`已新建并启用「${nextCurve.name}」`, "success");
    });

    document.getElementById("btnDelFanCurve")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings || {};
      const curves = (s.fanCurves || []).filter((c) => c.id !== s.activeFanCurveId);
      if (curves.length === 0) return;
      await rpc("saveFanCurves", { curves, activeCurveId: curves[0].id, activate: s.fanMode === "curve" });
      showToast("已删除当前风扇曲线", "info");
    });

    document.querySelectorAll("[data-curve-node-adj]").forEach((el) => {
      el.addEventListener("click", async () => {
        const [idxStr, field, deltaStr] = el.dataset.curveNodeAdj.split(":");
        const idx = Number(idxStr);
        const delta = Number(deltaStr);
        const s = state.snapshot?.settings || {};
        const curves = (s.fanCurves && s.fanCurves.length ? s.fanCurves : getDefaultFanCurves()).map((c) => ({
          ...c,
          points: (c.points || []).map((p) => ({ ...p }))
        }));
        const cur = curves.find((c) => c.id === s.activeFanCurveId) || curves[0];
        if (!cur || !cur.points[idx]) return;
        if (field === "tempC") {
          cur.points[idx].tempC = Math.max(25, Math.min(95, cur.points[idx].tempC + delta));
        } else {
          cur.points[idx].fanPercent = Math.max(20, Math.min(100, cur.points[idx].fanPercent + delta));
        }
        cur.points.sort((a, b) => a.tempC - b.tempC);
        await rpc("saveFanCurves", { curves, activeCurveId: cur.id, activate: false });
      });
    });

    document.getElementById("btnTogglePowerAdv")?.addEventListener("click", () => {
      state.powerAdvancedOpen = !state.powerAdvancedOpen;
      render();
    });
    document.getElementById("btnToggleCurveNodes")?.addEventListener("click", () => {
      state.fanNodesOpen = !state.fanNodesOpen;
      render();
    });
    document.getElementById("btnToggleCpuAdv")?.addEventListener("click", () => {
      state.cpuAdvancedOpen = !state.cpuAdvancedOpen;
      render();
    });

    document.querySelectorAll("[data-ui-layout]").forEach((el) => {
      el.addEventListener("click", async () => {
        const layoutId = el.dataset.uiLayout === "cockpit" ? "cockpit" : "bento";
        syncLayoutDom(layoutId);
        if (state.snapshot?.settings) state.snapshot.settings.uiLayout = layoutId;
        render();
        await rpc("setUiLayout", { layout: layoutId });
        const meta = UI_LAYOUTS.find((l) => l.id === layoutId);
        showToast(`已切换布局风格：${meta?.label || layoutId}`, "info");
      });
    });

    document.querySelectorAll("[data-ui-theme]").forEach((el) => {
      el.addEventListener("click", async () => {
        const themeId = el.dataset.uiTheme || "aurora";
        syncThemeDom(themeId);
        if (state.snapshot?.settings) state.snapshot.settings.uiTheme = themeId;
        render();
        await rpc("setUiTheme", { theme: themeId });
        const meta = UI_THEMES.find((t) => t.id === themeId);
        showToast(`已切换界面皮肤：${meta?.label || themeId}`, "info");
      });
    });

    document.getElementById("btnInstallHwSvc")?.addEventListener("click", async () => {
      try {
        showToast("正在请求一次性管理员授权并检查 PawnIO 驱动...");
        const res = await rpc("installHardwareService", {});
        showToast(res?.message || "底层硬件服务已启用", res?.ok === false ? "warn" : "success");
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.getElementById("btnOpenPawnIo")?.addEventListener("click", async () => {
      try {
        await rpc("openPawnIoDownload", {});
        showToast("已打开官方 PawnIO 驱动下载页，安装后重启电脑即可直控功耗", "info");
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

    const patchSteadyEco = async (patch, showFeedback = true) => {
      try {
        const res = await rpc("setSteadyEco", patch);
        if (showFeedback && res?.message) {
          showToast(res.message, res.steadyEcoActive ? "success" : "info");
        }
      } catch (err) {
        showToast(err.message, "error");
      }
    };

    document.querySelectorAll("[data-steady-mode]").forEach((el) => {
      el.addEventListener("click", () => {
        patchSteadyEco({ mode: el.dataset.steadyMode }, true);
      });
    });

    document.querySelectorAll("[data-steady-epp]").forEach((el) => {
      el.addEventListener("click", () => {
        const epp = Number(el.dataset.steadyEpp);
        patchSteadyEco({ cpuEpp: epp }, false);
        showToast(`稳帧净空 EPP 能效偏置已设为 ${epp}`, "info");
      });
    });

    document.getElementById("swSteadyCpu")?.addEventListener("click", () => {
      const s = state.snapshot?.settings || {};
      patchSteadyEco({ cpuEnabled: !(s.steadyEcoCpuEnabled !== false) }, false);
    });
    document.getElementById("swSteadyBg")?.addEventListener("click", () => {
      const s = state.snapshot?.settings || {};
      patchSteadyEco({ backgroundEnabled: !(s.steadyEcoBackgroundEnabled !== false) }, false);
    });
    document.getElementById("swSteadyPer")?.addEventListener("click", () => {
      const s = state.snapshot?.settings || {};
      patchSteadyEco({ peripheralEnabled: !(s.steadyEcoPeripheralEnabled !== false) }, false);
    });

    document.querySelectorAll("[data-steady-sub]").forEach((el) => {
      el.addEventListener("click", () => {
        const key = el.dataset.steadySub;
        const s = state.snapshot?.settings || {};
        if (key === "cpuEnabled") {
          patchSteadyEco({ cpuEnabled: !(s.steadyEcoCpuEnabled !== false) }, false);
        } else if (key === "backgroundEnabled") {
          patchSteadyEco({ backgroundEnabled: !(s.steadyEcoBackgroundEnabled !== false) }, false);
        } else if (key === "peripheralEnabled") {
          patchSteadyEco({ peripheralEnabled: !(s.steadyEcoPeripheralEnabled !== false) }, false);
        }
      });
    });

    document.getElementById("btnToggleOfflineWifi")?.addEventListener("click", async () => {
      try {
        const res = await rpc("toggleOfflineWifi", {});
        showToast(res?.message || "已切换离线 Wi-Fi 状态", res?.wifiRadioOff ? "success" : "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });

    document.querySelectorAll("[data-toolbox]").forEach((el) => {
      el.addEventListener("click", async () => {
        try {
          const res = await rpc("runToolboxAction", { action: el.dataset.toolbox, fromQam: IS_QAM });
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

    // Per-game performance profile (adjusting Watts or Hz automatically enables & persists the profile)
    const resolveGamePerfTarget = () => (IS_QAM || state.tab === "handheld") ? getRunningGame() : getActiveGame();
    const saveGamePerf = async (patch) => {
      const active = resolveGamePerfTarget();
      if (!active) return;
      const curMap = state.snapshot?.settings?.perGameProfiles || {};
      const existing = curMap[active.id] || {
        gameId: active.id,
        gameName: active.title,
        enabled: false,
        tdpWatts: state.snapshot?.settings?.currentTdpWatts || 25,
        refreshRateHz: 0,
        cpuMaxFreqMhz: 0
      };
      await rpc("saveGamePerfProfile", { ...existing, ...patch, gameId: active.id, gameName: active.title });
    };
    document.getElementById("swGamePerfEnabled")?.addEventListener("click", () => {
      const active = resolveGamePerfTarget();
      const cur = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.enabled;
      saveGamePerf({ enabled: !cur });
    });
    document.getElementById("btnGamePerfTdpDec")?.addEventListener("click", () => {
      const active = resolveGamePerfTarget();
      const curW = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.tdpWatts || state.snapshot?.settings?.currentTdpWatts || 40;
      saveGamePerf({ enabled: true, tdpWatts: Math.max(8, curW - 1) });
    });
    document.getElementById("btnGamePerfTdpInc")?.addEventListener("click", () => {
      const active = resolveGamePerfTarget();
      const curW = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.tdpWatts || state.snapshot?.settings?.currentTdpWatts || 40;
      saveGamePerf({ enabled: true, tdpWatts: Math.min(120, curW + 1) });
    });
    document.querySelectorAll("[data-game-perf-hz]").forEach((el) => {
      el.addEventListener("click", () => saveGamePerf({ enabled: true, refreshRateHz: Number(el.dataset.gamePerfHz) }));
    });
    document.getElementById("btnHomeGoOpti")?.addEventListener("click", () => {
      state.tab = "opti";
      state.optiSubTab = "tune";
      state.focusIndex = 0;
      render();
    });

    // Monitor & OSD events
    document.getElementById("btnCycleOsdPos")?.addEventListener("click", async () => {
      const order = ["top-left", "top-center", "top-right"];
      const cur = state.snapshot?.settings?.overlayPosition || "top-center";
      const next = order[(order.indexOf(cur) + 1) % order.length];
      await rpc("setOverlayHud", { position: next });
    });
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
    document.querySelectorAll("[data-osd-renderer]").forEach((el) => {
      el.addEventListener("click", async () => {
        const mode = el.dataset.osdRenderer;
        const sysNow = state.snapshot?.systemControl || {};
        if (mode !== "native" && !sysNow.rtssRunning) {
          showToast(sysNow.rtssInstalled
            ? "检测到已安装 RTSS，正在自动启动并接入共享内存..."
            : "未检测到 RTSS，正在自动下载安装并启动（请留意系统授权弹窗）...", "info");
        }
        try {
          const res = await rpc("setOverlayHud", { renderer: mode, autoPrepareRtss: true });
          if (mode === "native") {
            showToast("OSD 已切换为原生悬浮窗模式", "info");
          } else if (res?.rtssRunning) {
            const prefix = mode === "auto_rtss"
              ? "智能回退已就绪（独占全屏自动切 RTSS）"
              : "RTSS 共享内存穿透已启用";
            showToast(res?.launched || res?.installedNow ? `${res.message}` : prefix, "success");
          } else {
            showToast(res?.message || "未检测到 RTSS 运行，暂以原生悬浮窗兜底", "warn");
          }
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });
    document.getElementById("btnEnsureRtss")?.addEventListener("click", async () => {
      const sysNow = state.snapshot?.systemControl || {};
      showToast(sysNow.rtssInstalled
        ? "正在启动本机 RTSS..."
        : "正在通过 winget 自动安装并启动 RTSS（请留意系统授权弹窗）...", "info");
      try {
        const res = await rpc("ensureRtssReady", {});
        showToast(res?.message || (res?.rtssRunning ? "RTSS 已启动就绪" : "RTSS 暂未就绪"), res?.rtssRunning ? "success" : "warn");
      } catch (err) {
        showToast(err.message, "error");
      }
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
        showToast(res?.statusMessage || "已刷新 GPD 手柄配置", res?.connected ? "success" : "warn");
        render();
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.getElementById("btnPickL4")?.addEventListener("click", () => openBackKeyPickerModal("l4"));
    document.getElementById("btnPickR4")?.addEventListener("click", () => openBackKeyPickerModal("r4"));
    document.getElementById("btnToggleControlsAdv")?.addEventListener("click", () => {
      state.controlsAdvancedOpen = !state.controlsAdvancedOpen;
      render();
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
    document.getElementById("swInterceptOsk")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = !(s.interceptPhysicalOskButton !== false);
      await rpc("saveSettings", { ...s, interceptPhysicalOskButton: next });
      showToast(next ? "已开启机身键盘键 (Ctrl+Win+O) 接管" : "已恢复系统默认键盘键响应", "info");
    });
    document.getElementById("swMuteGameGamepad")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = !s.muteGameGamepadInOverlay;
      await rpc("saveSettings", { ...s, muteGameGamepadInOverlay: next });
      showToast(next ? "已开启侧栏期间屏蔽游戏后台手柄输入" : "已关闭游戏后台手柄屏蔽（更兼容《剑星》等游戏）", "info");
    });
    document.getElementById("swInterceptMenuBtn")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = s.interceptPhysicalMenuButton === false;
      await rpc("saveSettings", { ...s, interceptPhysicalMenuButton: next });
      showToast(next ? "已开启机身快捷菜单键 (Vendor HID / F13) 呼出侧栏" : "已释放机身快捷菜单键 (Vendor HID / F13)", "info");
    });
    document.getElementById("swInterceptGuideBtn")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = s.interceptGuideMenuButton === false;
      await rpc("saveSettings", { ...s, interceptGuideMenuButton: next });
      showToast(next ? "已开启手柄菜单 / Guide 键 (0x0400) 单独短按呼出侧栏" : "已关闭手柄 Guide 键 (0x0400) 接管", "info");
    });
    document.getElementById("btnMenuKeyDiag")?.addEventListener("click", async () => {
      if (state.menuKeyDiagRunning) return;
      state.menuKeyDiagRunning = true;
      state.menuKeyDiagSecLeft = 5;
      state.menuKeyDiagReport = null;
      render();
      showToast("开始 5 秒抓包：请立即短按一次机身快捷菜单键", "info");
      const timer = setInterval(() => {
        if (!state.menuKeyDiagRunning) {
          clearInterval(timer);
          return;
        }
        state.menuKeyDiagSecLeft = Math.max(1, (state.menuKeyDiagSecLeft || 1) - 1);
        const btn = document.getElementById("btnMenuKeyDiag");
        if (btn) btn.textContent = `抓包中 (${state.menuKeyDiagSecLeft}s)... 请按菜单键`;
        const ttl = document.querySelector("#menuKeyDiagPanel .diag-capture-title");
        if (ttl) ttl.textContent = `正在监听实体按键原始信号（剩余 ${state.menuKeyDiagSecLeft}s）...`;
      }, 1000);
      try {
        const rep = await rpc("runMenuKeyDiagnostic", { durationMs: 5000 });
        state.menuKeyDiagReport = rep;
        const cnt = rep?.events?.length ?? 0;
        showToast(cnt > 0 ? `抓包完成：共捕获 ${cnt} 条硬件信号` : "抓包完成：5 秒内未检测到信号，请查看状态或复制报告", cnt > 0 ? "success" : "warn");
      } catch (err) {
        showToast(err.message || "信号抓包诊断失败", "error");
      } finally {
        clearInterval(timer);
        state.menuKeyDiagRunning = false;
        state.menuKeyDiagSecLeft = 0;
        render();
      }
    });
    document.getElementById("btnCopyMenuKeyDiag")?.addEventListener("click", async () => {
      const txt = state.menuKeyDiagReport?.copyableReport;
      if (!txt) return;
      try {
        if (navigator.clipboard?.writeText) {
          await navigator.clipboard.writeText(txt);
        } else {
          const ta = document.createElement("textarea");
          ta.value = txt;
          ta.style.position = "fixed";
          ta.style.opacity = "0";
          document.body.appendChild(ta);
          ta.select();
          document.execCommand("copy");
          ta.remove();
        }
        showToast("已复制实体按键诊断报告到剪贴板", "success");
      } catch (err) {
        showToast(`复制失败: ${err.message || err}`, "error");
      }
    });
    document.getElementById("btnClearMenuKeyDiag")?.addEventListener("click", () => {
      state.menuKeyDiagReport = null;
      render();
    });
    document.getElementById("btnTogglePrereleaseCheck")?.addEventListener("click", () => {
      state.appUpdateIncludePrerelease = !state.appUpdateIncludePrerelease;
      render();
    });
    document.getElementById("btnCheckAppUpdate")?.addEventListener("click", async () => {
      if (state.appUpdateChecking) return;
      state.appUpdateChecking = true;
      render();
      try {
        const res = await rpc("checkAppUpdate", { includePrerelease: !!state.appUpdateIncludePrerelease });
        showToast(res?.message || "检查更新完成", res?.update?.hasUpdate ? "success" : "info");
      } catch (err) {
        showToast(err.message || "检查更新失败", "error");
      } finally {
        state.appUpdateChecking = false;
        render();
      }
    });
    document.getElementById("btnApplyAppUpdate")?.addEventListener("click", async () => {
      if (state.appUpdateProgress) return;
      try {
        const res = await rpc("downloadAndApplyAppUpdate", { includePrerelease: !!state.appUpdateIncludePrerelease });
        showToast(res?.message || "正在重启并应用更新...", res?.ok ? "success" : "warn");
      } catch (err) {
        state.appUpdateProgress = null;
        showToast(err.message || "下载或应用更新失败", "error");
        render();
      }
    });
    document.getElementById("btnOpenUpdateNotes")?.addEventListener("click", async () => {
      const url = state.snapshot?.latestUpdate?.htmlUrl || "https://github.com/Lokilankaaa/HaloDeck/releases";
      try {
        await rpc("openExternalUrl", { url });
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.getElementById("swBackButtonHotkeys")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = !s.enableBackButtonHotkeys;
      await rpc("saveSettings", { ...s, enableBackButtonHotkeys: next });
      showToast(next ? "已开启背键 F14–F16 快捷呼出接管" : "已释放背键 F14–F16，游戏可直接使用原生背键", "info");
    });
    document.getElementById("btnResetGamepadCombos")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      if (state.recordingTarget) {
        state.recordingTarget = null;
        await rpc("cancelRecordGamepadCombo");
      }
      await rpc("saveSettings", {
        ...s,
        consoleGamepadCombo: { buttons: ["BACK", "LB"] },
        qamGamepadCombo: { buttons: ["BACK", "RB"] },
        osdGamepadCombo: { buttons: ["BACK", "Y"] },
        insertGamepadCombo: { buttons: ["BACK", "START"] },
        consoleHotkey: { modifiers: 6, virtualKey: 0x4F, displayText: "Ctrl + Shift + O" },
        qamHotkey: { modifiers: 3, virtualKey: 0x4F, displayText: "Ctrl + Alt + O" },
        osdHotkey: { modifiers: 3, virtualKey: 0x4D, displayText: "Ctrl + Alt + M" },
        insertHotkey: { modifiers: 3, virtualKey: 0x49, displayText: "Ctrl + Alt + I" }
      });
      showToast("已恢复默认手柄组合键（Select+LB 主界面 / Select+RB 侧栏）与键盘热键", "success");
    });
    document.getElementById("btnTestOsk")?.addEventListener("click", async () => {
      try {
        const res = await rpc("runToolboxAction", { action: "osk", fromQam: IS_QAM });
        showToast(res?.message || "已切换屏幕键盘", "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.getElementById("btnQamToConsoleControls")?.addEventListener("click", () => {
      rpc("showConsole", { tab: "controls" });
    });
    document.getElementById("btnResetGpdFactory")?.addEventListener("click", async () => {
      if (state.gpdSaving) return;
      state.gpdSaving = true;
      render();
      try {
        const res = await rpc("saveGpdController", {
          l4Key: 0,
          r4Key: 0,
          l4Combo: null,
          r4Combo: null,
          rumble: 1,
          leftDeadzone: 0,
          rightDeadzone: 0
        });
        if (res && state.snapshot) {
          state.snapshot.gpdController = res;
          syncGpdDraft(res);
        }
        showToast("已恢复背键原厂默认（L4/R4 清空为 NC，震动=弱，死区=0）", "success");
      } catch (err) {
        showToast(err.message, "error");
      } finally {
        state.gpdSaving = false;
        render();
      }
    });
    document.getElementById("btnSaveGpd")?.addEventListener("click", async () => {
      if (state.gpdSaving) return;
      state.gpdSaving = true;
      render();
      try {
        const l4Combo = Array.isArray(state.gpdDraft.l4Combo) ? normalizeComboCodesList(state.gpdDraft.l4Combo) : [];
        const r4Combo = Array.isArray(state.gpdDraft.r4Combo) ? normalizeComboCodesList(state.gpdDraft.r4Combo) : [];
        const l4Single = l4Combo.length === 1 ? l4Combo[0] : (l4Combo.length >= 2 ? null : (state.gpdDraft.l4Key >= 0 ? state.gpdDraft.l4Key : null));
        const r4Single = r4Combo.length === 1 ? r4Combo[0] : (r4Combo.length >= 2 ? null : (state.gpdDraft.r4Key >= 0 ? state.gpdDraft.r4Key : null));
        const res = await rpc("saveGpdController", {
          l4Key: l4Single,
          r4Key: r4Single,
          l4Combo: l4Combo.length >= 2 ? l4Combo : null,
          r4Combo: r4Combo.length >= 2 ? r4Combo : null,
          rumble: state.gpdDraft.rumble,
          leftDeadzone: state.gpdDraft.leftDeadzone,
          rightDeadzone: state.gpdDraft.rightDeadzone
        });
        if (res && state.snapshot) {
          state.snapshot.gpdController = res;
          syncGpdDraft(res);
        }
        showToast(res?.statusMessage || "已应用背键与震动到手柄", "success");
      } catch (err) {
        showToast(err.message, "error");
      } finally {
        state.gpdSaving = false;
        render();
      }
    });

    // Hotkeys & Settings
    document.getElementById("btnRecordConsoleCombo")?.addEventListener("click", () => startRecordCombo("console"));
    document.getElementById("btnRecordQamCombo")?.addEventListener("click", () => startRecordCombo("qam"));
    document.getElementById("btnRecordOsdCombo")?.addEventListener("click", () => startRecordCombo("osd"));
    document.getElementById("btnRecordInsCombo")?.addEventListener("click", () => startRecordCombo("insert"));
    document.getElementById("btnClearConsoleCombo")?.addEventListener("click", () => clearGamepadCombo("console"));
    document.getElementById("btnClearQamCombo")?.addEventListener("click", () => clearGamepadCombo("qam"));
    document.getElementById("btnClearOsdCombo")?.addEventListener("click", () => clearGamepadCombo("osd"));
    document.getElementById("btnClearInsCombo")?.addEventListener("click", () => clearGamepadCombo("insert"));

    document.getElementById("btnHotkeyConsole")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 6, virtualKey: 0x4F, displayText: "Ctrl + Shift + O" },
        { modifiers: 3, virtualKey: 0x48, displayText: "Ctrl + Alt + H" },
        { modifiers: 0, virtualKey: 0x7B, displayText: "F12" },
        { modifiers: 0, virtualKey: 0, displayText: "已关闭" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.consoleHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, consoleHotkey: next });
      showToast(next.virtualKey === 0 ? "已取消前端主界面键盘热键" : `前端主界面键盘热键已切换为 ${next.displayText}`, next.virtualKey === 0 ? "info" : "success");
    });

    document.getElementById("btnHotkeyQam")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 3, virtualKey: 0x4F, displayText: "Ctrl + Alt + O" },
        { modifiers: 6, virtualKey: 0x4F, displayText: "Ctrl + Shift + O" },
        { modifiers: 3, virtualKey: 0x51, displayText: "Ctrl + Alt + Q" },
        { modifiers: 0, virtualKey: 0, displayText: "已关闭" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.qamHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, qamHotkey: next });
      showToast(next.virtualKey === 0 ? "已取消快捷侧栏键盘热键" : `QAM 键盘热键已切换为 ${next.displayText}`, next.virtualKey === 0 ? "info" : "success");
    });

    document.getElementById("btnHotkeyOsd")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 3, virtualKey: 0x4D, displayText: "Ctrl + Alt + M" },
        { modifiers: 6, virtualKey: 0x4D, displayText: "Ctrl + Shift + M" },
        { modifiers: 0, virtualKey: 0x79, displayText: "F10" },
        { modifiers: 0, virtualKey: 0, displayText: "已关闭" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.osdHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, osdHotkey: next });
      showToast(next.virtualKey === 0 ? "已取消 OSD 键盘热键" : `OSD 键盘热键已切换为 ${next.displayText}`, next.virtualKey === 0 ? "info" : "success");
    });

    document.getElementById("btnHotkeyIns")?.addEventListener("click", async () => {
      const choices = [
        { modifiers: 3, virtualKey: 0x49, displayText: "Ctrl + Alt + I" },
        { modifiers: 6, virtualKey: 0x49, displayText: "Ctrl + Shift + I" },
        { modifiers: 0, virtualKey: 0x7A, displayText: "F11" },
        { modifiers: 0, virtualKey: 0, displayText: "已关闭" }
      ];
      const s = state.snapshot.settings;
      const curIdx = choices.findIndex((c) => c.displayText === s.insertHotkey?.displayText);
      const next = choices[(curIdx + 1) % choices.length];
      await rpc("saveSettings", { ...s, insertHotkey: next });
      showToast(next.virtualKey === 0 ? "已取消菜单键盘热键" : `菜单键盘热键已切换为 ${next.displayText}`, next.virtualKey === 0 ? "info" : "success");
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
    document.getElementById("swRunAsAdmin")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = !s.runAsAdministrator;
      await rpc("saveSettings", { ...s, runAsAdministrator: next });
      if (next && !state.snapshot?.isElevated) {
        showToast("已开启默认管理员权限运行；点击「立即提权重启」即可立即生效", "info");
      } else {
        showToast(next ? "已开启默认管理员权限运行（开机自启将自动使用免 UAC 计划任务）" : "已关闭默认管理员权限运行，下次启动恢复普通权限", "info");
      }
    });
    document.getElementById("btnRelaunchAdmin")?.addEventListener("click", async () => {
      try {
        // On-demand single-session elevation (persist: false) so normal launches remain non-elevated and Steam Big Picture friendly.
        const res = await rpc("relaunchAsAdmin", { persist: false });
        if (res?.message) showToast(res.message, res.ok ? "success" : "warn");
      } catch (err) {
        showToast(err.message, "error");
      }
    });
    document.getElementById("btnRunAutoInject")?.addEventListener("click", async () => {
      const ev = await rpc("runAutoInjectNow");
      showToast(`后台自动注入检查完成（处理 ${ev?.length || 0} 项）`, "success");
    });

    // Display / Audio / CPU system controls (used across Handheld & QAM)
    document.querySelectorAll("[data-refresh-hz]").forEach((el) => {
      el.addEventListener("click", () => {
        const raw = el.dataset.refreshHz;
        if (raw === "drr") {
          const ceilingHz = state.snapshot?.systemControl?.dynamicCeilingHz || 120;
          const baseHz = Math.round(ceilingHz / 2);
          applySys("setRefreshRate", { hz: ceilingHz, dynamic: true }, `已开启系统动态刷新率 (${baseHz}–${ceilingHz}Hz · VRR 自适应)`);
          return;
        }
        const hz = Number(raw);
        const supportsDrr = !!(state.snapshot?.systemControl?.dynamicRefreshSupported);
        const toastMsg = supportsDrr && hz >= 100
          ? `已锁定固定 ${hz}Hz（点选「动态」可恢复系统 DRR）`
          : `屏幕刷新率已切换为 ${hz}Hz`;
        applySys("setRefreshRate", { hz, dynamic: false }, toastMsg);
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
    const setBrightnessSmooth = (pct, immediateFlush = false) => {
      const clamped = Math.max(5, Math.min(100, Math.round(pct)));
      updateBrightnessUiImmediate(clamped);
      scheduleCoalescedControl("bright", () => rpc("setBrightness", { percent: clamped }), immediateFlush ? 0 : 35, immediateFlush);
    };
    const setVolumeSmooth = (pct, immediateFlush = false) => {
      const clamped = Math.max(0, Math.min(100, Math.round(pct)));
      updateVolumeUiImmediate(clamped, false);
      scheduleCoalescedControl("vol", () => rpc("setVolume", { percent: clamped }), immediateFlush ? 0 : 35, immediateFlush);
    };

    const brightRange = document.getElementById("brightRange");
    brightRange?.addEventListener("input", () => setBrightnessSmooth(Number(brightRange.value), false));
    brightRange?.addEventListener("change", () => setBrightnessSmooth(Number(brightRange.value), true));
    document.getElementById("brightDec")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.brightnessPercent ?? 70;
      setBrightnessSmooth(cur - 5, true);
    });
    document.getElementById("brightInc")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.brightnessPercent ?? 70;
      setBrightnessSmooth(cur + 5, true);
    });

    const volRange = document.getElementById("volRange");
    volRange?.addEventListener("input", () => setVolumeSmooth(Number(volRange.value), false));
    volRange?.addEventListener("change", () => setVolumeSmooth(Number(volRange.value), true));
    document.getElementById("volDec")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.volumePercent ?? 50;
      setVolumeSmooth(cur - 5, true);
    });
    document.getElementById("volInc")?.addEventListener("click", () => {
      const cur = state.snapshot?.systemControl?.volumePercent ?? 50;
      setVolumeSmooth(cur + 5, true);
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
    document.querySelectorAll("[data-xefg-mult]").forEach((el) => {
      el.addEventListener("click", () => {
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        state.config.xeFgMultiplier = Math.min(4, Math.max(2, parseInt(el.dataset.xefgMult, 10) || 2));
        render();
      });
    });
    document.getElementById("swXeFgDmfg")?.addEventListener("click", () => {
      state.selectedPresetId = "custom";
      state._userModifiedConfig = true;
      state.config.xeFgDynamicMfg = !state.config.xeFgDynamicMfg;
      render();
    });
    document.querySelectorAll("[data-xefg-dmfg-fps]").forEach((el) => {
      el.addEventListener("click", () => {
        state.selectedPresetId = "custom";
        state._userModifiedConfig = true;
        const val = parseInt(el.dataset.xefgDmfgFps, 10) || 0;
        state.config.xeFgDynamicTargetFps = val >= 10 ? val : 0;
        render();
      });
    });
    document.getElementById("btnGoMfgUnlockerComp")?.addEventListener("click", () => {
      state.tab = "opti";
      state.optiSubTab = "components";
      state.focusIndex = 0;
      render();
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
          state.qamOptiSubView = "tune";
          state.focusIndex = 0;
        }
        syncConfigFromActiveGame(state.snapshot);
        render();
      });
    });

    document.getElementById("btnOpenConsoleGamePicker")?.addEventListener("click", () => {
      state.tab = "opti";
      state.optiSubTab = "library";
      state.focusIndex = 0;
      render();
    });

    document.getElementById("btnLibBackToTune")?.addEventListener("click", () => {
      state.tab = "opti";
      state.optiSubTab = "tune";
      state.focusIndex = 0;
      render();
    });

    document.querySelectorAll("[data-lib-layout]").forEach((el) => {
      el.addEventListener("click", () => {
        state.libraryLayout = el.dataset.libLayout || "posters";
        state.focusIndex = 0;
        render();
      });
    });

    document.getElementById("btnQamOpenGamePicker")?.addEventListener("click", () => {
      state.qamOptiSubView = "games";
      state.focusIndex = 0;
      render();
    });

    document.getElementById("btnQamBackToTune")?.addEventListener("click", () => {
      state.qamOptiSubView = "tune";
      state.focusIndex = 0;
      render();
    });

    document.getElementById("btnQamRescan")?.addEventListener("click", async () => {
      showToast("正在扫描已安装游戏...");
      await rpc("scanGames");
      showToast("游戏列表已刷新", "success");
    });

    document.getElementById("btnInject")?.addEventListener("click", () => executeInject(false));
    document.getElementById("btnInjectRestart")?.addEventListener("click", () => executeInject(true));
    document.getElementById("btnRestore")?.addEventListener("click", () => executeRestore());
    document.getElementById("btnSendInsert")?.addEventListener("click", () => rpc("sendInsertKey"));
    document.getElementById("btnToggleLiveFg")?.addEventListener("click", () => rpc("toggleLiveFg"));

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

  const activeRecordKeySet = new Set();
  let burstRecordKeySet = new Set();

  function cleanupBackModal() {
    const wasRecording = state.backModal.recordingActive || state.backModal.tab === "record";
    state.backModal.open = false;
    state.backModal.recordingActive = false;
    activeRecordKeySet.clear();
    burstRecordKeySet.clear();
    window.__hoRefreshBackModal = null;
    if (wasRecording) {
      rpc("cancelRecordBackButton").catch(() => {});
    }
    document.getElementById("modalBackdrop")?.classList.add("hidden");
  }

  function applyBackSlotSingle(slot, singleCode) {
    if (slot === "l4") {
      state.gpdDraft.l4Key = singleCode;
      state.gpdDraft.l4Combo = [];
    } else {
      state.gpdDraft.r4Key = singleCode;
      state.gpdDraft.r4Combo = [];
    }
    cleanupBackModal();
    render();
    applySpatialFocus();
  }

  function applyBackSlotCombo(slot, codes) {
    const norm = normalizeComboCodesList(codes);
    if (norm.length === 1) {
      applyBackSlotSingle(slot, norm[0]);
      return;
    }
    if (norm.length >= 2) {
      if (slot === "l4") {
        state.gpdDraft.l4Key = 0;
        state.gpdDraft.l4Combo = norm;
      } else {
        state.gpdDraft.r4Key = 0;
        state.gpdDraft.r4Combo = norm;
      }
      cleanupBackModal();
      render();
      applySpatialFocus();
    }
  }

  function openBackKeyPickerModal(slot) {
    const slotTitle = slot === "l4" ? "L4 左背键" : "R4 右背键";
    const curSingle = slot === "l4" ? state.gpdDraft.l4Key : state.gpdDraft.r4Key;
    const curCombo = slot === "l4" ? [...(state.gpdDraft.l4Combo || [])] : [...(state.gpdDraft.r4Combo || [])];
    const curDesc = resolveBackSlotLabel(curSingle, curCombo);

    activeRecordKeySet.clear();
    burstRecordKeySet.clear();
    state.backModal = {
      open: true,
      slot,
      tab: "single",
      recordingActive: false,
      recordedCodes: curCombo.length ? [...curCombo] : [],
      comboCodes: curCombo.length ? [...curCombo] : (curSingle > 0 ? [curSingle] : [])
    };

    document.getElementById("modalTitle").textContent = `配置${slotTitle}映射`;
    document.getElementById("modalSubtitle").textContent = `当前草稿：${curDesc}（选中后请点击「应用到手柄」写入 Flash）`;
    const body = document.getElementById("modalBody");

    const startLiveRecording = () => {
      activeRecordKeySet.clear();
      burstRecordKeySet.clear();
      state.backModal.recordingActive = true;
      state.backModal.recordedCodes = [];
      rpc("beginRecordBackButton").catch(() => {});
      renderModalContent();
    };

    const stopLiveRecording = () => {
      state.backModal.recordingActive = false;
      activeRecordKeySet.clear();
      rpc("cancelRecordBackButton").catch(() => {});
      renderModalContent();
    };

    const renderModalContent = () => {
      const bm = state.backModal;
      const singleSelectedVal = curCombo.length >= 2 ? -999 : curSingle;
      const tabsHtml = `
        <div class="backkey-modal-tabs">
          <button class="pill-btn small ${bm.tab === "single" ? "active" : ""}" data-back-modal-tab="single" data-focusable="true">单键点选</button>
          <button class="pill-btn small ${bm.tab === "record" ? "active" : ""}" data-back-modal-tab="record" data-focusable="true">录制按键 / 组合键</button>
          <button class="pill-btn small ${bm.tab === "combo" ? "active" : ""}" data-back-modal-tab="combo" data-focusable="true">手动组合键</button>
        </div>
      `;

      let tabBody = "";
      if (bm.tab === "single") {
        tabBody = `
          <div class="backkey-picker-section">
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:4px;">
              <div class="gpd-key-group-label" style="margin:0;">手柄按键</div>
              <button class="pill-btn small ${singleSelectedVal === 0 ? "active" : ""}" data-pick-back-key="0" data-pick-slot="${slot}" data-focusable="true">恢复原厂 / 清空背键 (NC)</button>
            </div>
            <div class="backkey-picker-grid">
              ${GPD_BACK_GAMEPAD_OPTIONS.map((opt) => `
                <button class="pill-btn small ${singleSelectedVal === opt.value ? "active" : ""}" data-pick-back-key="${opt.value}" data-pick-slot="${slot}" data-focusable="true">${esc(opt.label)}</button>
              `).join("")}
            </div>
          </div>
          ${GPD_FULL_KEYBOARD_GROUPS.map((grp) => `
            <div class="backkey-picker-section">
              <div class="gpd-key-group-label">${esc(grp.title)}</div>
              <div class="full-kb-grid">
                ${grp.keys.map((opt) => `
                  <button class="pill-btn small kb-key-btn ${opt.special ? "is-special-fn" : ""} ${singleSelectedVal === opt.value ? "active" : ""}" data-pick-back-key="${opt.value}" data-pick-slot="${slot}" data-focusable="true">${esc(opt.label)}</button>
                `).join("")}
              </div>
            </div>
          `).join("")}
        `;
      } else if (bm.tab === "record") {
        const recChips = bm.recordedCodes.length
          ? bm.recordedCodes.map((c) => `<span class="combo-chip-pill">${esc(resolveShortHidLabel(c))}</span>`).join(`<span class="keycap-plus">+</span>`)
          : `<span style="font-size:12px;color:var(--text-muted);">${bm.recordingActive ? "等待按键中：可直接同时按下手柄键或键盘组合键..." : "点击下方「开始录制」，按下单键或最多 4 个组合键后松开"}</span>`;
        tabBody = `
          <div class="backkey-picker-section">
            <div class="gpd-key-group-label">实时录制手柄组合或键盘快捷键（最多 4 键）</div>
            <div id="recordStageBox" class="record-stage-box ${bm.recordingActive ? "recording" : ""}">
              <div class="combo-preview-bar">${recChips}</div>
              <div style="font-size:11px;color:${bm.recordingActive ? "#8dc7ff" : "var(--text-muted)"};">
                ${bm.recordingActive ? "● 正在监听物理手柄与键盘输入（按下全部目标键后松开即可自动完成）" : (bm.recordedCodes.length ? `已捕获 ${bm.recordedCodes.length} 键，点击右侧按钮确认使用` : "支持手柄按键/扳机与全键盘按键自由录制")}
              </div>
            </div>
            <div style="display:flex;gap:8px;justify-content:flex-end;flex-wrap:wrap;margin-top:6px;">
              <button id="btnToggleBackRecord" class="btn btn-sm ${bm.recordingActive ? "btn-armed-danger" : ""}" data-focusable="true">${bm.recordingActive ? "停止录制" : (bm.recordedCodes.length ? "重新录制" : "开始录制")}</button>
              <button id="btnApplyRecordedBack" class="btn btn-sm btn-primary" data-focusable="true" ${bm.recordedCodes.length ? "" : "disabled"}>确认录制结果 (${bm.recordedCodes.length} 键)</button>
            </div>
          </div>
        `;
      } else {
        const cList = bm.comboCodes || [];
        const comboChips = cList.length
          ? cList.map((c, idx) => `<button class="combo-chip-pill interactive" data-combo-remove-idx="${idx}" title="点击移除" data-focusable="true">${esc(resolveShortHidLabel(c))} ×</button>`).join(`<span class="keycap-plus">+</span>`)
          : `<span style="font-size:11.5px;color:var(--text-muted);">依次点选下方手柄键或全键盘按键，自由拼接最多 4 键组合宏</span>`;
        tabBody = `
          <div class="backkey-picker-section">
            <div class="gpd-key-group-label">自由组合构建器（已选 ${cList.length}/4 键）</div>
            <div id="comboPreviewBar" class="combo-preview-bar">${comboChips}</div>
            <div style="display:flex;gap:8px;justify-content:flex-end;margin-bottom:4px;">
              <button id="btnClearManualCombo" class="btn btn-sm" data-focusable="true" ${cList.length ? "" : "disabled"}>清空已选</button>
              <button id="btnApplyManualCombo" class="btn btn-sm btn-primary" data-focusable="true" ${cList.length ? "" : "disabled"}>确认组合 (${cList.length} 键)</button>
            </div>
            <div class="gpd-key-group-label">手柄按键</div>
            <div class="backkey-picker-grid">
              ${GPD_BACK_GAMEPAD_OPTIONS.map((opt) => `
                <button class="pill-btn small ${cList.includes(opt.value) ? "active" : ""}" data-combo-toggle-code="${opt.value}" data-focusable="true">${esc(opt.label)}</button>
              `).join("")}
            </div>
            ${GPD_FULL_KEYBOARD_GROUPS.map((grp) => `
              <div class="gpd-key-group-label" style="margin-top:6px;">${esc(grp.title)}</div>
              <div class="full-kb-grid">
                ${grp.keys.map((opt) => `
                  <button class="pill-btn small kb-key-btn ${opt.special ? "is-special-fn" : ""} ${cList.includes(opt.value) ? "active" : ""}" data-combo-toggle-code="${opt.value}" data-focusable="true">${esc(opt.label)}</button>
                `).join("")}
              </div>
            `).join("")}
          </div>
        `;
      }

      body.innerHTML = tabsHtml + tabBody;

      body.querySelectorAll("[data-back-modal-tab]").forEach((btn) => {
        btn.addEventListener("click", () => {
          const nextTab = btn.dataset.backModalTab;
          if (!nextTab || nextTab === bm.tab) return;
          if (bm.recordingActive) {
            bm.recordingActive = false;
            rpc("cancelRecordBackButton").catch(() => {});
          }
          bm.tab = nextTab;
          if (nextTab === "record") {
            startLiveRecording();
          } else {
            renderModalContent();
          }
        });
      });

      body.querySelectorAll("[data-pick-back-key]").forEach((btn) => {
        btn.addEventListener("click", () => {
          applyBackSlotSingle(slot, Number(btn.dataset.pickBackKey));
        });
      });

      document.getElementById("btnToggleBackRecord")?.addEventListener("click", () => {
        if (state.backModal.recordingActive) stopLiveRecording();
        else startLiveRecording();
      });
      document.getElementById("btnApplyRecordedBack")?.addEventListener("click", () => {
        if (state.backModal.recordedCodes.length) {
          applyBackSlotCombo(slot, state.backModal.recordedCodes);
        }
      });

      body.querySelectorAll("[data-combo-remove-idx]").forEach((btn) => {
        btn.addEventListener("click", () => {
          const idx = Number(btn.dataset.comboRemoveIdx);
          if (idx >= 0 && idx < bm.comboCodes.length) {
            bm.comboCodes.splice(idx, 1);
            renderModalContent();
          }
        });
      });

      body.querySelectorAll("[data-combo-toggle-code]").forEach((btn) => {
        btn.addEventListener("click", () => {
          const code = Number(btn.dataset.comboToggleCode);
          const pos = bm.comboCodes.indexOf(code);
          if (pos >= 0) {
            bm.comboCodes.splice(pos, 1);
          } else if (bm.comboCodes.length < 4) {
            bm.comboCodes = normalizeComboCodesList([...bm.comboCodes, code]);
          } else {
            showToast("单个背键最多支持 4 键同时触发", "warn");
          }
          renderModalContent();
        });
      });

      document.getElementById("btnClearManualCombo")?.addEventListener("click", () => {
        bm.comboCodes = [];
        renderModalContent();
      });
      document.getElementById("btnApplyManualCombo")?.addEventListener("click", () => {
        if (bm.comboCodes.length) {
          applyBackSlotCombo(slot, bm.comboCodes);
        }
      });

      applySpatialFocus();
    };

    window.__hoRefreshBackModal = renderModalContent;
    document.getElementById("modalBackdrop").classList.remove("hidden");
    state.focusIndex = 0;
    renderModalContent();
  }

  async function startRecordCombo(target) {
    if (state.recordingTarget === target) {
      state.recordingTarget = null;
      await rpc("cancelRecordGamepadCombo");
      render();
      showToast("已取消组合键录制", "info");
      return;
    }
    state.recordingTarget = target;
    render();
    await rpc("beginRecordGamepadCombo");
    showToast("请在手柄上同时按下目标组合键后松开...", "info");
  }

  async function clearGamepadCombo(target) {
    if (!state.snapshot?.settings) return;
    if (state.recordingTarget === target) {
      state.recordingTarget = null;
      await rpc("cancelRecordGamepadCombo");
    }
    const next = { ...state.snapshot.settings };
    const labels = {
      console: "呼出前端主界面",
      qam: "呼出快捷侧栏",
      osd: "切换帧率悬浮窗",
      mouse: "手柄鼠标模式",
      insert: "呼出超分菜单"
    };
    if (target === "console") next.consoleGamepadCombo = { buttons: [] };
    else if (target === "qam") next.qamGamepadCombo = { buttons: [] };
    else if (target === "osd") next.osdGamepadCombo = { buttons: [] };
    else if (target === "mouse") next.mouseModeGamepadCombo = { buttons: [] };
    else if (target === "insert") next.insertGamepadCombo = { buttons: [] };
    await rpc("saveSettings", next);
    const backHint = next.enableBackButtonHotkeys && target !== "mouse"
      ? "（若背键仍触发 F14/F15，请关闭上方「接管背键 F14–F16」开关，或点击「应用到手柄」以新版 V2 协议重写背键）"
      : "";
    showToast(`已取消「${labels[target] || target}」手柄快捷键${backHint}`, "info");
  }

  async function finishGamepadComboRecord(buttons) {
    const target = state.recordingTarget;
    state.recordingTarget = null;
    if (target && buttons?.length && buttons.length < 2) {
      render();
      showToast("为避免占用游戏内单键（如 Start/A/B），全局快捷键请至少同时按下 2 个按键组合", "warn");
      return;
    }
    if (target && buttons?.length >= 2 && state.snapshot?.settings) {
      const next = { ...state.snapshot.settings };
      if (target === "console") next.consoleGamepadCombo = { buttons };
      else if (target === "qam") next.qamGamepadCombo = { buttons };
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

  function applySpatialFocus(shouldScroll = false) {
    document.querySelectorAll(".gp-focused").forEach((el) => el.classList.remove("gp-focused"));
    const items = getFocusableItems();
    if (!items.length) return;
    if (state.focusIndex >= items.length) state.focusIndex = 0;
    if (state.focusIndex < 0) state.focusIndex = items.length - 1;
    const target = items[state.focusIndex];
    target.classList.add("gp-focused");
    if (shouldScroll) {
      target.scrollIntoView({ block: "nearest", inline: "nearest" });
    }
  }

  function moveSpatial(dir) {
    state.lastInputModality = "gamepad";
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
      applySpatialFocus(true);
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
    applySpatialFocus(true);
  }

  function handleGamepadAction(action) {
    state.lastInputModality = "gamepad";
    const modal = document.getElementById("modalBackdrop");
    const modalOpen = modal && !modal.classList.contains("hidden");

    switch (action) {
      case "UP":
      case "DOWN":
      case "LEFT":
      case "RIGHT":
        if (!modalOpen && state.activePluginId && document.getElementById("activePluginFrame")) {
          forwardGamepadToPluginFrame(action);
        } else {
          moveSpatial(action);
        }
        break;
      case "A": {
        if (!modalOpen && state.activePluginId && document.getElementById("activePluginFrame")) {
          forwardGamepadToPluginFrame("A");
        } else {
          const items = getFocusableItems();
          items[state.focusIndex]?.click();
        }
        break;
      }
      case "B":
        if (modalOpen) {
          if (state.backModal.open) {
            cleanupBackModal();
          } else {
            modal.classList.add("hidden");
          }
          applySpatialFocus();
        } else if (state.activePluginId) {
          state.activePluginId = null;
          state.focusIndex = 0;
          render();
        } else if (!IS_QAM && state.tab === "plugins") {
          state.tab = "handheld";
          state.focusIndex = 0;
          render();
        } else if (!IS_QAM && state.tab === "opti" && state.optiSubTab === "library") {
          state.optiSubTab = "tune";
          state.focusIndex = 0;
          render();
        } else if (state.advancedOpen && !IS_QAM) {
          state.advancedOpen = false;
          render();
        } else if (IS_QAM && state.qamTab === "opti" && state.qamOptiSubView === "games") {
          state.qamOptiSubView = "tune";
          state.focusIndex = 0;
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
            state.focusIndex = Math.max(0, QAM_TABS.indexOf(state.qamTab));
          } else {
            state.tab = CONSOLE_TABS[(CONSOLE_TABS.indexOf(state.tab) + step + CONSOLE_TABS.length) % CONSOLE_TABS.length];
            state.focusIndex = Math.max(0, CONSOLE_TABS.indexOf(state.tab));
          }
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
      state.activePluginId = null;
      state.focusIndex = Math.max(0, CONSOLE_TABS.indexOf(state.tab));
      render();
    });
  });

  document.querySelectorAll(".qam-rail-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.qamTab = btn.dataset.qamTab || "handheld";
      if (state.qamTab !== "opti") {
        state.qamOptiSubView = "tune";
      }
      if (state.qamTab !== "plugins") {
        state.activePluginId = null;
      }
      state.focusIndex = 0;
      render();
    });
  });

  document.getElementById("topApuChip")?.addEventListener("click", () => {
    state.tab = "monitor";
    state.focusIndex = Math.max(0, CONSOLE_TABS.indexOf("monitor"));
    render();
  });

  document.getElementById("topSysChip")?.addEventListener("click", () => {
    state.tab = "handheld";
    state.focusIndex = 0;
    render();
  });

  document.getElementById("btnLayoutSwitch")?.addEventListener("click", async () => {
    const cur = state.snapshot?.settings?.uiLayout || document.body.dataset.layout || "bento";
    const next = cur === "cockpit" ? "bento" : "cockpit";
    syncLayoutDom(next);
    if (state.snapshot?.settings) state.snapshot.settings.uiLayout = next;
    render();
    await rpc("setUiLayout", { layout: next });
    const meta = UI_LAYOUTS.find((l) => l.id === next);
    showToast(`已切换布局风格：${meta?.label || next}`, "info");
  });

  document.getElementById("btnThemeSwitch")?.addEventListener("click", async () => {
    const cur = state.snapshot?.settings?.uiTheme || document.body.dataset.theme || "aurora";
    const idx = UI_THEMES.findIndex((t) => t.id === cur);
    const next = UI_THEMES[(idx + 1) % UI_THEMES.length];
    syncThemeDom(next.id);
    if (state.snapshot?.settings) state.snapshot.settings.uiTheme = next.id;
    render();
    pushThemeToActivePluginFrame();
    await rpc("setUiTheme", { theme: next.id });
    showToast(`已切换界面皮肤：${next.label}`, "info");
  });

  function renderAppUpdateModalBody() {
    const modal = document.getElementById("modalBackdrop");
    const title = document.getElementById("modalTitle");
    const sub = document.getElementById("modalSubtitle");
    const body = document.getElementById("modalBody");
    if (!modal || !title || !sub || !body) return;

    const curVer = state.snapshot?.appVersion || "0.6.2";
    const upd = state.snapshot?.latestUpdate;
    const hasUpd = !!(upd && upd.hasUpdate);
    const prog = state.appUpdateProgress;
    const includePre = !!state.appUpdateIncludePrerelease;

    title.textContent = "HaloDeck 在线检查与自我更新";
    sub.textContent = `当前版本 v${curVer} · 走零限流 GitHub Releases 通道（支持镜像前缀与 SHA-256 校验）`;

    body.innerHTML = `
      <div class="update-modal-layout">
        <div class="plugin-detail-head">
          <span class="hh-card-badge">当前版本：v${esc(curVer)}</span>
          ${upd ? `<span class="hh-card-badge ${hasUpd ? "live" : ""}">${hasUpd ? `发现新版：v${esc(upd.latestVersion)}${upd.isPrerelease ? " (测试版)" : ""}` : `最新版本：v${esc(upd.latestVersion)}（已是最新）`}</span>` : `<span class="hh-card-badge">尚未检查</span>`}
          <button id="btnModalTogglePre" class="pill-btn small ${includePre ? "active" : ""}" data-focusable="true">${includePre ? "通道：含测试版 (-rc)" : "通道：仅正式版"}</button>
        </div>

        <div id="appUpdateModalProg" class="plugin-dl-banner ${prog ? "" : "hidden"}">
          ${prog ? (prog.status === "applying" ? "SHA-256 校验通过，正在重启原地更新..." : `正在下载 v${esc(prog.version)} · ${Math.round(prog.percent || 0)}%`) : ""}
        </div>

        ${upd?.releaseNotes ? `
          <div class="update-notes-card">
            <div class="update-notes-title">${esc(upd.title || `v${upd.latestVersion}`)} 更新说明</div>
            <pre class="update-notes-pre">${esc(upd.releaseNotes)}</pre>
          </div>
        ` : `
          <div class="update-notes-card">
            <div class="cw-row-sub">点击下方「立即检查更新」即可获取最新版本信息；如国内下载较慢，可在「插件扩展 → 链接直装与网络加速」中配置 GitHub 镜像前缀。</div>
          </div>
        `}

        <div class="update-modal-footer">
          <button id="btnModalCheckAppUpdate" class="btn" data-focusable="true" ${state.appUpdateChecking ? "disabled" : ""}>${state.appUpdateChecking ? "检查中..." : "立即检查更新"}</button>
          ${hasUpd && upd.downloadUrl ? `<button id="btnModalApplyAppUpdate" class="btn btn-primary" data-focusable="true" ${prog ? "disabled" : ""}>${prog ? "更新中..." : `一键下载并更新 v${esc(upd.latestVersion)}`}</button>` : ""}
          <button id="btnModalOpenReleases" class="btn" data-focusable="true">前往 Release 页面 ↗</button>
        </div>
      </div>
    `;
    body.scrollTop = 0;

    document.getElementById("btnModalTogglePre")?.addEventListener("click", async () => {
      state.appUpdateIncludePrerelease = !state.appUpdateIncludePrerelease;
      renderAppUpdateModalBody();
      await runAppUpdateCheck(true);
    });
    document.getElementById("btnModalCheckAppUpdate")?.addEventListener("click", () => runAppUpdateCheck(true));
    document.getElementById("btnModalApplyAppUpdate")?.addEventListener("click", async () => {
      if (state.appUpdateProgress) return;
      try {
        const res = await rpc("downloadAndApplyAppUpdate", { includePrerelease: !!state.appUpdateIncludePrerelease });
        showToast(res?.message || "正在重启并应用更新...", res?.ok ? "success" : "warn");
      } catch (err) {
        state.appUpdateProgress = null;
        showToast(err.message || "下载或应用更新失败", "error");
        renderAppUpdateModalBody();
      }
    });
    document.getElementById("btnModalOpenReleases")?.addEventListener("click", async () => {
      const url = state.snapshot?.latestUpdate?.htmlUrl || "https://github.com/Lokilankaaa/HaloDeck/releases";
      try { await rpc("openExternalUrl", { url }); } catch (err) { showToast(err.message, "error"); }
    });
  }

  async function runAppUpdateCheck(refreshModal = false) {
    if (state.appUpdateChecking) return;
    state.appUpdateChecking = true;
    if (refreshModal) renderAppUpdateModalBody();
    else render();
    try {
      const res = await rpc("checkAppUpdate", { includePrerelease: !!state.appUpdateIncludePrerelease });
      if (res?.update && state.snapshot) {
        state.snapshot.latestUpdate = res.update;
      }
      showToast(res?.message || "检查更新完成", res?.update?.hasUpdate ? "success" : "info");
    } catch (err) {
      showToast(err.message || "检查更新失败", "error");
    } finally {
      state.appUpdateChecking = false;
      render();
      if (refreshModal) renderAppUpdateModalBody();
    }
  }

  function openAppUpdateModal() {
    const modal = document.getElementById("modalBackdrop");
    if (!modal) return;
    renderAppUpdateModalBody();
    modal.classList.remove("hidden");
    if (!state.snapshot?.latestUpdate) {
      runAppUpdateCheck(true);
    }
  }

  document.getElementById("btnTopAppVersion")?.addEventListener("click", () => openAppUpdateModal());
  document.getElementById("btnOpenQam")?.addEventListener("click", () => rpc("toggleQam"));
  document.getElementById("btnMinimize")?.addEventListener("click", () => rpc("minimizeToTray"));
  document.getElementById("btnFullscreen")?.addEventListener("click", () => rpc("toggleFullscreen"));
  document.getElementById("btnCloseApp")?.addEventListener("click", () => rpc("minimizeToTray"));
  document.getElementById("btnCloseModal")?.addEventListener("click", () => {
    if (state.backModal.open) {
      cleanupBackModal();
    } else {
      document.getElementById("modalBackdrop").classList.add("hidden");
    }
    applySpatialFocus();
  });

  window.addEventListener("pointerdown", (e) => {
    state.lastInputModality = "pointer";
    const t = e.target;
    if (t && t.tagName === "INPUT" && t.type === "range") {
      state.isDraggingRange = true;
      if (t.id === "tdpRangeInput") state.activeSliderDrag = "tdp";
      else if (t.id === "brightRange") state.activeSliderDrag = "bright";
      else if (t.id === "volRange") state.activeSliderDrag = "vol";
      else state.activeSliderDrag = t.id || "range";
    }
    const focusEl = t?.closest?.('[data-focusable="true"]');
    if (focusEl) {
      const items = getFocusableItems();
      const idx = items.indexOf(focusEl);
      if (idx >= 0) state.focusIndex = idx;
    }
  }, true);

  window.addEventListener("wheel", () => {
    state.lastInputModality = "pointer";
  }, { passive: true, capture: true });

  const endRangeDrag = () => {
    if (!state.isDraggingRange) return;
    state.isDraggingRange = false;
    state.activeSliderDrag = null;
    state.dragCooldownUntil = Date.now() + 280;
    setTimeout(() => {
      if (!state.isDraggingRange && Date.now() >= state.dragCooldownUntil && state.pendingSnapshot) {
        const pending = state.pendingSnapshot;
        state.pendingSnapshot = null;
        applySnapshot(pending);
      }
    }, 300);
  };
  window.addEventListener("pointerup", endRangeDrag, true);
  window.addEventListener("pointercancel", endRangeDrag, true);

  window.addEventListener("keydown", (e) => {
    if (state.backModal.open && state.backModal.tab === "record" && state.backModal.recordingActive) {
      const hid = CODE_TO_HID_MAP[e.code];
      if (hid) {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        activeRecordKeySet.add(hid);
        if (burstRecordKeySet.size < 4) burstRecordKeySet.add(hid);
        state.backModal.recordedCodes = normalizeComboCodesList(Array.from(burstRecordKeySet));
        if (typeof window.__hoRefreshBackModal === "function") window.__hoRefreshBackModal();
        return;
      }
    }
  }, true);

  window.addEventListener("keyup", (e) => {
    if (state.backModal.open && state.backModal.tab === "record" && state.backModal.recordingActive) {
      const hid = CODE_TO_HID_MAP[e.code];
      if (hid) {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        activeRecordKeySet.delete(hid);
        if (activeRecordKeySet.size === 0 && burstRecordKeySet.size > 0) {
          state.backModal.recordedCodes = normalizeComboCodesList(Array.from(burstRecordKeySet));
          state.backModal.recordingActive = false;
          rpc("cancelRecordBackButton").catch(() => {});
          if (typeof window.__hoRefreshBackModal === "function") window.__hoRefreshBackModal();
        }
      }
    }
  }, true);

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
