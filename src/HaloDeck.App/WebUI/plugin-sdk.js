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
      gamepadListeners.forEach((fn) => {
        try { fn(msg.action); } catch {}
      });
    }
  });

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
      reset() {
        return callHost("themeReset", {});
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
