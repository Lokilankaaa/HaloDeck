/* HaloDeck Community Plugin SDK v1 (Decky-style sandboxed iframe bridge) */
(function () {
  "use strict";

  const pending = new Map();
  const eventListeners = new Set();
  const gamepadListeners = new Set();
  const themeListeners = new Set();
  let reqSeq = 0;
  let cachedContext = null;
  let readyResolve = null;
  const readyPromise = new Promise((resolve) => {
    readyResolve = resolve;
  });

  function applyThemePayload(payload) {
    if (!payload) return;
    if (payload.theme) {
      document.documentElement.dataset.theme = payload.theme;
    }
    if (payload.surface) {
      document.documentElement.dataset.surface = payload.surface;
    }
    if (payload.cssVars && typeof payload.cssVars === "object") {
      const root = document.documentElement.style;
      for (const [k, v] of Object.entries(payload.cssVars)) {
        if (k.startsWith("--") && typeof v === "string") {
          root.setProperty(k, v);
        }
      }
    }
    themeListeners.forEach((fn) => {
      try { fn(payload); } catch {}
    });
  }

  function callHost(action, args) {
    return new Promise((resolve, reject) => {
      const id = `hp_${++reqSeq}`;
      pending.set(id, { resolve, reject });
      window.parent.postMessage({
        __halodeckPlugin: true,
        id,
        action,
        args: args || {}
      }, "*");
    });
  }

  window.addEventListener("message", (event) => {
    const msg = event.data;
    if (!msg || typeof msg !== "object") return;

    if (msg.__halodeckPluginReply && msg.id && pending.has(msg.id)) {
      const slot = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.ok) slot.resolve(msg.result);
      else slot.reject(new Error(msg.error || "HaloDeck plugin API error"));
      return;
    }

    if (msg.__halodeckPluginTheme) {
      applyThemePayload(msg);
      return;
    }

    if (msg.__halodeckPluginEvent) {
      eventListeners.forEach((fn) => {
        try { fn(msg.eventName, msg.data); } catch {}
      });
      return;
    }

    if (msg.__halodeckPluginGamepad && msg.action) {
      let handled = false;
      gamepadListeners.forEach((fn) => {
        try {
          if (fn(msg.action) === true) handled = true;
        } catch {}
      });
      if (!handled) {
        handleDefaultGamepadAction(msg.action);
      }
    }
  });

  // Built-in automatic gamepad spatial navigator for ALL sandboxed plugins:
  // every standard button / input / range / textarea / [data-focusable] works with D-Pad + A out of the box!
  let gpFocusEl = null;
  let gpStyleInjected = false;

  function ensureGpStyle() {
    if (gpStyleInjected || !document.head) return;
    gpStyleInjected = true;
    const st = document.createElement("style");
    st.textContent = `.hd-gp-focus{outline:2px solid var(--accent,#1a8cff) !important;outline-offset:2px !important;box-shadow:0 0 0 4px var(--accent-soft,rgba(26,140,255,0.22)) !important;}`;
    document.head.appendChild(st);
  }

  function getFocusableList() {
    const nodes = Array.from(document.querySelectorAll(
      'button:not(:disabled), input:not(:disabled):not([type="hidden"]), textarea:not(:disabled), select:not(:disabled), [data-focusable="true"]'
    ));
    return nodes.filter((el) => {
      const r = el.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0) return false;
      const cs = getComputedStyle(el);
      return cs.visibility !== "hidden" && cs.display !== "none";
    });
  }

  function setGpFocus(el) {
    ensureGpStyle();
    if (gpFocusEl && gpFocusEl !== el) {
      gpFocusEl.classList.remove("hd-gp-focus");
    }
    gpFocusEl = el || null;
    if (gpFocusEl) {
      gpFocusEl.classList.add("hd-gp-focus");
      try { gpFocusEl.scrollIntoView({ block: "nearest", inline: "nearest" }); } catch {}
    }
  }

  function handleDefaultGamepadAction(rawAction) {
    const act = String(rawAction || "").toUpperCase();
    const list = getFocusableList();
    if (list.length === 0) return;

    let idx = gpFocusEl ? list.indexOf(gpFocusEl) : -1;
    if (idx < 0) {
      setGpFocus(list[0]);
      if (act !== "A" && act !== "CONFIRM") return;
      idx = 0;
    }

    const cur = list[idx];
    if (cur && cur.tagName === "INPUT" && cur.type === "range" && (act === "LEFT" || act === "RIGHT")) {
      const min = Number(cur.min || 0);
      const max = Number(cur.max || 100);
      const step = Number(cur.step || Math.max(1, Math.round((max - min) / 24)));
      const nextVal = Math.min(max, Math.max(min, Number(cur.value || 0) + (act === "RIGHT" ? step : -step)));
      cur.value = String(nextVal);
      cur.dispatchEvent(new Event("input", { bubbles: true }));
      cur.dispatchEvent(new Event("change", { bubbles: true }));
      return;
    }

    if (act === "A" || act === "CONFIRM") {
      if (cur.tagName === "TEXTAREA" || (cur.tagName === "INPUT" && cur.type !== "range" && cur.type !== "checkbox")) {
        cur.focus();
      } else {
        cur.click();
      }
      return;
    }

    if (act === "DOWN" || act === "RIGHT") {
      setGpFocus(list[(idx + 1) % list.length]);
    } else if (act === "UP" || act === "LEFT") {
      setGpFocus(list[(idx - 1 + list.length) % list.length]);
    }
  }

  window.addEventListener("pointerdown", () => {
    if (gpFocusEl) {
      gpFocusEl.classList.remove("hd-gp-focus");
      gpFocusEl = null;
    }
  }, true);

  const api = {
    async ready() {
      if (cachedContext) return cachedContext;
      const ctx = await callHost("getContext", {});
      cachedContext = ctx;
      if (ctx && ctx.theme) {
        document.documentElement.dataset.theme = ctx.theme;
      }
      if (readyResolve) {
        readyResolve(ctx);
        readyResolve = null;
      }
      return ctx;
    },

    getContext() {
      return callHost("getContext", {});
    },

    getGames() {
      return callHost("getGames", {});
    },

    getRunningGame() {
      return callHost("getRunningGame", {});
    },

    getTelemetry() {
      return callHost("getTelemetry", {});
    },

    storage: {
      async get(key) {
        const res = await callHost("storageGet", { key });
        return res ? res.value : null;
      },
      set(key, value) {
        return callHost("storageSet", { key, value });
      },
      remove(key) {
        return callHost("storageRemove", { key });
      }
    },

    backend: {
      call(method, params, timeoutMs) {
        return callHost("backendCall", {
          method,
          params: params === undefined ? {} : params,
          timeoutMs: timeoutMs || 30000
        });
      }
    },

    openDataFolder() {
      return callHost("openDataFolder", {});
    },

    theme: {
      applyVariables(variables) {
        return callHost("themeApply", { variables: variables || {} });
      },
      setVariables(variables) {
        return callHost("themeApply", { variables: variables || {} });
      },
      reset() {
        return callHost("themeReset", {});
      }
    },

    games: {
      list() {
        return callHost("getGames", {});
      },
      getRunning() {
        return callHost("getRunningGame", {});
      }
    },

    telemetry: {
      get() {
        return callHost("getTelemetry", {});
      }
    },

    shell: {
      openDataFolder() {
        return callHost("openDataFolder", {});
      }
    },

    ui: {
      toast(message, kind) {
        window.parent.postMessage({
          __halodeckPlugin: true,
          action: "toast",
          args: { message: String(message || ""), kind: kind || "info" }
        }, "*");
      }
    },

    sendKey(vk) {
      return callHost("sendKey", { vk });
    },

    toast(message, kind) {
      window.parent.postMessage({
        __halodeckPlugin: true,
        action: "toast",
        args: { message: String(message || ""), kind: kind || "info" }
      }, "*");
    },

    onEvent(handler) {
      if (typeof handler === "function") eventListeners.add(handler);
      return () => eventListeners.delete(handler);
    },

    on(eventName, handler) {
      if (typeof handler !== "function") return () => {};
      const wrapped = (name, data) => {
        if (name === eventName) handler(data);
      };
      eventListeners.add(wrapped);
      return () => eventListeners.delete(wrapped);
    },

    onGamepad(handler) {
      if (typeof handler === "function") gamepadListeners.add(handler);
      return () => gamepadListeners.delete(handler);
    },

    onThemeChange(handler) {
      if (typeof handler === "function") themeListeners.add(handler);
      return () => themeListeners.delete(handler);
    },

    whenReady: readyPromise
  };

  window.HaloDeckPlugin = api;
  window.parent.postMessage({ __halodeckPlugin: true, action: "sdkLoaded" }, "*");
})();
