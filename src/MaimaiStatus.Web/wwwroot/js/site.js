/* 站点脚本:三态主题(system / light / dark,localStorage 持久化)+ SSE 客户端
 * 与旧 Nuxt useColorMode 行为一致:system 跟随 prefers-color-scheme 并实时响应切换
 */
(function () {
  const MEDIA = '(prefers-color-scheme: dark)';

  function prefOf(raw) {
    return raw === 'light' || raw === 'dark' ? raw : 'system';
  }

  function resolveDark(pref) {
    if (pref === 'dark') return true;
    if (pref === 'light') return false;
    return window.matchMedia && window.matchMedia(MEDIA).matches;
  }

  function applyHtml(pref, dark) {
    const el = document.documentElement;
    el.classList.toggle('dark', dark);
    el.setAttribute('data-theme', dark ? 'dark' : 'light');
    // Fluent 5.0.1 契约:组件设计 token 跟随 body[data-theme](dark/light/system)
    document.body.setAttribute('data-theme', pref);
  }

  window.maimaiTheme = {
    /** 读存量偏好 + 应用 + 挂 system 监听,返回 { pref, dark } */
    bootstrap: function (storageKey, dotNet) {
      let pref = 'system';
      try { pref = prefOf(localStorage.getItem(storageKey) || 'system'); } catch (e) { }
      const dark = resolveDark(pref);
      applyHtml(pref, dark);
      try {
        if (window.matchMedia) {
          const mq = window.matchMedia(MEDIA);
          const handler = function (e) {
            if (pref !== 'system') return; // 手动挡时 system 变化不生效
            applyHtml(pref, e.matches);
            if (dotNet) dotNet.invokeMethodAsync('OnSystemThemeChanged', e.matches);
          };
          if (mq.addEventListener) mq.addEventListener('change', handler);
          else if (mq.addListener) mq.addListener(handler);
        }
      } catch (e) { }
      return { pref: pref, dark: dark };
    },
    /** 切到指定挡位,返回最终 isDark */
    apply: function (pref) {
      pref = prefOf(pref);
      const dark = resolveDark(pref);
      applyHtml(pref, dark);
      return dark;
    },
    save: function (storageKey, pref) {
      try { localStorage.setItem(storageKey, pref); } catch (e) { }
    }
  };

  /* ---------------- SSE:/api/stream(EventSource) ---------------- */
  let es = null;
  let errCount = 0;
  let dotNetRef = null;

  function teardown() {
    if (es) {
      try { es.close(); } catch (e) { }
      es = null;
    }
  }

  window.maimaiSse = {
    /** 连接成功返回 true;失败返回 false(页面回退 30s 轮询) */
    connect: function (url, dotNet) {
      teardown();
      dotNetRef = dotNet;
      if (!window.EventSource) return false;
      errCount = 0;
      try {
        es = new EventSource(url);
      } catch (e) {
        return false;
      }
      es.addEventListener('status', function (ev) {
        errCount = 0;
        if (dotNetRef) {
          // 忽略 Promise 拒绝(电路已断时的迟到回调)
          dotNetRef.invokeMethodAsync('OnSseStatus', ev.data).catch(function () { });
        }
      });
      es.onerror = function () {
        errCount += 1;
        // EventSource 自带重连;连续 3 次仍失败 → 通知 .NET 切轮询兜底
        if (errCount >= 3) {
          teardown();
          if (dotNetRef) dotNetRef.invokeMethodAsync('OnSseDown').catch(function () { });
        }
      };
      return true;
    },
    close: function () {
      teardown();
    }
  };
})();
