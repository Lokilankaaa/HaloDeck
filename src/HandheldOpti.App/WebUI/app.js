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
    { value: -1, label: "保留原厂设置" },
    { value: 0x0000, label: "关闭背键" },
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
    { value: 0x0049, label: "Insert (超分菜单)" }
  ];

  const GPD_BACK_PRESETS = {
    fps_l3r3: { l4: 0x800F, r4: 0x8010, shortLabel: "摇杆按下 (L3 / R3)", label: "摇杆按下 (L4=手柄L3 / R4=手柄R3)" },
    act_ab: { l4: 0x8007, r4: 0x8008, shortLabel: "跳跃与闪避 (A / B)", label: "跳跃与闪避 (L4=手柄A / R4=手柄B)" },
    shoulder_lbrb: { l4: 0x800B, r4: 0x800C, shortLabel: "肩键扩展 (LB / RB)", label: "肩键扩展 (L4=手柄LB / R4=手柄RB)" },
    halodeck_fn: { l4: 0x0068, r4: 0x0069, shortLabel: "侧栏与帧率窗", label: "侧栏与帧率窗 (L4=侧栏 / R4=帧率窗)", enableHotkeys: true }
  };

  const GPD_COMBO_MODIFIER_OPTIONS = [
    { value: 0x00E0, label: "Ctrl" },
    { value: 0x00E1, label: "Shift" },
    { value: 0x00E2, label: "Alt" },
    { value: 0x00E3, label: "Win" }
  ];

  const GPD_COMBO_EXTRA_KEYS = [
    { value: 0x0012, label: "O" },
    { value: 0x000C, label: "I" },
    { value: 0x0010, label: "M" },
    { value: 0x0007, label: "D" },
    { value: 0x0016, label: "S" },
    { value: 0x0014, label: "Q" },
    { value: 0x0008, label: "E" },
    { value: 0x0009, label: "F" },
    { value: 0x0015, label: "R" },
    { value: 0x002C, label: "Space" },
    { value: 0x002B, label: "Tab" },
    { value: 0x0028, label: "Enter" },
    { value: 0x0029, label: "Esc" },
    { value: 0x0049, label: "Insert" },
    { value: 0x003D, label: "F4" },
    { value: 0x0043, label: "F10" },
    { value: 0x0044, label: "F11" },
    { value: 0x0045, label: "F12" },
    { value: 0x0068, label: "F13 侧栏" },
    { value: 0x0069, label: "F14 帧率窗" },
    { value: 0x006A, label: "F15 超分菜单" }
  ];

  const GPD_BACK_COMBO_PRESETS = [
    { id: "gp_lbrb", label: "LB + RB 双肩大招", codes: [0x800B, 0x800C] },
    { id: "gp_ltrt", label: "LT + RT 双扳机", codes: [0x800D, 0x800E] },
    { id: "gp_l3r3", label: "L3 + R3 觉醒/处决", codes: [0x800F, 0x8010] },
    { id: "gp_ax", label: "手柄 A + X", codes: [0x8007, 0x8009] },
    { id: "kb_qam", label: "Ctrl+Alt+O (侧栏)", codes: [0x00E0, 0x00E2, 0x0012] },
    { id: "kb_osd", label: "Ctrl+Alt+M (帧率窗)", codes: [0x00E0, 0x00E2, 0x0010] },
    { id: "kb_alttab", label: "Alt + Tab 切窗", codes: [0x00E2, 0x002B] },
    { id: "kb_wind", label: "Win + D 桌面", codes: [0x00E3, 0x0007] },
    { id: "kb_snip", label: "Win+Shift+S 截屏", codes: [0x00E3, 0x00E1, 0x0016] }
  ];

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
      [0x0068, "F13"], [0x0069, "F14"], [0x006A, "F15"]
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
    if (typeof code !== "number" || code < 0) return "保留原厂设置";
    if (code === 0) return "关闭背键";
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
    controlsAdvancedOpen: false,
    searchQuery: "",
    libraryFilter: "all",
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
    } else if (name === "toast" && data?.message) {
      showToast(data.message, data.kind || "info");
    }
  }

  // ---------------- Formatters & Live Telemetry ----------------
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

  function formatPowerBadge(sys) {
    if (!sys) return "--%";
    if (!sys.hasBattery) return "AC 直连 (无电池)";
    const sysW = typeof sys.systemDrawWatts === "number" && sys.systemDrawWatts > 0
      ? sys.systemDrawWatts
      : (!sys.isAcOnline && sys.batteryPowerWatts > 0 ? sys.batteryPowerWatts : 0);
    const eta = formatStandbyEta(sys);
    const etaPart = eta && eta !== "计算中..." ? ` · 续航${eta}` : "";
    if (sys.isAcOnline) {
      if (sys.isCharging) {
        const chg = typeof sys.batteryPowerWatts === "number" && sys.batteryPowerWatts > 0
          ? `充+${sys.batteryPowerWatts.toFixed(0)}W`
          : "充电";
        return `${sys.batteryPercent}% ${chg}${etaPart}`;
      }
      return `${sys.batteryPercent}% 旁路${etaPart}`;
    }
    const wStr = sysW > 0 ? ` -${sysW.toFixed(1)}W` : " 电池";
    return `${sys.batteryPercent}%${wStr}${etaPart}`;
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

    return `
      <svg class="fan-curve-svg" viewBox="0 0 ${W} ${H}" role="img" aria-label="温度与风扇占空比曲线图">
        <defs>
          <linearGradient id="fanCurveArea" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#2dd4bf" stop-opacity="0.32" />
            <stop offset="100%" stop-color="#2dd4bf" stop-opacity="0.02" />
          </linearGradient>
        </defs>
        ${[40, 60, 80, 100].map((f) => {
          const y = mapY(f).toFixed(1);
          return `<line x1="${padL}" y1="${y}" x2="${W - padR}" y2="${y}" stroke="rgba(255,255,255,0.08)" stroke-width="1" />
                  <text x="4" y="${Number(y) + 3}" fill="#7f8da3" font-size="9">${f}%</text>`;
        }).join("")}
        ${[40, 55, 70, 82].map((t) => {
          const x = mapX(t).toFixed(1);
          return `<line x1="${x}" y1="${padT}" x2="${x}" y2="${padT + ih}" stroke="rgba(255,255,255,0.07)" stroke-width="1" />
                  <text x="${Number(x) - 9}" y="${H - 6}" fill="${t >= 82 ? "#fbbf24" : "#7f8da3"}" font-size="9">${t}°</text>`;
        }).join("")}
        <rect x="${safetyX}" y="${padT}" width="${(W - padR - Number(safetyX)).toFixed(1)}" height="${(Number(safetyY) - padT).toFixed(1)}" fill="rgba(245,158,11,0.12)" stroke="rgba(245,158,11,0.4)" stroke-dasharray="3,3" stroke-width="1" />
        ${areaStr ? `<polygon points="${areaStr}" fill="url(#fanCurveArea)" />` : ""}
        ${coordStr ? `<polyline points="${coordStr}" fill="none" stroke="#2dd4bf" stroke-width="2.2" stroke-linejoin="round" />` : ""}
        ${pts.map((p, idx) => `
          <circle cx="${mapX(p.tempC).toFixed(1)}" cy="${mapY(p.fanPercent).toFixed(1)}" r="4.2" fill="#0f1722" stroke="#2dd4bf" stroke-width="2" />
          <text x="${mapX(p.tempC).toFixed(1)}" y="${Math.max(11, mapY(p.fanPercent) - 7).toFixed(1)}" text-anchor="middle" fill="#cbd5e1" font-size="8.5">${p.fanPercent}%</text>
        `).join("")}
        <circle cx="${liveX}" cy="${liveY}" r="5" fill="#fbbf24" stroke="#111827" stroke-width="1.5" />
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
      const fpsPrefix = sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : "";
      topApuLabel.textContent = `${fpsPrefix}APU ${apuText} · C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}° · VRAM ${vramUsedGb}G`;
    }

    const topSysLabel = document.getElementById("topSysLabel");
    if (topSysLabel) {
      topSysLabel.textContent = `${formatResShort(`${sys.screenWidth}x${sys.screenHeight}`)} · ${sys.refreshRateHz || "--"}Hz · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`;
    }

    const pfApuVal = document.getElementById("pfApuVal");
    if (pfApuVal) pfApuVal.textContent = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : `目标 ${state.snapshot?.settings?.currentTdpWatts || 45}W`;

    const pfApuSub = document.getElementById("pfApuSub");
    if (pfApuSub) pfApuSub.textContent = `GPU ${sys.gpuUsagePercent || 0}% · CPU ${sys.cpuTempC || "--"}° / GPU ${sys.gpuTempC || "--"}°C`;

    const pfSysVal = document.getElementById("pfSysVal");
    const sysDraw = typeof sys.systemDrawWatts === "number" && sys.systemDrawWatts > 0
      ? sys.systemDrawWatts
      : (sys.apuPowerWatts != null ? sys.apuPowerWatts + 4.5 : 0);
    if (pfSysVal) pfSysVal.textContent = sysDraw > 0 ? `${sysDraw.toFixed(1)} W` : "-- W";

    const pfSysSub = document.getElementById("pfSysSub");
    if (pfSysSub) {
      if (!sys.hasBattery) pfSysSub.textContent = "AC 直供运行功耗";
      else if (sys.isAcOnline && sys.isCharging) pfSysSub.textContent = `不含充电 · 充电器 ≈${(sys.estimatedChargerWatts ?? (sysDraw + (sys.batteryPowerWatts || 0))).toFixed(1)}W`;
      else if (sys.isAcOnline) pfSysSub.textContent = "AC 旁路直供整机";
      else pfSysSub.textContent = "电池实测整机功耗";
    }

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
      else pfBatSub.textContent = `${sys.batteryPercent}% · ${sys.voltageVolts > 0 ? `${sys.voltageVolts.toFixed(1)}V` : "离线放电"}`;
    }

    const pfEtaVal = document.getElementById("pfEtaVal");
    if (pfEtaVal) pfEtaVal.textContent = formatStandbyEta(sys);

    const pfEtaSub = document.getElementById("pfEtaSub");
    if (pfEtaSub) {
      if (!sys.hasBattery) pfEtaSub.textContent = "未安装电池组";
      else if (sys.isAcOnline) pfEtaSub.textContent = `按整机 ${sysDraw.toFixed(1)}W 离电推算 (${sys.batteryRemainingWh?.toFixed(1) || "--"}Wh)`;
      else pfEtaSub.textContent = `余电 ${sys.batteryRemainingWh?.toFixed(1) || "--"} / ${sys.batteryFullWh?.toFixed(1) || "--"} Wh`;
    }

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
    setEl("kpiBat", formatPowerBadge(sys));
    setEl("kpiBatSub", sys.isAcOnline && sys.estimatedChargerWatts != null
      ? `整机 ${sysDraw.toFixed(1)}W · 充电器 ≈${sys.estimatedChargerWatts.toFixed(1)}W`
      : `整机 ${sysDraw.toFixed(1)}W · 预计续航 ${formatStandbyEta(sys)}`);

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
      const rpm = hw.fanRpm || 0;
      const fpsBadge = sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : "";
      qamPowerChip.textContent = `${fpsBadge}APU ${apuText} · C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}° · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}${rpm > 0 ? ` · ${rpm}RPM` : ""}`;
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
    const l4IsMacro = gpd.l4Mode === 2 || gpd.l4Mode === 6;
    const r4IsMacro = gpd.r4Mode === 2 || gpd.r4Mode === 6;
    const l4Combo = l4IsMacro && Array.isArray(gpd.l4ComboCodes) && gpd.l4ComboCodes.length >= 2
      ? normalizeComboCodesList(gpd.l4ComboCodes)
      : [];
    const r4Combo = r4IsMacro && Array.isArray(gpd.r4ComboCodes) && gpd.r4ComboCodes.length >= 2
      ? normalizeComboCodesList(gpd.r4ComboCodes)
      : [];
    state.gpdDraft = {
      l4Key: !gpd.connected || (l4IsMacro && !l4Combo.length) ? -1 : (l4Combo.length ? 0 : (typeof gpd.l4KeyCode === "number" ? gpd.l4KeyCode : 0)),
      r4Key: !gpd.connected || (r4IsMacro && !r4Combo.length) ? -1 : (r4Combo.length ? 0 : (typeof gpd.r4KeyCode === "number" ? gpd.r4KeyCode : 0)),
      l4Combo,
      r4Combo,
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

  function renderStepSlider({ idPrefix, value, min = 0, displayText = null, disabled = false, muted = false, muteBtnId = null }) {
    const pct = Math.max(min, Math.min(100, Number(value) || 0));
    const text = displayText || (muted ? "静音" : `${pct}%`);
    return `
      <div class="step-slider">
        <button id="${idPrefix}Dec" class="step-btn" data-focusable="true" ${disabled ? "disabled" : ""} aria-label="减小">−</button>
        <div class="step-track" data-slider-track="${esc(idPrefix)}">
          <div id="${idPrefix}Fill" class="step-fill" style="width:${muted ? 0 : pct}%;"></div>
          <span id="${idPrefix}Val" class="step-val">${esc(text)}</span>
          <input id="${idPrefix}Range" class="step-range-input" type="range" min="${min}" max="100" step="1" value="${pct}" ${disabled ? "disabled" : ""} aria-label="${esc(idPrefix)}" />
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

    const curTdp = settings.currentTdpWatts || 40;
    const customPresets = (settings.customTdpPresets && settings.customTdpPresets.length)
      ? settings.customTdpPresets
      : [15, 25, 35, 45, 60, 80, 100];
    const unlockThreeStage = !!settings.unlockThreeStagePower;
    const slowPpt = settings.slowPptWatts || curTdp;
    const fastPpt = settings.fastPptWatts || Math.min(140, curTdp + 2);
    const hasCurrentPreset = customPresets.includes(curTdp);

    const fanMode = normalizeFanModeUi(settings.fanMode);
    const manualFanPct = settings.manualFanPercent || 55;
    const fanCurves = (settings.fanCurves && settings.fanCurves.length) ? settings.fanCurves : getDefaultFanCurves();
    const activeCurve = getActiveFanCurve(settings);

    const hzOptions = (sys.supportedRefreshRates && sys.supportedRefreshRates.length)
      ? sys.supportedRefreshRates.map((h) => ({ value: String(h), label: `${h}Hz` }))
      : [{ value: "60", label: "60Hz" }, { value: "120", label: "120Hz" }];

    const resOptions = (sys.supportedResolutions && sys.supportedResolutions.length)
      ? sys.supportedResolutions.slice(0, 3).map((r) => ({ value: r, label: formatResShort(r) }))
      : [{ value: "1920x1080", label: "1080p" }, { value: "1600x900", label: "900p" }, { value: "1280x720", label: "720p" }];

    const curRes = `${sys.screenWidth || 1920}x${sys.screenHeight || 1080}`;
    const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
    const curTempEval = maxTemp > 0 ? maxTemp : 48;
    const curveEvalPct = evaluateFanCurvePct(curTempEval, activeCurve.points);
    const apuPowerText = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : `目标 ${curTdp}W`;
    const sysDrawNow = typeof sys.systemDrawWatts === "number" && sys.systemDrawWatts > 0
      ? sys.systemDrawWatts
      : (sys.apuPowerWatts != null ? sys.apuPowerWatts + 4.5 : 0);
    const sysDrawText = sysDrawNow > 0 ? `${sysDrawNow.toFixed(1)} W` : "-- W";
    const sysDrawSubText = !sys.hasBattery
      ? "AC 直供运行功耗"
      : (sys.isAcOnline && sys.isCharging
        ? `不含充电 · 充电器 ≈${(sys.estimatedChargerWatts ?? (sysDrawNow + (sys.batteryPowerWatts || 0))).toFixed(1)}W`
        : (sys.isAcOnline ? "AC 旁路直供整机" : "电池实测整机功耗"));
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
          : `${sys.batteryPercent}% · ${sys.voltageVolts > 0 ? `${sys.voltageVolts.toFixed(1)}V` : "离线放电"}`;
    const etaValText = formatStandbyEta(sys);
    const etaSubText = !sys.hasBattery
      ? "未安装电池组"
      : (sys.isAcOnline
        ? `按整机 ${sysDrawNow.toFixed(1)}W 离电推算 (${(sys.batteryRemainingWh || 0).toFixed(1)}Wh)`
        : `余电 ${(sys.batteryRemainingWh || 0).toFixed(1)} / ${(sys.batteryFullWh || 0).toFixed(1)} Wh`);

    const perGameProfiles = settings.perGameProfiles || {};
    const gamePerf = active ? (perGameProfiles[active.id] || {
      gameId: active.id,
      gameName: active.title,
      enabled: false,
      tdpWatts: curTdp,
      refreshRateHz: sys.refreshRateHz || 120
    }) : null;

    const daemonVer = hw.daemonVersion || 0;
    const needsV2Upgrade = hw.serviceRunning && daemonVer < 2;
    const needsV3Upgrade = hw.serviceRunning && daemonVer < 3;
    const gpdConflictNote = hw.gpdServiceRunning
      ? "⚠ GPDToolService 并行运行中：HaloDeck 直接写入 SMU/EC，GPD Tools 只显示其自有内部缓存，不会自动刷新外部变更；建议暂停 GPDTools 以避免双端抢控。"
      : (needsV3Upgrade
        ? "同步机制：HaloDeck 经 PawnIO 直控 SMU/EC（当前 v2 支持 8–120W 开放功耗与风扇曲线；升级至 v3 可解锁内核无钩子 ETW 帧率与 SMN 独立 CPU 温度）。"
        : "同步机制：HaloDeck 经签名 PawnIO 直写 SMU/EC 并附带无钩子 ETW 帧率与 SMN CPU 温度采样，双方互不读取彼此缓存。");

    const serviceBanner = !hw.serviceRunning
      ? `
        <div class="hh-banner warn">
          <div class="hh-banner-text">
            <strong>普通用户态运行中（显示 · 音频 · 监控 · 游戏工具 · 超分均已可用）</strong>
            <span class="hh-banner-sub">${hw.pawnIoInstalled
              ? "已检测到 PawnIO 驱动。点击右侧启用服务后即可免 UAC 弹窗直控 8W–120W 三段功耗、PM-Table 硬件校验与 EC 风扇曲线。"
              : "未检测到 PawnIO 驱动或尚未注册 HaloDeckHardwareSvc。安装 PawnIO 后点击右侧按钮即可开启硬件直控。"}</span>
          </div>
          <div class="hh-banner-actions">
            ${hw.gpdServiceExists ? `<button id="btnToggleGpdSvc" class="btn btn-sm" data-focusable="true">${hw.gpdServiceRunning ? "暂停 GPDTools 服务" : "恢复 GPDTools 服务"}</button>` : ""}
            <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" data-focusable="true">一键启用 TDP / 风扇直控</button>
          </div>
        </div>`
      : `
        <div class="hh-banner ${hw.gpdServiceRunning || needsV2Upgrade ? "warn" : ""}">
          <div class="hh-banner-text">
            <strong>PawnIO 底层硬件直控已就绪 (v${daemonVer || 1}) · ${esc(hw.dmiBoard || "GPD WIN 5")}</strong>
            <span class="hh-banner-sub">${esc(gpdConflictNote)}</span>
          </div>
          <div class="hh-banner-actions">
            ${needsV3Upgrade ? `<button id="btnInstallHwSvc" class="btn btn-sm btn-primary" data-focusable="true">${needsV2Upgrade ? "升级重启硬件服务" : "升级硬件服务 v3 (解锁 FPS/CPU温)"}</button>` : ""}
            ${hw.gpdServiceExists ? `<button id="btnToggleGpdSvc" class="btn btn-sm" data-focusable="true">${hw.gpdServiceRunning ? "暂停 GPDTools 服务" : "恢复 GPDTools 服务"}</button>` : ""}
          </div>
        </div>`;

    const hwReadbackText = hw.hwStapmLimitWatts != null
      ? `PM 实读 (${hw.pmTableVersion || "SMU"})：SPL ${hw.hwStapmLimitWatts.toFixed(1)}W · sPPT ${hw.hwSlowLimitWatts != null ? hw.hwSlowLimitWatts.toFixed(1) : "--"}W · fPPT ${hw.hwFastLimitWatts != null ? hw.hwFastLimitWatts.toFixed(1) : "--"}W`
      : (needsV2Upgrade
        ? `当前硬件服务 v1（仅回传目标 ${hw.appliedTdpWatts}W / 12–85W），升级服务后显示 RyzenSMU PM-Table 真实硬件读回`
        : `SMU 目标：SPL ${hw.appliedTdpWatts || curTdp}W${hw.appliedSlowWatts ? ` · sPPT ${hw.appliedSlowWatts}W · fPPT ${hw.appliedFastWatts}W` : ""}`);

    const pwmPctNow = hw.appliedFanPwm > 0 ? Math.round((hw.appliedFanPwm / 244) * 100) : 0;

    return `
      <div class="page-scroll">
        ${serviceBanner}

        <div class="hh-layout">
          <!-- Left Column: Power Flow + Open Power Config + Direct % & Fan Curve -->
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
                  <span id="pfApuSub" class="pf-sub">GPU ${sys.gpuUsagePercent || 0}% · CPU ${sys.cpuTempC || "--"}° / GPU ${sys.gpuTempC || "--"}°C</span>
                </div>
                <div class="pf-box">
                  <span class="pf-label">整机运行功耗</span>
                  <span id="pfSysVal" class="pf-val">${esc(sysDrawText)}</span>
                  <span id="pfSysSub" class="pf-sub">${esc(sysDrawSubText)}</span>
                </div>
                <div class="pf-box">
                  <span class="pf-label">电池充放功率</span>
                  <span id="pfBatVal" class="pf-val">${esc(batMainText)}</span>
                  <span id="pfBatSub" class="pf-sub">${esc(batSubText)}</span>
                </div>
                <div class="pf-box">
                  <span class="pf-label">实时剩余待机 / 续航</span>
                  <span id="pfEtaVal" class="pf-val accent-teal">${esc(etaValText)}</span>
                  <span id="pfEtaSub" class="pf-sub">${esc(etaSubText)}</span>
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">开放功耗配置 (Strix Halo APU · 8W–120W)</span>
                <span id="consoleTdpBadge" class="hh-card-badge ${hw.smuReady ? "live" : "warn"}">${hw.hwStapmLimitWatts != null ? `PM ${hw.hwStapmLimitWatts.toFixed(0)}W` : (hw.smuReady ? `SMU ${hw.appliedTdpWatts}W` : `设定 ${curTdp}W`)}</span>
              </div>

              <div id="pmReadbackLine" class="gpd-sync-note">${esc(hwReadbackText)}</div>

              <div class="tdp-chip-grid">
                ${customPresets.map((w) => `
                  <button class="tdp-chip-btn ${curTdp === w ? "active" : ""}" data-tdp-watt="${w}" data-focusable="true">${w}W</button>
                `).join("")}
              </div>

              <div class="tdp-open-toolbar">
                <div style="display:flex;gap:6px;flex-wrap:wrap;">
                  ${!hasCurrentPreset
                    ? `<button id="btnAddTdpPreset" class="pill-btn small active" data-focusable="true">+ 存 ${curTdp}W</button>`
                    : (customPresets.length > 1 ? `<button id="btnDelTdpPreset" class="pill-btn small" data-focusable="true">− 删 ${curTdp}W</button>` : "")}
                </div>
                <div style="display:flex;gap:6px;flex-wrap:wrap;">
                  <button id="btnOpenSettingsJson" class="pill-btn small" data-focusable="true">编辑 JSON</button>
                  <button id="btnReloadSettingsJson" class="pill-btn small" data-focusable="true">重载配置</button>
                </div>
              </div>

              <div class="slider-control-row">
                <div class="slider-header">
                  <span>持续 TDP 上限 (STAPM / SPL)</span>
                  <div style="display:flex;align-items:center;gap:6px;">
                    <button id="btnTdpDec" class="step-btn" data-focusable="true" title="降低 1W">−1W</button>
                    <input id="tdpNumberInput" class="num-mini-input" type="number" min="8" max="120" value="${curTdp}" aria-label="输入目标 TDP 瓦数" />
                    <span id="tdpValReadout" style="font-size:12px;font-weight:700;">W</span>
                    <button id="btnTdpInc" class="step-btn" data-focusable="true" title="提高 1W">+1W</button>
                  </div>
                </div>
                <input id="tdpRangeInput" class="hh-range" type="range" min="8" max="120" step="1" value="${curTdp}" />
              </div>

              <div class="cw-row" style="padding:8px 0 4px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">解锁独立三段功耗 (SPL / sPPT / fPPT)</span>
                  <span class="cw-row-sub">分别配置持续功耗 (0x14)、慢速升压 (0x16) 与瞬时峰值 (0x15)</span>
                </div>
                ${renderSwitch("swUnlockThreeStage", unlockThreeStage)}
              </div>

              ${unlockThreeStage ? `
                <div class="tdp-three-stage">
                  <div class="stage-row">
                    <span>sPPT 慢速升压 (8–130W)</span>
                    <div style="display:flex;align-items:center;gap:6px;">
                      <button class="step-btn" data-stage-step="slow:-1" data-focusable="true">−1</button>
                      <strong id="slowPptVal">${slowPpt} W</strong>
                      <button class="step-btn" data-stage-step="slow:1" data-focusable="true">+1</button>
                    </div>
                  </div>
                  <input id="slowPptRange" class="hh-range" type="range" min="8" max="130" step="1" value="${slowPpt}" />

                  <div class="stage-row" style="margin-top:4px;">
                    <span>fPPT 瞬时峰值 (8–140W)</span>
                    <div style="display:flex;align-items:center;gap:6px;">
                      <button class="step-btn" data-stage-step="fast:-1" data-focusable="true">−1</button>
                      <strong id="fastPptVal">${fastPpt} W</strong>
                      <button class="step-btn" data-stage-step="fast:1" data-focusable="true">+1</button>
                    </div>
                  </div>
                  <input id="fastPptRange" class="hh-range" type="range" min="8" max="140" step="1" value="${fastPpt}" />
                </div>` : ""}

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
                    <button id="btnBatTdpDec" class="step-btn" data-focusable="true">−1</button>
                    <strong style="font-size:14px;">${settings.batteryTdpWatts || 25}W</strong>
                    <button id="btnBatTdpInc" class="step-btn" data-focusable="true">+1</button>
                  </div>
                </div>
                <div class="pf-box" style="padding:8px 10px;">
                  <span class="pf-label">插电目标 TDP</span>
                  <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
                    <button id="btnAcTdpDec" class="step-btn" data-focusable="true">−1</button>
                    <strong style="font-size:14px;">${settings.acTdpWatts || 55}W</strong>
                    <button id="btnAcTdpInc" class="step-btn" data-focusable="true">+1</button>
                  </div>
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">风扇直控与温度曲线 (GPD 双风扇 EC)</span>
                <span id="fanRpmBadge" class="hh-card-badge ${hw.ecReady ? "live" : ""}">${hw.ecReady && hw.fanRpm > 0 ? `${hw.fanRpm} RPM · PWM ${pwmPctNow}%` : (hw.serviceRunning ? "仅 SMU 就绪" : "EC 未授权")}</span>
              </div>

              <div class="fan-mode-pills">
                ${[
                  { value: "auto", label: "EC 自动" },
                  { value: "manual", label: "直接百分比" },
                  { value: "curve", label: "温度曲线" }
                ].map((opt) => `
                  <button class="pill-btn ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
                `).join("")}
              </div>

              <div class="fan-direct-box ${fanMode === "manual" ? "is-active" : ""}">
                <div class="slider-header">
                  <span>直接风扇百分比 (${fanMode === "manual" ? "当前直接定速生效" : "拖动或点按快捷 % 即切入直接控制"})</span>
                  <div style="display:flex;align-items:center;gap:6px;">
                    <button id="btnFanPctDec" class="step-btn" data-focusable="true">−5%</button>
                    <strong id="fanManualVal">${manualFanPct}%</strong>
                    <button id="btnFanPctInc" class="step-btn" data-focusable="true">+5%</button>
                  </div>
                </div>
                <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" />
                <div class="fan-quick-grid">
                  ${[30, 45, 60, 75, 90, 100].map((p) => `
                    <button class="pill-btn small ${fanMode === "manual" && manualFanPct === p ? "active" : ""}" data-fan-quick-pct="${p}" data-focusable="true">${p}%</button>
                  `).join("")}
                </div>
              </div>

              <div class="fan-curve-box ${fanMode === "curve" ? "is-active" : ""}">
                <div class="fan-curve-head">
                  <div style="display:flex;align-items:center;gap:8px;">
                    <strong style="font-size:12.5px;color:#fff;">可选 / 可编辑温度曲线</strong>
                    <span class="hh-card-badge ${fanMode === "curve" ? "live" : ""}">当前 ${curTempEval}°C → ${curveEvalPct}%</span>
                  </div>
                  <div style="display:flex;gap:6px;">
                    <button id="btnAddFanCurve" class="pill-btn small" data-focusable="true">+ 新建曲线</button>
                    ${fanCurves.length > 1 ? `<button id="btnDelFanCurve" class="pill-btn small" data-focusable="true">删除</button>` : ""}
                  </div>
                </div>

                <div class="fan-curve-selector">
                  ${fanCurves.map((c) => `
                    <button class="pill-btn small ${activeCurve.id === c.id ? "active" : ""}" data-select-fan-curve="${esc(c.id)}" data-focusable="true">${esc(c.name)}</button>
                  `).join("")}
                </div>

                ${renderFanCurveSvg(activeCurve, curTempEval, curveEvalPct)}

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
              </div>

              <div style="font-size:11px;color:var(--text-muted);">
                热安全底线：APU ≥ 82°C 强制保底 ≥ 80%、≥ 86°C 强制 100%；15s 心跳超时或退出程序自动交还 EC 自主控温。
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
                      min: 5,
                      displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "外接屏不支持",
                      disabled: !sys.brightnessSupported
                    })}
                    ${renderStepSlider({
                      idPrefix: "vol",
                      value: sys.volumePercent ?? 50,
                      min: 0,
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
                  <span class="tb-sub">${settings.preferredOskMode === "xbox" && sys.gpdXboxKeyboardAvailable ? "Xbox 键盘" : settings.preferredOskMode === "osk" ? "传统 OSK" : "触摸键盘"}</span>
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
                  <button id="btnGamePerfTdpDec" class="step-btn" data-focusable="true">−1W</button>
                  <button id="btnGamePerfTdpInc" class="step-btn" data-focusable="true">+1W</button>
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
    const hw = sys.hardwareService || {};
    const settings = snap.settings || {};
    const level = settings.overlayLevel ?? 0;
    const pos = settings.overlayPosition || "top-center";
    const opacity = settings.overlayOpacity ?? 0.86;
    const daemonVer = hw.daemonVersion || 0;
    const vramUsedGb = ((sys.vramUsedMb || 0) / 1024).toFixed(1);
    const vramTotalGb = sys.vramTotalMb > 0 ? (sys.vramTotalMb / 1024).toFixed(0) : "16";
    const sharedGb = ((sys.sharedVramUsedMb || 0) / 1024).toFixed(1);
    const fpsText = sys.currentFps > 0 ? `${sys.currentFps} FPS` : "待检测游戏";
    const fpsSubText = sys.frameTimeMs > 0
      ? `${sys.frameTimeMs.toFixed(1)} ms · ETW 无钩子`
      : (daemonVer >= 3 ? "DX11 / DX12 / Vulkan 就绪" : "升级硬件服务 v3 解锁");

    const previewLines = level === 0
      ? ["悬浮窗已关闭（零资源占用，开启后鼠标完全穿透不拦截游戏点击）"]
      : level === 1
        ? [`${sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : ""}APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W · C${sys.cpuTempC || "--"}°/G${sys.gpuTempC || "--"}° · ${sys.hasBattery ? `${sys.batteryPercent}%` : "AC"}`]
        : level === 2
          ? [
              `${sys.currentFps > 0 ? `${sys.currentFps} FPS | ` : ""}APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W (目标 ${settings.currentTdpWatts || 45}W)`,
              `GPU ${sys.gpuUsagePercent || 0}% @ ${sys.gfxClockMhz || 0}MHz ${sys.gpuTempC || "--"}°C · VRAM ${vramUsedGb}G`,
              `CPU ${sys.cpuUsagePercent || 0}% ${sys.cpuTempC || "--"}°C · RAM ${sys.usedRamGb || "--"}/${sys.totalRamGb || "--"}GB`
            ]
          : [
              `${getActiveGame()?.title || "HaloDeck"} · ${fpsText}`,
              `APU ${sys.apuPowerWatts?.toFixed(1) || "--"}W · GPU ${sys.gpuUsagePercent || 0}% (${sys.gpuTempC || "--"}°C) · CPU ${sys.cpuUsagePercent || 0}% (${sys.cpuTempC || "--"}°C)`,
              `VRAM ${vramUsedGb}/${vramTotalGb}G (共享${sharedGb}G) · ${formatPowerBadge(sys)}`
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
              纯原生 WPF WS_EX_TRANSPARENT 顶层透明窗口：结合内核无钩子 ETW 帧率采样，不注入游戏进程、不触发反作弊、鼠标点击完全穿透。
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
            <span class="pf-label">当前游戏帧率 (FPS)</span>
            <span id="kpiFps" class="mon-kpi-val accent-blue">${esc(fpsText)}</span>
            <span id="kpiFpsSub" class="mon-kpi-sub">${esc(fpsSubText)}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">APU 封装功耗</span>
            <span id="kpiApuW" class="mon-kpi-val">${sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)} W` : "-- W"}</span>
            <span id="kpiApuSub" class="mon-kpi-sub">设定 ${settings.currentTdpWatts || 40}W · ${sys.adlAvailable ? "ADL2 采样" : "需核显驱动"}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">GPU 占用与频率</span>
            <span id="kpiGpuLoad" class="mon-kpi-val">${sys.gpuUsagePercent || 0}%</span>
            <span id="kpiGpuSub" class="mon-kpi-sub">${sys.gfxClockMhz || 0} MHz · 显存频率 ${sys.memClockMhz || 0} MHz</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">GPU 显存 (专用 + 共享)</span>
            <span id="kpiVram" class="mon-kpi-val">${vramUsedGb} / ${vramTotalGb} GB</span>
            <span id="kpiVramSub" class="mon-kpi-sub">专用 ${sys.vramUsedMb || 0}MB · 共享 ${sys.sharedVramUsedMb || 0}MB (${sharedGb}G)</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">CPU 占用与主频</span>
            <span id="kpiCpuLoad" class="mon-kpi-val">${sys.cpuUsagePercent || 0}%</span>
            <span id="kpiCpuSub" class="mon-kpi-sub">${sys.cpuClockMhz > 0 ? `${sys.cpuClockMhz} MHz` : esc(sys.cpuName || "AMD Ryzen")}</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">CPU / GPU 独立温度</span>
            <span id="kpiCpuTemp" class="mon-kpi-val">CPU ${sys.cpuTempC || "--"}°C</span>
            <span id="kpiGpuTempSub" class="mon-kpi-sub">GPU ${sys.gpuTempC || "--"}°C · 风扇 ${hw.fanRpm || 0} RPM</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">系统内存 (UMA)</span>
            <span id="kpiMem" class="mon-kpi-val">${sys.totalRamGb > 0 ? `${sys.usedRamGb} / ${sys.totalRamGb} GB` : "-- GB"}</span>
            <span class="mon-kpi-sub">提交上限 ${snap.memoryHealth?.commitLimitGb || "--"} GB</span>
          </div>
          <div class="mon-kpi-card">
            <span class="pf-label">整机电源与电池</span>
            <span id="kpiBat" class="mon-kpi-val" style="font-size:15px;">${esc(formatPowerBadge(sys))}</span>
            <span id="kpiBatSub" class="mon-kpi-sub">${sys.estimatedChargerWatts != null ? `充电器输入 ≈ ${sys.estimatedChargerWatts.toFixed(1)}W` : "实时电池供电估算"}</span>
          </div>
        </div>

        <div class="charts-grid">
          <div class="chart-card">
            <div class="hh-card-head" style="margin-bottom:4px;">
              <span class="hh-card-title">最近 60 秒 · APU 功耗 (W) & 温度 (°C)</span>
            </div>
            <div id="svgChartPowerTemp">
              ${buildSvgChart(sys.apuWattsHistory || [], sys.tempHistory || [], 120, 100, "#2dd4bf", "#fbbf24", "APU W", "Temp °C")}
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

  // ---------------- Console View 3: Controls, GPD Back Buttons & App Settings ----------------
  function hasPendingGpdChanges(gpd, draft) {
    if (!gpd?.connected) return false;
    const l4Macro = gpd.l4Mode === 2 || gpd.l4Mode === 6;
    const r4Macro = gpd.r4Mode === 2 || gpd.r4Mode === 6;
    const origL4Combo = l4Macro && Array.isArray(gpd.l4ComboCodes) && gpd.l4ComboCodes.length >= 2
      ? normalizeComboCodesList(gpd.l4ComboCodes)
      : [];
    const origR4Combo = r4Macro && Array.isArray(gpd.r4ComboCodes) && gpd.r4ComboCodes.length >= 2
      ? normalizeComboCodesList(gpd.r4ComboCodes)
      : [];
    const origL4 = l4Macro && !origL4Combo.length ? -1 : (origL4Combo.length ? 0 : (typeof gpd.l4KeyCode === "number" ? gpd.l4KeyCode : 0));
    const origR4 = r4Macro && !origR4Combo.length ? -1 : (origR4Combo.length ? 0 : (typeof gpd.r4KeyCode === "number" ? gpd.r4KeyCode : 0));
    const comboEq = (a, b) => JSON.stringify(normalizeComboCodesList(a || [])) === JSON.stringify(b || []);
    return (
      draft.l4Key !== origL4 ||
      draft.r4Key !== origR4 ||
      !comboEq(draft.l4Combo, origL4Combo) ||
      !comboEq(draft.r4Combo, origR4Combo) ||
      draft.rumble !== (gpd.rumbleLevel ?? 1) ||
      draft.leftDeadzone !== (gpd.leftDeadzone ?? 0) ||
      draft.rightDeadzone !== (gpd.rightDeadzone ?? 0)
    );
  }

  function renderControlsView() {
    const snap = state.snapshot || {};
    const settings = snap.settings || {};
    const gpd = snap.gpdController || {};
    const draft = state.gpdDraft;

    ensureGpdControllerLoaded();

    const qamButtons = settings.qamGamepadCombo?.buttons || ["BACK", "RB"];
    const osdButtons = settings.osdGamepadCombo?.buttons || ["BACK", "Y"];
    const mouseButtons = settings.mouseModeGamepadCombo?.buttons || ["BACK", "LB"];
    const insButtons = settings.insertGamepadCombo?.buttons || ["BACK", "START"];
    const interceptOsk = settings.interceptPhysicalOskButton !== false;
    const dirtyGpd = hasPendingGpdChanges(gpd, draft);

    return `
      <div class="page-scroll">
        <div class="controls-layout">
          <!-- Left: Simplified Back Buttons & Rumble + Desktop Mouse Mode -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">背键与震动 (L4 / R4)</span>
                <div style="display:flex;align-items:center;gap:6px;">
                  <span class="hh-card-badge ${gpd.connected ? "live" : ""}">${gpd.connected ? `已连接 · FW X${gpd.firmwareX || "?"}` : "未检测到手柄"}</span>
                  <button id="btnReadGpd" class="btn btn-sm" data-focusable="true">刷新</button>
                </div>
              </div>

              <div class="hh-field" style="margin-bottom:10px;">
                <label>快速场景预设（选定后点击右下角「应用到手柄」写入 Flash）</label>
                <div class="gpd-preset-grid">
                  ${Object.entries(GPD_BACK_PRESETS).map(([id, p]) => {
                    const activePreset = !draft.l4Combo?.length && !draft.r4Combo?.length && draft.l4Key === p.l4 && draft.r4Key === p.r4;
                    return `<button class="pill-btn small ${activePreset ? "active" : ""}" data-gpd-preset="${id}" data-focusable="true">${esc(p.shortLabel)}</button>`;
                  }).join("")}
                </div>
              </div>

              <div class="backkey-slot-list">
                <div class="backkey-slot-row">
                  <div class="backkey-slot-meta">
                    <strong>L4 左背键</strong>
                    <span>单键映射、实时按键录制或组合键（手柄/键盘）</span>
                  </div>
                  <div class="comp-actions">
                    <span id="l4SlotBadge" class="pill-badge">${esc(resolveBackSlotLabel(draft.l4Key, draft.l4Combo))}</span>
                    <button id="btnPickL4" class="btn btn-sm" data-focusable="true">修改 ▸</button>
                  </div>
                </div>
                <div class="backkey-slot-row">
                  <div class="backkey-slot-meta">
                    <strong>R4 右背键</strong>
                    <span>单键映射、实时按键录制或组合键（手柄/键盘）</span>
                  </div>
                  <div class="comp-actions">
                    <span id="r4SlotBadge" class="pill-badge">${esc(resolveBackSlotLabel(draft.r4Key, draft.r4Combo))}</span>
                    <button id="btnPickR4" class="btn btn-sm" data-focusable="true">修改 ▸</button>
                  </div>
                </div>
              </div>

              <div class="cw-row" style="padding:10px 0 6px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">马达震动</span>
                </div>
                ${renderSegmented("data-gpd-rumble", [
                  { value: "0", label: "关闭" },
                  { value: "1", label: "弱" },
                  { value: "2", label: "强" }
                ], String(draft.rumble), "compact")}
              </div>

              <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;padding-top:8px;border-top:1px solid rgba(255,255,255,0.06);">
                <span style="font-size:11px;color:var(--text-muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">当前芯片：L4 [${esc(gpd.l4KeyLabel || "未知")}] · R4 [${esc(gpd.r4KeyLabel || "未知")}]</span>
                <button id="btnSaveGpd" class="btn btn-sm btn-primary" data-focusable="true" ${!gpd.connected ? "disabled" : ""}>${dirtyGpd ? "应用修改到手柄" : "应用到手柄"}</button>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">手柄桌面鼠标</span>
                ${renderSwitch("swMouseMode", !!settings.gamepadMouseModeEnabled)}
              </div>
              <p style="font-size:12px;color:var(--text-secondary);margin:0;">
                左摇杆移光标 · A 左键 · B 右键 · 右摇杆滚轮 · LB 减速 / RB 加速。
              </p>
            </div>
          </div>

          <!-- Right: Win11/Xbox Touch Keyboard + Essential Shortcuts + Collapsible Advanced -->
          <div class="hh-col">
            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">屏幕键盘 (Win11 / Xbox 手柄布局)</span>
                <button id="btnTestOsk" class="btn btn-sm btn-primary" data-focusable="true">呼出 / 收起键盘</button>
              </div>
              <div class="cw-group">
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">接管机身屏幕键盘按键</span>
                    <span class="cw-row-sub">按机身键盘键直接呼出或收起 Win11 屏幕键盘（首次呼出后可在键盘左上角齿轮切为『游戏手柄』布局），不再弹出老旧传统键盘。</span>
                  </div>
                  ${renderSwitch("swInterceptOsk", interceptOsk)}
                </div>
              </div>
            </div>

            <div class="hh-card">
              <div class="hh-card-head">
                <span class="hh-card-title">常用快捷呼出与启动</span>
                <span class="hh-card-badge">后台托盘全局生效</span>
              </div>
              <div class="hotkey-list">
                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">呼出快捷侧栏</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(qamButtons)}
                    <button id="btnRecordQamCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "qam" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>

                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">切换帧率悬浮窗</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(osdButtons)}
                    <button id="btnRecordOsdCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "osd" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>

                <div class="hotkey-row">
                  <div>
                    <div class="cw-row-title">呼出超分菜单</div>
                  </div>
                  <div class="comp-actions">
                    ${renderKeycaps(insButtons)}
                    <button id="btnRecordInsCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "insert" ? "请按下手柄..." : "录制"}</button>
                  </div>
                </div>
              </div>

              <div class="cw-group" style="margin-top:8px;">
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">开机自启（最小化驻留托盘）</span>
                  </div>
                  ${renderSwitch("setStartup", !!settings.launchAtWindowsStartup)}
                </div>
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">默认全屏控制台</span>
                  </div>
                  ${renderSwitch("setFullscreenBoot", !!settings.launchConsoleFullscreen)}
                </div>
                <div class="cw-row">
                  <div class="cw-row-copy">
                    <span class="cw-row-title">后台自动注入 OptiScaler</span>
                  </div>
                  ${renderSwitch("setAutoInject", !!settings.backgroundAutoInjectEnabled)}
                </div>
              </div>

              <button id="btnToggleControlsAdv" class="btn btn-sm" style="width:100%;margin-top:10px;" data-focusable="true">
                ${state.controlsAdvancedOpen ? "收起高级按键与死区设置 ▴" : "展开高级按键与死区设置 ▾"}
              </button>

              ${state.controlsAdvancedOpen ? `
                <div class="cw-group" style="margin-top:10px;padding-top:10px;border-top:1px solid rgba(255,255,255,0.08);">
                  <div class="cw-row">
                    <div class="cw-row-copy">
                      <span class="cw-row-title">左 / 右摇杆中心死区补偿 (-10..10)</span>
                      <span class="cw-row-sub">调整后需点击左侧「应用到手柄」生效</span>
                    </div>
                    <div style="display:flex;align-items:center;gap:5px;">
                      <button id="btnGpdLdzDec" class="step-btn" data-focusable="true">L−</button>
                      <span style="font-size:11.5px;min-width:34px;text-align:center;">左 ${draft.leftDeadzone}</span>
                      <button id="btnGpdLdzInc" class="step-btn" data-focusable="true">L+</button>
                      <button id="btnGpdRdzDec" class="step-btn" data-focusable="true">R−</button>
                      <span style="font-size:11.5px;min-width:34px;text-align:center;">右 ${draft.rightDeadzone}</span>
                      <button id="btnGpdRdzInc" class="step-btn" data-focusable="true">R+</button>
                    </div>
                  </div>
                  <div class="hotkey-row">
                    <div class="cw-row-title">桌面鼠标组合键</div>
                    <div class="comp-actions">
                      ${renderKeycaps(mouseButtons)}
                      <button id="btnRecordMouseCombo" class="btn btn-sm" data-focusable="true">${state.recordingTarget === "mouse" ? "请按下手柄..." : "录制"}</button>
                    </div>
                  </div>
                  <div class="hotkey-row">
                    <div class="cw-row-title">全局键盘快捷键</div>
                    <div style="display:flex;gap:6px;flex-wrap:wrap;">
                      <button id="btnHotkeyQam" class="btn btn-sm" data-focusable="true">侧栏: ${esc(settings.qamHotkey?.displayText || "Ctrl+Alt+O")}</button>
                      <button id="btnHotkeyOsd" class="btn btn-sm" data-focusable="true">OSD: ${esc(settings.osdHotkey?.displayText || "Ctrl+Alt+M")}</button>
                      <button id="btnHotkeyIns" class="btn btn-sm" data-focusable="true">菜单: ${esc(settings.insertHotkey?.displayText || "Ctrl+Alt+I")}</button>
                    </div>
                  </div>
                  <div class="cw-row">
                    <div class="cw-row-copy">
                      <span class="cw-row-title">立即扫描一次运行中游戏并注入</span>
                    </div>
                    <button id="btnRunAutoInject" class="btn btn-sm" data-focusable="true">立即扫描</button>
                  </div>
                </div>
              ` : ""}
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

              <div class="console-target-card">
                <div class="console-target-info">
                  <span style="font-size:11px;color:var(--text-muted);">目标游戏选择（二级完整游戏库）</span>
                  <strong style="font-size:13px;color:#fff;">${active ? esc(active.title) : "暂未选择目标"} · 共检测到 ${games.length} 款游戏</strong>
                </div>
                <div style="display:flex;gap:6px;flex-wrap:wrap;">
                  <button id="btnOpenConsoleGamePicker" class="btn btn-sm btn-primary" data-focusable="true">切换目标游戏 (${games.length}) ▸</button>
                  <button id="btnAddExeInline" class="btn btn-sm" data-focusable="true">+ 添加 EXE</button>
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
    const daemonVer = hw.daemonVersion || 0;
    const vramUsedGb = ((sys.vramUsedMb || 0) / 1024).toFixed(1);
    const vramTotalGb = sys.vramTotalMb > 0 ? (sys.vramTotalMb / 1024).toFixed(0) : "16";
    const sharedGb = ((sys.sharedVramUsedMb || 0) / 1024).toFixed(1);

    if (state.qamTab === "handheld") {
      const curTdp = settings.currentTdpWatts || 40;
      const customPresets = (settings.customTdpPresets && settings.customTdpPresets.length)
        ? settings.customTdpPresets
        : [15, 25, 35, 45, 60, 80, 100];
      const fanMode = normalizeFanModeUi(settings.fanMode);
      const manualFanPct = settings.manualFanPercent || 55;
      const fanCurves = (settings.fanCurves && settings.fanCurves.length) ? settings.fanCurves : getDefaultFanCurves();
      const activeCurve = getActiveFanCurve(settings);
      const maxTemp = Math.max(sys.gpuTempC || 0, sys.cpuTempC || 0);
      const curTempEval = maxTemp > 0 ? maxTemp : 48;
      const curveEvalPct = evaluateFanCurvePct(curTempEval, activeCurve.points);
      const apuStr = sys.apuPowerWatts != null ? `${sys.apuPowerWatts.toFixed(1)}W` : "--W";
      const rpm = hw.fanRpm || 0;
      const hzOptions = (sys.supportedRefreshRates && sys.supportedRefreshRates.length)
        ? sys.supportedRefreshRates.map((h) => ({ value: String(h), label: `${h}Hz` }))
        : [{ value: "60", label: "60Hz" }, { value: "120", label: "120Hz" }];
      const curRes = `${sys.screenWidth || 1920}x${sys.screenHeight || 1080}`;
      const resOptions = [
        { value: "1920x1080", label: "1080p" },
        { value: "1600x900", label: "900p" },
        { value: "1280x720", label: "720p" }
      ];
      const perGameProfiles = settings.perGameProfiles || {};
      const gamePerf = active ? (perGameProfiles[active.id] || {
        gameId: active.id,
        gameName: active.title,
        enabled: false,
        tdpWatts: curTdp,
        refreshRateHz: sys.refreshRateHz || 120
      }) : null;

      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>掌机状态实时流</span>
              <span id="qamPowerChip" class="hh-card-badge ${hw.serviceRunning ? "live" : ""}">${sys.currentFps > 0 ? `${sys.currentFps} FPS · ` : ""}APU ${apuStr}</span>
            </div>
            <div class="qam-strip-grid">
              <div class="qam-mini-kpi">
                <span class="pf-label">APU 实测 / 目标</span>
                <strong id="qamStripApu">${apuStr} / ${curTdp}W</strong>
              </div>
              <div class="qam-mini-kpi">
                <span class="pf-label">电源与续航</span>
                <strong id="qamStripBat">${esc(formatPowerBadge(sys))}</strong>
              </div>
              <div class="qam-mini-kpi">
                <span class="pf-label">CPU/GPU 温度 · 风扇</span>
                <strong id="qamStripTherm">C${sys.cpuTempC || "--"}° G${sys.gpuTempC || "--"}° · ${rpm}R</strong>
              </div>
            </div>
            ${!hw.serviceRunning ? `
              <button id="btnInstallHwSvc" class="btn btn-sm btn-primary" style="width:100%;margin-top:8px;" data-focusable="true">一键启用 TDP / 风扇直控</button>
            ` : (daemonVer < 3 ? `
              <button id="btnInstallHwSvc" class="btn btn-sm" style="width:100%;margin-top:8px;" data-focusable="true">${daemonVer < 2 ? "升级硬件服务 (解锁 PM-Table / 120W)" : "升级硬件服务 v3 (解锁 FPS / SMN CPU温度)"}</button>
            ` : "")}
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>开放 APU 功耗 (8–120W)</span>
              <strong id="qamTdpReadout">${curTdp}W${hw.hwStapmLimitWatts != null ? ` (实读 ${hw.hwStapmLimitWatts.toFixed(0)}W)` : ""}</strong>
            </div>
            <div class="qam-preset-row">
              ${customPresets.slice(0, 6).map((w) => `
                <button class="pill-btn qam-pill ${curTdp === w ? "active" : ""}" data-tdp-watt="${w}" data-focusable="true">${w}W</button>
              `).join("")}
            </div>
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:10px;">
              <button id="btnTdpDec" class="step-btn qam-step" data-focusable="true">−1W</button>
              <input id="tdpRangeInput" class="hh-range" type="range" min="8" max="120" step="1" value="${curTdp}" style="flex:1;" />
              <button id="btnTdpInc" class="step-btn qam-step" data-focusable="true">+1W</button>
            </div>
            <div class="cw-row qam-compact-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">插拔电自动切换 TDP</span>
                <span class="cw-row-sub">离电 ${settings.batteryTdpWatts || 25}W · 插电 ${settings.acTdpWatts || 55}W</span>
              </div>
              ${renderSwitch("swPowerLinkage", !!settings.powerLinkageEnabled)}
            </div>
            ${active && gamePerf ? `
              <div class="cw-row qam-compact-row" style="margin-top:4px;">
                <div class="cw-row-copy">
                  <span class="cw-row-title">专属档案 · ${esc(active.title)}</span>
                  <span class="cw-row-sub">${gamePerf.enabled ? `已启用 (${gamePerf.tdpWatts || curTdp}W)` : "点击开关绑定该游戏 TDP"}</span>
                </div>
                ${renderSwitch("swGamePerfEnabled", !!gamePerf.enabled)}
              </div>` : ""}
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>风扇控制 (GPD 双风扇 EC)</span>
              <span id="fanRpmBadge" class="hh-card-badge ${hw.ecReady ? "live" : ""}">${rpm} RPM</span>
            </div>
            <div class="qam-fan-row">
              ${[
                { value: "auto", label: "EC 自动" },
                { value: "manual", label: "直接 %" },
                { value: "curve", label: "温度曲线" }
              ].map((opt) => `
                <button class="pill-btn qam-pill ${fanMode === opt.value ? "active" : ""}" data-fan-mode="${opt.value}" data-focusable="true">${opt.label}</button>
              `).join("")}
            </div>
            <div style="display:flex;align-items:center;gap:8px;margin-top:8px;">
              <button id="btnFanPctDec" class="step-btn qam-step" data-focusable="true">−5%</button>
              <input id="fanManualRange" class="hh-range" type="range" min="20" max="100" step="1" value="${manualFanPct}" style="flex:1;" />
              <span id="fanManualVal" style="font-size:12px;font-weight:700;min-width:36px;text-align:right;">${manualFanPct}%</span>
              <button id="btnFanPctInc" class="step-btn qam-step" data-focusable="true">+5%</button>
            </div>
            <div class="qam-fan-quick-row">
              ${[35, 50, 65, 80, 100].map((p) => `
                <button class="pill-btn qam-pill ${fanMode === "manual" && manualFanPct === p ? "active" : ""}" data-fan-quick-pct="${p}" data-focusable="true">${p}%</button>
              `).join("")}
            </div>
            ${fanMode === "curve" ? `
              <div class="qam-curve-row">
                <div class="qam-preset-row wrap" style="margin:6px 0 4px;">
                  ${fanCurves.map((c) => `
                    <button class="pill-btn qam-pill ${activeCurve.id === c.id ? "active" : ""}" data-select-fan-curve="${esc(c.id)}" data-focusable="true">${esc(c.name.split(" ")[0])}</button>
                  `).join("")}
                </div>
                <span style="font-size:11px;color:var(--text-secondary);">曲线实时目标：${curTempEval}°C → ${curveEvalPct}%</span>
              </div>` : ""}
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>显示 · CPU 调度 · 亮度与音量</span>
              <span style="font-size:11px;color:var(--text-secondary);">${sys.refreshRateHz || "--"}Hz · ${formatResShort(curRes)}</span>
            </div>
            <div style="display:flex;flex-direction:column;gap:7px;margin-bottom:8px;">
              ${renderSegmented("data-refresh-hz", hzOptions, String(sys.refreshRateHz || 120), "compact")}
              ${renderSegmented("data-res-mode", resOptions, curRes, "compact")}
              ${renderSegmented("data-cpu-boost", [
                { value: "off", label: "关睿频" },
                { value: "efficient", label: "平衡调度" },
                { value: "aggressive", label: "满血睿频" }
              ], sys.cpuBoostMode || "aggressive", "compact")}
            </div>
            <div style="display:flex;flex-direction:column;gap:7px;">
              ${renderStepSlider({ idPrefix: "bright", value: sys.brightnessPercent ?? 70, min: 5, displayText: sys.brightnessSupported ? `亮度 ${sys.brightnessPercent}%` : "不支持亮度", disabled: !sys.brightnessSupported })}
              ${renderStepSlider({ idPrefix: "vol", value: sys.volumePercent ?? 50, min: 0, muted: !!sys.isMuted, muteBtnId: "btnToggleMute" })}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>掌机快捷工具</span>
              <span style="font-size:11px;color:var(--text-muted);">${frozen.length > 0 ? `已冻结 ${frozen.length}` : "随点随用"}</span>
            </div>
            <div class="qam-toolbox-mini">
              <button class="pill-btn qam-pill" data-toolbox="osk" data-focusable="true">${settings.preferredOskMode === "xbox" && sys.gpdXboxKeyboardAvailable ? "Xbox 屏幕键盘" : "屏幕键盘"}</button>
              <button id="btnToolboxMouse" class="pill-btn qam-pill ${settings.gamepadMouseModeEnabled ? "active" : ""}" data-focusable="true">手柄鼠标 ${settings.gamepadMouseModeEnabled ? "开" : "关"}</button>
              <button class="pill-btn qam-pill ${frozen.length > 0 ? "active" : ""}" data-toolbox="toggle_fridge" data-focusable="true">游戏冰箱${frozen.length ? ` (${frozen.length})` : ""}</button>
              <button class="pill-btn qam-pill" data-toolbox="show_desktop" data-focusable="true">返回桌面</button>
              <button class="pill-btn qam-pill" data-toolbox="task_view" data-focusable="true">切换窗口</button>
              <button id="btnKillGame" class="pill-btn qam-pill ${state.killArmed ? "btn-armed-danger" : ""}" data-focusable="true">${state.killArmed ? "确认强关?" : "强关游戏"}</button>
            </div>
          </div>
        </div>
      `;
    }

    if (state.qamTab === "osd") {
      const level = settings.overlayLevel ?? 0;
      const pos = settings.overlayPosition || "top-center";
      const opacity = settings.overlayOpacity ?? 0.86;
      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>无钩子穿透式 OSD 档位 (0–4 档)</span>
              <span class="hh-card-badge ${level > 0 ? "live" : ""}">${level === 0 ? "已关闭" : `档位 ${level}`}</span>
            </div>
            <div class="osd-level-pills" style="margin-bottom:8px;">
              ${[0, 1, 2, 3, 4].map((v) => `
                <button class="pill-btn qam-pill ${level === v ? "active" : ""}" data-osd-level="${v}" data-focusable="true">${v === 0 ? "关" : `${v}级`}</button>
              `).join("")}
            </div>
            <div style="display:flex;flex-direction:column;gap:7px;">
              ${renderSegmented("data-osd-pos", [
                { value: "top-left", label: "左上" },
                { value: "top-center", label: "顶中" },
                { value: "top-right", label: "右上" }
              ], pos, "compact")}
              ${renderSegmented("data-osd-opacity", [
                { value: "0.55", label: "55%透" },
                { value: "0.75", label: "75%透" },
                { value: "0.88", label: "88%透" },
                { value: "1", label: "100%" }
              ], String(opacity >= 0.95 ? 1 : opacity >= 0.82 ? 0.88 : opacity >= 0.65 ? 0.75 : 0.55), "compact")}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>实时掌机核心指标</span></div>
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
                <span class="pf-label">GPU 负载/频率</span>
                <strong id="qamKpiGpu">${sys.gpuUsagePercent || 0}% · ${sys.gfxClockMhz || 0}M</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">CPU / GPU 温度</span>
                <strong id="qamKpiTemp">C ${sys.cpuTempC || "--"}° / G ${sys.gpuTempC || "--"}°</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">GPU 显存 (专+享)</span>
                <strong id="qamKpiVram">${vramUsedGb}/${vramTotalGb}G (+${sharedGb}G)</strong>
              </div>
              <div class="qam-kpi-card">
                <span class="pf-label">UMA 内存 · 风扇</span>
                <strong id="qamKpiMem">${sys.usedRamGb || "--"}G · ${hw.fanRpm || 0}RPM</strong>
              </div>
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title"><span>60s 功耗与 GPU 负载曲线</span></div>
            <div id="qamOsdSparkline" style="margin-bottom:8px;">
              ${buildSvgChart(sys.apuWattsHistory || [], sys.gpuUsageHistory || [], 85, 100, "#2dd4bf", "#38bdf8", "APU W", "GPU %")}
            </div>
            <div class="qam-section-title" style="margin-bottom:4px;"><span>60s CPU 负载与温度曲线</span></div>
            <div id="qamOsdSparkline2">
              ${buildSvgChart(sys.cpuUsageHistory || [], sys.tempHistory || [], 100, 100, "#a78bfa", "#fbbf24", "CPU %", "Temp °C")}
            </div>
          </div>
        </div>
      `;
    }

    if (state.qamTab === "controls") {
      ensureGpdControllerLoaded();
      const gpd = snap.gpdController || {};
      const draft = state.gpdDraft;
      const interceptOsk = settings.interceptPhysicalOskButton !== false;
      const dirtyGpd = hasPendingGpdChanges(gpd, draft);

      return `
        <div class="qam-body">
          <div class="qam-section">
            <div class="qam-section-title">
              <span>背键快捷切换</span>
              <span class="hh-card-badge ${gpd.connected ? "live" : ""}">${gpd.connected ? `FW X${gpd.firmwareX || "?"}` : "未检测手柄"}</span>
            </div>
            <div class="gpd-warn-note" style="margin-bottom:8px;">
              当前：L4 [${esc(gpd.l4KeyLabel || "未知")}] · R4 [${esc(gpd.r4KeyLabel || "未知")}]
            </div>
            <div class="qam-toolbox-mini" style="margin-bottom:8px;">
              ${Object.entries(GPD_BACK_PRESETS).map(([id, p]) => {
                const activePreset = !draft.l4Combo?.length && !draft.r4Combo?.length && draft.l4Key === p.l4 && draft.r4Key === p.r4;
                return `<button class="pill-btn qam-pill ${activePreset ? "active" : ""}" data-gpd-preset="${id}" data-focusable="true">${esc(p.shortLabel)}</button>`;
              }).join("")}
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
            <button id="btnSaveGpd" class="btn btn-sm btn-primary" style="width:100%;" data-focusable="true" ${!gpd.connected ? "disabled" : ""}>${dirtyGpd ? "应用修改到手柄" : "应用到手柄"}</button>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>屏幕键盘</span>
              <span class="hh-card-badge live">Win11 / Xbox</span>
            </div>
            <button id="btnTestOsk" class="btn btn-sm btn-primary" style="width:100%;margin-bottom:8px;" data-focusable="true">呼出 / 收起屏幕键盘</button>
            <div class="cw-row qam-compact-row">
              <div class="cw-row-copy">
                <span class="cw-row-title">接管机身键盘键</span>
                <span class="cw-row-sub">改用 Win11 触摸/手柄布局，屏蔽老旧键盘</span>
              </div>
              ${renderSwitch("swInterceptOsk", interceptOsk)}
            </div>
          </div>

          <div class="qam-section">
            <div class="qam-section-title">
              <span>手柄鼠标与快捷操作</span>
              ${renderSwitch("swQamMouseMode", !!settings.gamepadMouseModeEnabled)}
            </div>
            <p style="font-size:11.5px;color:var(--text-secondary);margin:0 0 8px;">
              左摇杆移光标 · A 左键 · B 右键 · 右摇杆滚轮 · LB 降速 / RB 加速。
            </p>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;">
              <button id="btnSendInsert" class="btn btn-sm" data-focusable="true">发送 Insert 菜单</button>
              <button id="btnQamToConsoleControls" class="btn btn-sm" data-focusable="true">更多按键设置</button>
            </div>
          </div>
        </div>
      `;
    }

    // QAM Tab 4: Multi-level OptiScaler Tuning <-> Game Picker Drill-Down
    if (state.qamOptiSubView === "games") {
      return `
        <div class="qam-body">
          <div class="qam-game-picker-view">
            <div class="qam-picker-head">
              <button id="btnQamBackToTune" class="btn btn-sm" data-focusable="true">‹ 返回调优</button>
              <strong style="font-size:12.5px;color:#fff;">选择目标 (${games.length})</strong>
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
    const statusText = inj?.isInjected
      ? `已注入 · ${formatConfigSummary(inj.appliedConfig || cfg, true)}`
      : "当前目标原生未注入（点击下方切换目标进入游戏选择页）";

    return `
      <div class="qam-body">
        <div id="qamStatusBanner" class="qam-target-card ${inj?.isInjected ? "injected" : "uninjected"}">
          <div style="display:flex;align-items:center;justify-content:space-between;gap:6px;">
            <strong style="font-size:13.5px;color:#fff;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${esc(active?.title || "未选择游戏")}</strong>
            <span class="hh-card-badge ${inj?.isInjected ? "live" : "warn"}">${inj?.isInjected ? "已注入" : "原生未注入"}</span>
          </div>
          <span style="font-size:11.5px;color:var(--text-secondary);">${esc(statusText)}</span>
          <button id="btnQamOpenGamePicker" class="btn btn-sm" style="width:100%;margin-top:4px;justify-content:space-between;" data-focusable="true">
            <span>切换游戏 (${games.length})</span>
            <span>▸</span>
          </button>
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

        <div class="qam-section">
          <div class="qam-section-title"><span>兼容增强与快速诊断</span></div>
          <div class="cw-row qam-compact-row" style="margin-bottom:8px;">
            <div class="cw-row-copy">
              <span class="cw-row-title">FakeNVAPI 兼容层</span>
              <span class="cw-row-sub">解锁 Reflex 低延迟与 DLSS-FG 转换通道</span>
            </div>
            ${renderSwitch("swFakenvapi", !!cfg.enableFakenvapi)}
          </div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;">
            <button id="btnLaunchGame" class="btn btn-sm" data-focusable="true" ${!active ? "disabled" : ""}>启动游戏</button>
            <button id="btnInspectLog" class="btn btn-sm" data-focusable="true" ${!active ? "disabled" : ""}>诊断与日志</button>
          </div>
          ${!snap.vrrWindowedEnabled ? `<button id="btnEnableVrr" class="btn btn-sm" style="width:100%;margin-top:6px;" data-focusable="true">一键开启 Win11 窗口化 VRR</button>` : ""}
        </div>

        <div style="display:flex;flex-direction:column;gap:7px;">
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
    applySpatialFocus(false);
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

    const hw = state.snapshot?.systemControl?.hardwareService || {};
    const consoleBadge = document.getElementById("consoleTdpBadge");
    if (consoleBadge) {
      consoleBadge.textContent = hw.smuReady ? `SMU ${w}W` : `设定 ${w}W`;
    }
    const qamReadout = document.getElementById("qamTdpReadout");
    if (qamReadout) {
      qamReadout.textContent = hw.hwStapmLimitWatts != null ? `${w}W (目标 ${w}W)` : `${w}W`;
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
          showToast(res?.message || `风扇模式已切换为 ${formatFanModeLabel(mode)}`, res?.hardwareApplied ? "success" : "info");
        } catch (err) {
          showToast(err.message, "error");
        }
      });
    });

    const applyDirectFanPct = async (pct) => {
      const clamped = Math.max(20, Math.min(100, Math.round(pct)));
      try {
        const res = await rpc("setFanMode", { mode: "manual", manualPercent: clamped });
        showToast(res?.message || `风扇直接百分比已设为 ${clamped}%`, res?.hardwareApplied ? "success" : "info");
      } catch (err) {
        showToast(err.message, "error");
      }
    };

    const fanRange = document.getElementById("fanManualRange");
    fanRange?.addEventListener("input", () => {
      const val = document.getElementById("fanManualVal");
      if (val) val.textContent = `${fanRange.value}%`;
    });
    fanRange?.addEventListener("change", () => applyDirectFanPct(Number(fanRange.value)));
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

    document.getElementById("btnInstallHwSvc")?.addEventListener("click", async () => {
      try {
        showToast("正在请求一次性管理员授权更新 HaloDeckHardwareSvc...");
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
      const curW = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.tdpWatts || state.snapshot?.settings?.currentTdpWatts || 40;
      saveGamePerf({ tdpWatts: Math.max(8, curW - 1) });
    });
    document.getElementById("btnGamePerfTdpInc")?.addEventListener("click", () => {
      const active = getActiveGame();
      const curW = state.snapshot?.settings?.perGameProfiles?.[active?.id]?.tdpWatts || state.snapshot?.settings?.currentTdpWatts || 40;
      saveGamePerf({ tdpWatts: Math.min(120, curW + 1) });
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
    document.querySelectorAll("[data-gpd-preset]").forEach((el) => {
      el.addEventListener("click", async () => {
        const preset = GPD_BACK_PRESETS[el.dataset.gpdPreset];
        if (!preset) return;
        state.gpdDraft.l4Key = preset.l4;
        state.gpdDraft.r4Key = preset.r4;
        state.gpdDraft.l4Combo = [];
        state.gpdDraft.r4Combo = [];
        if (preset.enableHotkeys && state.snapshot?.settings && state.snapshot.settings.enableBackButtonHotkeys === false) {
          await rpc("saveSettings", { ...state.snapshot.settings, enableBackButtonHotkeys: true });
        }
        showToast(`已选中预设「${preset.shortLabel || preset.label}」，请点「应用到手柄」生效`, "info");
        render();
      });
    });
    document.getElementById("swInterceptOsk")?.addEventListener("click", async () => {
      const s = state.snapshot?.settings;
      if (!s) return;
      const next = !(s.interceptPhysicalOskButton !== false);
      await rpc("saveSettings", { ...s, interceptPhysicalOskButton: next });
      showToast(next ? "已开启机身键盘键 (Ctrl+Win+O) 接管" : "已恢复系统默认键盘键响应", "info");
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
      rpc("showConsole");
    });
    document.getElementById("btnSaveGpd")?.addEventListener("click", async () => {
      try {
        const l4Combo = Array.isArray(state.gpdDraft.l4Combo) ? normalizeComboCodesList(state.gpdDraft.l4Combo) : [];
        const r4Combo = Array.isArray(state.gpdDraft.r4Combo) ? normalizeComboCodesList(state.gpdDraft.r4Combo) : [];
        const l4Single = l4Combo.length === 1 ? l4Combo[0] : (l4Combo.length >= 2 ? null : (state.gpdDraft.l4Key >= 0 ? state.gpdDraft.l4Key : null));
        const r4Single = r4Combo.length === 1 ? r4Combo[0] : (r4Combo.length >= 2 ? null : (state.gpdDraft.r4Key >= 0 ? state.gpdDraft.r4Key : null));
        const usesHaloDeckFn = [0x0068, 0x0069, 0x006A].includes(l4Single) ||
          [0x0068, 0x0069, 0x006A].includes(r4Single) ||
          l4Combo.some((c) => c >= 0x0068 && c <= 0x006A) ||
          r4Combo.some((c) => c >= 0x0068 && c <= 0x006A);
        if (usesHaloDeckFn && state.snapshot?.settings && state.snapshot.settings.enableBackButtonHotkeys === false) {
          await rpc("saveSettings", { ...state.snapshot.settings, enableBackButtonHotkeys: true });
        }
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
            <div class="gpd-key-group-label">手柄按键（手柄模式下直接参与游戏）</div>
            <div class="backkey-picker-grid">
              ${GPD_BACK_GAMEPAD_OPTIONS.map((opt) => `
                <button class="pill-btn small ${singleSelectedVal === opt.value ? "active" : ""}" data-pick-back-key="${opt.value}" data-pick-slot="${slot}" data-focusable="true">${esc(opt.label)}</button>
              `).join("")}
            </div>
          </div>
          <div class="backkey-picker-section">
            <div class="gpd-key-group-label">快捷功能与常用键盘键</div>
            <div class="backkey-picker-grid">
              ${GPD_BACK_KEYBOARD_OPTIONS.map((opt) => `
                <button class="pill-btn small ${singleSelectedVal === opt.value ? "active" : ""}" data-pick-back-key="${opt.value}" data-pick-slot="${slot}" data-focusable="true">${esc(opt.label)}</button>
              `).join("")}
            </div>
          </div>
        `;
      } else if (bm.tab === "record") {
        const recChips = bm.recordedCodes.length
          ? bm.recordedCodes.map((c) => `<span class="combo-chip-pill">${esc(resolveShortHidLabel(c))}</span>`).join(`<span class="keycap-plus">+</span>`)
          : `<span style="font-size:12px;color:var(--text-muted);">${bm.recordingActive ? "等待按键中：可直接同时按下手柄键（如 LB+RB / LT+RT）或键盘组合键（如 Ctrl+Shift+O）..." : "点击下方「开始录制」，按下单键或最多 4 个组合键后松开"}</span>`;
        tabBody = `
          <div class="backkey-picker-section">
            <div class="gpd-key-group-label">实时录制手柄组合或键盘快捷键（最多 4 键）</div>
            <div id="recordStageBox" class="record-stage-box ${bm.recordingActive ? "recording" : ""}">
              <div class="combo-preview-bar">${recChips}</div>
              <div style="font-size:11px;color:${bm.recordingActive ? "#8dc7ff" : "var(--text-muted)"};">
                ${bm.recordingActive ? "● 正在监听物理手柄与键盘输入（按下全部目标键后松开即可自动完成）" : (bm.recordedCodes.length ? `已捕获 ${bm.recordedCodes.length} 键，点击右侧按钮确认使用` : "支持手柄按键/扳机与键盘修饰键混搭录制")}
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
          : `<span style="font-size:11.5px;color:var(--text-muted);">请从下方预设直接选取，或依次点选最多 4 个手柄/键盘按键自由组合</span>`;
        tabBody = `
          <div class="backkey-picker-section">
            <div class="gpd-key-group-label">常用组合键预设（点选立即生效到草稿）</div>
            <div class="backkey-picker-grid">
              ${GPD_BACK_COMBO_PRESETS.map((p) => `
                <button class="pill-btn small" data-combo-preset="${p.id}" data-focusable="true">${esc(p.label)}</button>
              `).join("")}
            </div>
          </div>
          <div class="backkey-picker-section">
            <div class="gpd-key-group-label">自由组合构建器（已选 ${cList.length}/4 键，支持手柄组合与键鼠组合）</div>
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
            <div class="gpd-key-group-label" style="margin-top:4px;">键盘修饰键</div>
            <div class="backkey-picker-grid">
              ${GPD_COMBO_MODIFIER_OPTIONS.map((opt) => `
                <button class="pill-btn small ${cList.includes(opt.value) ? "active" : ""}" data-combo-toggle-code="${opt.value}" data-focusable="true">${esc(opt.label)}</button>
              `).join("")}
            </div>
            <div class="gpd-key-group-label" style="margin-top:4px;">常用功能 / 字母键</div>
            <div class="backkey-picker-grid">
              ${GPD_COMBO_EXTRA_KEYS.map((opt) => `
                <button class="pill-btn small ${cList.includes(opt.value) ? "active" : ""}" data-combo-toggle-code="${opt.value}" data-focusable="true">${esc(opt.label)}</button>
              `).join("")}
            </div>
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

      body.querySelectorAll("[data-combo-preset]").forEach((btn) => {
        btn.addEventListener("click", () => {
          const preset = GPD_BACK_COMBO_PRESETS.find((p) => p.id === btn.dataset.comboPreset);
          if (preset) applyBackSlotCombo(slot, preset.codes);
        });
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
        moveSpatial(action);
        break;
      case "A": {
        const items = getFocusableItems();
        items[state.focusIndex]?.click();
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
      if (state.qamTab !== "opti") {
        state.qamOptiSubView = "tune";
      }
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
