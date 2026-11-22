/* ECharts Interop:延迟多线 / 可用率柱 / 上报双柱(从旧 Vue LatencyChart.vue + index.vue 平移)
 * 语义与旧版一致:
 *  - 容器隐藏/零宽高不初始化(返回 'HIDDEN',等可见再画),避免画布全白
 *  - 空数据画中央空态字,不走 xAxis:'time'(部分浏览器会整包抛错)
 *  - 维护段:柱满格灰 / 线压 0 灰 + markArea 灰底
 *  - 绘制异常返回错误文案(报错上屏),并 dispose 坏实例,下次重建不抱僵尸
 */
(function () {
  const MAINT_GRAY = '#9aa3c7';
  const SVC_COLORS = ['#22e6ff', '#ffd84d', '#ff9d3e', '#ff3ec8', '#9d7bff', '#2dffa8', '#7affd4', '#ff5470'];
  const AVAIL_OK = '#22c55e', AVAIL_WARN = '#f59e0b', AVAIL_BAD = '#ef4444';

  /** id -> { el, chart, dark } */
  const registry = new Map();

  function theme(dark) {
    return {
      axis: dark ? '#9aa3c7' : '#64748b',
      split: dark ? 'rgba(255,255,255,0.08)' : 'rgba(15,23,42,0.08)',
      tipBg: dark ? '#0b1130' : '#ffffff',
      tipText: dark ? '#eef1ff' : '#0f172a',
    };
  }

  function fmtAxisTime(ts) {
    return new Date(ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
  }

  function fmtTipTime(ts) {
    const d = new Date(ts);
    return `${d.getMonth() + 1}/${d.getDate()} ` + d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
  }

  // 脏数据只会画残,非法数据会整包抛错导致全白:先过滤
  function cleanPts(pts) {
    if (!Array.isArray(pts)) return [];
    return pts.filter(p =>
      Array.isArray(p) && typeof p[0] === 'number' && typeof p[1] === 'number' &&
      Number.isFinite(p[0]) && Number.isFinite(p[1])
    );
  }

  function availHex(p) {
    const v = Number(p);
    return v >= 99.5 ? AVAIL_OK : v >= 90 ? AVAIL_WARN : AVAIL_BAD;
  }

  function ensure(id, el, dark) {
    let entry = registry.get(id);
    if (entry && entry.el !== el) {
      // 元素被重建(DOM 替换):丢掉旧实例
      try { entry.chart && entry.chart.dispose(); } catch (e) { }
      entry = null;
    }
    if (!entry) {
      entry = { el: el, chart: null, dark: dark };
      registry.set(id, entry);
    }
    const rect = el.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return null; // 隐藏容器:不初始化
    if (!entry.chart || entry.chart.isDisposed()) {
      entry.chart = window.echarts.init(el);
    } else {
      entry.chart.resize();
    }
    entry.dark = dark;
    return entry;
  }

  function fail(id, err, tag) {
    console.error('[maimaiCharts:' + tag + ']', err);
    const entry = registry.get(id);
    if (entry && entry.chart) {
      try { entry.chart.dispose(); } catch (e) { }
      entry.chart = null;
    }
    return '绘制失败:' + String((err && err.message) || err).slice(0, 160);
  }

  /* ---------------- 延迟多线(监控页图表概览-延迟 / 历史页延迟曲线) ---------------- */
  function renderLatency(id, el, payload, dark) {
    const entry = ensure(id, el, dark);
    if (!entry) return 'HIDDEN';
    const t = theme(dark);
    try {
      const services = Array.isArray(payload.services) ? payload.services : [];
      const spans = (Array.isArray(payload.spans) ? payload.spans : []).filter(s =>
        Array.isArray(s) && typeof s[0] === 'number' && typeof s[1] === 'number' &&
        Number.isFinite(s[0]) && Number.isFinite(s[1])
      );
      const hasData = services.length > 0;

      // 空数据:画空态框(不留白,也不让 time 轴抛错)
      if (!hasData && spans.length === 0) {
        entry.chart.setOption({
          grid: { left: 46, right: 16, top: 42, bottom: 62 },
          xAxis: { type: 'time', axisLabel: { show: false }, axisLine: { show: false }, axisTick: { show: false } },
          yAxis: { show: false },
          series: [{ type: 'line', data: [] }],
          graphic: [{ type: 'text', left: 'center', top: 'middle', style: { text: '数据采集中…', fill: t.axis, fontSize: 13 } }]
        }, true);
        return '';
      }

      entry.chart.setOption({
        grid: { left: 46, right: 16, top: 42, bottom: 62 },
        legend: { top: 0, textStyle: { color: t.axis, fontSize: 12 } },
        tooltip: {
          trigger: 'axis',
          backgroundColor: t.tipBg,
          borderWidth: 0,
          textStyle: { color: t.tipText, fontSize: 12 },
          axisPointer: { lineStyle: { color: t.split } },
          valueFormatter: v => `${v}ms`
        },
        xAxis: {
          type: 'time',
          axisLabel: { color: t.axis, fontSize: 10, hideOverlap: true, formatter: v => fmtAxisTime(v) },
          axisLine: { lineStyle: { color: t.split } },
          axisTick: { show: false }
        },
        yAxis: {
          type: 'value',
          axisLabel: { color: t.axis, fontSize: 10, formatter: '{value}ms' },
          splitLine: { lineStyle: { color: t.split } }
        },
        dataZoom: [
          { type: 'inside' },
          { type: 'slider', height: 22, bottom: 6, borderColor: t.split, textStyle: { color: t.axis, fontSize: 10 } }
        ],
        graphic: hasData ? [] : [{ type: 'text', left: 'center', top: 'middle', style: { text: '数据采集中…', fill: t.axis, fontSize: 13 } }],
        series: [
          ...services.map((s, i) => ({
            name: s.name || s.key,
            type: 'line',
            smooth: true,
            symbol: 'none',
            color: SVC_COLORS[i % SVC_COLORS.length],
            lineStyle: { width: 2, color: SVC_COLORS[i % SVC_COLORS.length] },
            emphasis: { focus: 'series' },
            data: cleanPts(s.series)
          })),
          // 维护段:灰线压 0 + markArea 灰底(同名 legend 自动合并为一个"维护")
          ...spans.map(sp => ({
            name: '维护',
            type: 'line',
            symbol: 'none',
            color: MAINT_GRAY,
            lineStyle: { width: 2, color: MAINT_GRAY },
            data: [[sp[0], 0], [sp[1], 0]],
            markArea: {
              silent: true,
              itemStyle: { color: 'rgba(154,163,199,0.12)' },
              data: [[{ xAxis: sp[0] }, { xAxis: sp[1] }]]
            }
          }))
        ]
      }, true);
      return '';
    } catch (err) {
      return fail(id, err, 'latency');
    }
  }

  /* ---------------- 服务可用率柱(48 桶:maint 满格灰 / empty 留白) ---------------- */
  function renderAvailability(id, el, payload, dark) {
    const entry = ensure(id, el, dark);
    if (!entry) return 'HIDDEN';
    const t = theme(dark);
    try {
      const buckets = Array.isArray(payload.buckets) ? payload.buckets : [];
      const ratios = buckets.map(b => b.ratio * 100);
      const min = ratios.length ? Math.min.apply(null, ratios) : 100;

      entry.chart.setOption({
        grid: { left: 42, right: 12, top: 16, bottom: 24 },
        tooltip: {
          trigger: 'axis',
          backgroundColor: t.tipBg,
          borderWidth: 0,
          textStyle: { color: t.tipText, fontSize: 12 },
          formatter: function (ps) {
            const p = Array.isArray(ps) ? ps[0] : ps;
            const b = buckets[p.dataIndex];
            if (!b || b.empty || p.value[1] == null) return `${fmtTipTime(p.value[0])}<br/>暂无采集`;
            if (b.maint) return `${fmtTipTime(p.value[0])}<br/>常规维护中(04:00-07:00 GMT+8)`;
            return `${fmtTipTime(p.value[0])}<br/>可用率 <b>${Number(p.value[1]).toFixed(1)}%</b>`;
          }
        },
        xAxis: {
          type: 'time',
          axisLabel: { color: t.axis, fontSize: 10, hideOverlap: true, formatter: v => fmtAxisTime(v) },
          axisLine: { lineStyle: { color: t.split } },
          axisTick: { show: false }
        },
        yAxis: {
          type: 'value',
          min: Math.max(0, Math.floor(min - 1)),
          max: 100,
          axisLabel: { color: t.axis, fontSize: 10, formatter: '{value}%' },
          splitLine: { lineStyle: { color: t.split } }
        },
        series: [{
          type: 'bar',
          data: buckets.map(b => [b.ts, b.empty ? null : (b.maint ? 100 : +(b.ratio * 100).toFixed(1))]),
          barWidth: '60%',
          itemStyle: {
            borderRadius: [3, 3, 0, 0],
            color: function (p) {
              const b = buckets[p.dataIndex];
              if (!b || b.empty || p.value[1] == null) return 'transparent';
              return b.maint ? MAINT_GRAY : availHex(p.value[1]);
            }
          }
        }]
      }, true);
      return '';
    } catch (err) {
      return fail(id, err, 'avail');
    }
  }

  /* ---------------- 上报双柱(异常 / 正常,带 dataZoom) ---------------- */
  function renderReports(id, el, payload, dark) {
    const entry = ensure(id, el, dark);
    if (!entry) return 'HIDDEN';
    const t = theme(dark);
    try {
      const trend = Array.isArray(payload.trend) ? payload.trend : [];
      const hasData = trend.length > 0;

      entry.chart.setOption({
        grid: { left: 46, right: 16, top: 42, bottom: 62 },
        legend: { top: 0, textStyle: { color: t.axis, fontSize: 12 } },
        tooltip: {
          trigger: 'axis',
          backgroundColor: t.tipBg,
          borderWidth: 0,
          textStyle: { color: t.tipText, fontSize: 12 },
          axisPointer: { lineStyle: { color: t.split } },
          valueFormatter: v => `${v} 条`
        },
        xAxis: {
          type: 'time',
          axisLabel: { color: t.axis, fontSize: 10, hideOverlap: true, formatter: v => fmtAxisTime(v) },
          axisLine: { lineStyle: { color: t.split } },
          axisTick: { show: false }
        },
        yAxis: {
          type: 'value',
          axisLabel: { color: t.axis, fontSize: 10, formatter: '{value}' },
          splitLine: { lineStyle: { color: t.split } }
        },
        dataZoom: [
          { type: 'inside' },
          { type: 'slider', height: 22, bottom: 6, borderColor: t.split, textStyle: { color: t.axis, fontSize: 10 } }
        ],
        graphic: hasData ? [] : [{ type: 'text', left: 'center', top: 'middle', style: { text: '数据采集中…', fill: t.axis, fontSize: 13 } }],
        series: [
          {
            name: '异常上报',
            type: 'bar',
            barWidth: '38%',
            itemStyle: { borderRadius: [3, 3, 0, 0], color: '#ff5470' },
            data: trend.map(p => [p.ts, p.anomaly])
          },
          {
            name: '正常上报',
            type: 'bar',
            barWidth: '38%',
            itemStyle: { borderRadius: [3, 3, 0, 0], color: '#2dffa8' },
            data: trend.map(p => [p.ts, p.normal])
          }
        ]
      }, true);
      return '';
    } catch (err) {
      return fail(id, err, 'reports');
    }
  }

  /* ---------------- 统一入口(C# 只管数据,配置全在这里) ---------------- */
  window.maimaiCharts = {
    // 返回:'' 成功 / 'HIDDEN' 容器隐藏(不报错,等可见重画)/ 其余为错误文案
    render: function (kind, id, el, payloadJson, dark) {
      let payload = {};
      try {
        payload = typeof payloadJson === 'string' ? JSON.parse(payloadJson || '{}') : (payloadJson || {});
      } catch (err) {
        return '绘制失败:payload 解析失败';
      }
      if (!window.echarts) return '绘制失败:echarts 未加载';
      if (kind === 'latency') return renderLatency(id, el, payload, !!dark);
      if (kind === 'availability') return renderAvailability(id, el, payload, !!dark);
      if (kind === 'reports') return renderReports(id, el, payload, !!dark);
      return '绘制失败:未知图表类型 ' + kind;
    },
    resize: function (id) {
      const entry = registry.get(id);
      if (entry && entry.chart && !entry.chart.isDisposed()) entry.chart.resize();
    },
    dispose: function (id) {
      const entry = registry.get(id);
      if (entry) {
        try { entry.chart && entry.chart.dispose(); } catch (e) { }
        registry.delete(id);
      }
    }
  };

  // 窗口尺寸变化:全部重排(替代每个组件各自挂 resize 监听)
  let resizeTimer = null;
  window.addEventListener('resize', function () {
    if (resizeTimer) clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      registry.forEach(function (entry) {
        if (entry.chart && !entry.chart.isDisposed()) entry.chart.resize();
      });
    }, 120);
  });
})();
