// Vezérlőpult — saját, függőség nélküli SVG-diagramok (a CSP csak saját szkriptet enged, build lépés nincs).
// Szabályok: egy y-tengely; vékony oszlopok (max. 24 px) lekerekített véggel, alul szögletesen; 2 px rés az
// egymás melletti / egymásra rakott elemek közt; minden értéket elér a buborék, a felirat vagy a táblázat is.
requireAuth();

(function () {
  const $ = (id) => document.getElementById(id);
  const NS = 'http://www.w3.org/2000/svg';
  const ICON = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const MONTH_SHORT = ['jan', 'feb', 'már', 'ápr', 'máj', 'jún', 'júl', 'aug', 'szep', 'okt', 'nov', 'dec'];
  const WEEKDAY = ['V', 'H', 'K', 'Sze', 'Cs', 'P', 'Szo'];
  const css = (name) => getComputedStyle($('dash')).getPropertyValue(name).trim();
  const CATEGORY_VARS = ['--series-1', '--series-2', '--series-3', '--series-4', '--series-5'];

  let data = null;

  // ---------- segédfüggvények ----------
  function svgEl(tag, attrs, parent) {
    const e = document.createElementNS(NS, tag);
    for (const [k, v] of Object.entries(attrs || {})) e.setAttribute(k, v);
    if (parent) parent.appendChild(e);
    return e;
  }
  // Kerek tengely-osztás: 1 / 2 / 2,5 / 5 × 10^n.
  function niceStep(raw) {
    if (raw <= 0) return 1;
    const exp = Math.pow(10, Math.floor(Math.log10(raw)));
    const f = raw / exp;
    return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * exp;
  }
  // Tengelyfelirat: 10 000 alatt teljes, fölötte „e” (ezer) / „M” (millió) — tengelyenként egy stílus.
  function compact(n, useCompact) {
    if (n === 0) return '0';
    if (!useCompact) return Math.round(n).toLocaleString('hu-HU');
    if (n >= 1e6) return `${(n / 1e6).toLocaleString('hu-HU', { maximumFractionDigits: 1 })} M`;
    return `${(n / 1000).toLocaleString('hu-HU', { maximumFractionDigits: 1 })} e`;
  }
  const huf = (n) => formatCurrency(Math.round(n));
  // „2026-ban”, „2025-ben” — a rag az évszám kiejtett végéhez igazodik.
  function inYear(y) {
    const last = y % 10, tens = Math.floor(y / 10) % 10;
    const back = last ? [3, 6, 8].includes(last) : (y % 100 === 0 ? (y % 1000 !== 0) : [2, 3, 6, 8].includes(tens));
    return `${y}-${back ? 'ban' : 'ben'}`;
  }
  // Felül lekerekített oszlop, az alapvonalon szögletes.
  function columnPath(x, y, w, h, r) {
    r = Math.max(0, Math.min(r, w / 2, h));
    return `M${x},${y + h}V${y + r}A${r},${r} 0 0 1 ${x + r},${y}H${x + w - r}A${r},${r} 0 0 1 ${x + w},${y + r}V${y + h}Z`;
  }

  // ---------- buborék ----------
  const tip = document.createElement('div');
  tip.className = 'viz-tip';
  tip.setAttribute('role', 'status');
  document.body.appendChild(tip);
  // A feliratok adatból jönnek, ezért textContenttel kerülnek be (nem innerHTML-lel).
  function showTip(content, x, y) {
    tip.replaceChildren();
    const title = document.createElement('div');
    title.className = 't-title';
    title.textContent = content.title;
    tip.appendChild(title);
    for (const row of content.rows) {
      const r = document.createElement('div');
      r.className = 't-row';
      const key = document.createElement('span');
      key.className = 't-key';
      key.style.background = row.color;
      const label = document.createElement('span');
      label.textContent = row.label;
      const val = document.createElement('span');
      val.className = 't-val';
      val.textContent = row.value;
      r.append(key, label, val);
      tip.appendChild(r);
    }
    if (content.foot) {
      const f = document.createElement('div');
      f.className = 't-foot';
      f.textContent = content.foot;
      tip.appendChild(f);
    }
    tip.classList.add('show');
    const w = tip.offsetWidth, h = tip.offsetHeight;
    let left = x + 14, top = y - h - 12;
    if (left + w > window.innerWidth - 8) left = x - w - 14;
    if (top < 8) top = y + 16;
    tip.style.left = `${Math.max(8, left)}px`;
    tip.style.top = `${top}px`;
  }
  const hideTip = () => tip.classList.remove('show');

  // ---------- oszlopdiagram (csoportos vagy egymásra rakott) ----------
  // series: [{ name, color, values[12] }], stacked: bool, tooltip(i) → { title, rows, foot }, capSeries: a kiemelt
  // sorozat indexe, amelynek a legnagyobb oszlopa értékfeliratot kap (csak az az egy — a többit a buborék adja).
  function renderColumns(container, { series, stacked, tooltip, capSeries, emptyText, ariaLabel }) {
    container.replaceChildren();
    const W = Math.max(280, container.clientWidth);
    const H = 250;
    const top = 22, bottom = 26;
    const plotH = H - top - bottom;
    const n = 12;
    const totals = Array.from({ length: n }, (_, i) => stacked
      ? series.reduce((s, ser) => s + ser.values[i], 0)
      : Math.max(...series.map((ser) => ser.values[i])));
    const maxVal = Math.max(0, ...totals);
    const step = niceStep((maxVal || 1) / 4);
    const yMax = Math.max(step, Math.ceil(maxVal / step) * step);
    const useCompact = yMax >= 10000;
    // Üres adatsornál csak az alapvonal látszik (nincs kitalált skála).
    const ticks = [];
    if (maxVal > 0) for (let v = 0; v <= yMax + step / 2; v += step) ticks.push(v);
    else ticks.push(0);
    const left = Math.max(...ticks.map((t) => compact(t, useCompact).length)) * 6.6 + 12;
    const plotW = W - left - 4;
    const band = plotW / n;
    const y = (v) => top + plotH - (v / yMax) * plotH;

    const svg = svgEl('svg', { viewBox: `0 0 ${W} ${H}`, height: H, role: 'img', 'aria-label': ariaLabel }, container);
    // rács + tengely
    for (const t of ticks) {
      svgEl('line', { x1: left, x2: W - 4, y1: y(t), y2: y(t), stroke: t === 0 ? css('--viz-axis') : css('--viz-grid'), 'stroke-width': 1, 'shape-rendering': 'crispEdges' }, svg);
      svgEl('text', { x: left - 8, y: y(t) + 4, 'text-anchor': 'end', class: 'tick' }, svg).textContent = compact(t, useCompact);
    }
    // Keskeny kijelzőn minden második hónap kap feliratot, hogy ne csússzanak egymásba.
    const every = band < 30 ? 2 : 1;
    for (let i = 0; i < n; i += every) {
      svgEl('text', { x: left + band * i + band / 2, y: H - 8, 'text-anchor': 'middle', class: 'tick' }, svg).textContent = MONTH_SHORT[i];
    }

    const washes = [];
    for (let i = 0; i < n; i++) {
      washes.push(svgEl('rect', { x: left + band * i + 1, y: top, width: band - 2, height: plotH, rx: 6, class: 'wash' }, svg));
    }

    // oszlopok
    let capPos = null;
    if (stacked) {
      const w = Math.min(24, band * 0.56);
      for (let i = 0; i < n; i++) {
        const x = left + band * i + (band - w) / 2;
        let base = y(0);
        const visible = series.map((s, si) => ({ si, v: s.values[i] })).filter((s) => s.v > 0);
        visible.forEach((s, k) => {
          const h = (s.v / yMax) * plotH;
          const isTop = k === visible.length - 1;
          const gap = k > 0 ? 2 : 0; // 2 px felületszínű rés az egymásra rakott szeletek közt
          const segH = Math.max(0, h - gap);
          const segY = base - h;
          if (segH > 0) {
            if (isTop) svgEl('path', { d: columnPath(x, segY, w, segH, 4), fill: series[s.si].color }, svg);
            else svgEl('rect', { x, y: segY, width: w, height: segH, fill: series[s.si].color }, svg);
          }
          base -= h;
        });
      }
    } else {
      const k = series.length;
      const w = Math.min(24, (band * 0.7 - (k - 1) * 2) / k);
      const groupW = k * w + (k - 1) * 2;
      for (let i = 0; i < n; i++) {
        series.forEach((s, si) => {
          const v = s.values[i];
          if (v <= 0) return;
          const x = left + band * i + (band - groupW) / 2 + si * (w + 2);
          svgEl('path', { d: columnPath(x, y(v), w, y(0) - y(v), 4), fill: s.color }, svg);
          if (si === capSeries && (!capPos || v > capPos.v)) capPos = { v, x: x + w / 2, y: y(v) };
        });
      }
    }
    if (capPos) {
      const label = huf(capPos.v);
      const half = label.length * 3.3;
      const cx = Math.min(W - 4 - half, Math.max(left + half, capPos.x));
      svgEl('text', { x: cx, y: capPos.y - 7, 'text-anchor': 'middle', class: 'cap' }, svg).textContent = label;
    }

    if (maxVal === 0 && emptyText) {
      const empty = document.createElement('div');
      empty.className = 'viz-empty';
      empty.textContent = emptyText;
      container.appendChild(empty);
    }

    // hónaponkénti találati sáv (nagyobb, mint az oszlop) — egérrel és billentyűzettel is
    for (let i = 0; i < n; i++) {
      const hit = svgEl('rect', { x: left + band * i, y: top, width: band, height: plotH + bottom, class: 'hit', tabindex: 0 }, svg);
      const content = tooltip(i);
      hit.setAttribute('aria-label', `${content.title}: ${content.rows.map((r) => `${r.label} ${r.value}`).join(', ')}`);
      const on = () => washes[i].classList.add('on');
      const off = () => { washes[i].classList.remove('on'); hideTip(); };
      hit.addEventListener('pointermove', (e) => { on(); showTip(content, e.clientX, e.clientY); });
      hit.addEventListener('pointerleave', off);
      hit.addEventListener('focus', () => {
        on();
        const r = hit.getBoundingClientRect();
        showTip(content, r.left + r.width / 2, r.top + 30);
      });
      hit.addEventListener('blur', off);
    }
  }

  function legend(el, items) {
    el.innerHTML = items.map((it) => `<span class="key"><span class="sw" style="background:${it.color}"></span>${escapeHtml(it.name)}</span>`).join('');
  }

  // ---------- szakaszok ----------
  function renderKpis() {
    const d = data;
    $('kpi-invoiced-label').textContent = `Kiszámlázva — ${d.year}`;
    $('kpi-invoiced').textContent = huf(d.invoicedTotal);
    $('kpi-invoiced-sub').textContent = `${d.invoiceCount} db számla${d.failed.count ? ` · ${d.failed.count} sikertelen nélkül` : ''}`;
    const range = d.compareUntilMonth === 12 ? 'teljes év' : `jan–${MONTH_SHORT[d.compareUntilMonth - 1]}`;
    if (d.previousYearToDate > 0) {
      const pct = ((d.invoicedTotal - d.previousYearToDate) / d.previousYearToDate) * 100;
      const up = pct >= 0;
      const pctText = `${up ? '+' : '−'}${Math.abs(pct).toLocaleString('hu-HU', { maximumFractionDigits: 1 })}%`;
      $('kpi-delta').innerHTML = `<span class="${up ? 'up' : 'down'}">${ICON(up ? 'arrow-up' : 'arrow-down')}</span>
        <span class="${up ? 'up' : 'down'}">${pctText}</span>
        <span class="vs">${d.year - 1} azonos időszakához képest (${range}: ${escapeHtml(huf(d.previousYearToDate))})</span>`;
    } else {
      $('kpi-delta').innerHTML = `<span class="vs">${inYear(d.year - 1)} ugyanebben az időszakban nem volt számla.</span>`;
    }
    $('kpi-utility-label').textContent = `Rezsi — ${d.year}`;
    $('kpi-utility').textContent = huf(d.utilityTotal);
    $('kpi-occupancy').textContent = `${d.rentedProperties} / ${d.activeProperties}`;
    const pct = d.activeProperties ? Math.round((d.rentedProperties / d.activeProperties) * 100) : 0;
    $('occupancy-meter').querySelector('span').style.width = `${pct}%`;
    $('occupancy-meter').setAttribute('aria-valuenow', pct);
    const vacant = d.activeProperties - d.rentedProperties;
    $('kpi-occupancy-sub').textContent = d.activeProperties
      ? `${pct}% kiadva · ${vacant ? `${vacant} üres` : 'nincs üres'}`
      : 'Még nincs aktív ingatlan.';
  }

  function renderMonthly() {
    const d = data;
    const cur = { name: String(d.year), color: css('--viz-accent'), values: d.monthly.map((m) => m.amount) };
    const prev = { name: String(d.year - 1), color: css('--viz-context'), values: d.monthly.map((m) => m.previousAmount) };
    $('monthly-title').textContent = `Kiszámlázott összeg havonta — ${d.year}`;
    legend($('monthly-legend'), [cur, prev]);
    renderColumns($('monthly-chart'), {
      series: [prev, cur],
      stacked: false,
      capSeries: 1,
      emptyText: 'Ebben az évben még nincs számla.',
      ariaLabel: `Kiszámlázott összeg havonta, ${d.year} és ${d.year - 1}`,
      tooltip: (i) => ({
        title: `${MONTH_NAMES[i]}`,
        rows: [
          { color: cur.color, label: cur.name, value: huf(cur.values[i]) },
          { color: prev.color, label: prev.name, value: huf(prev.values[i]) },
        ],
        foot: `${d.monthly[i].count} db számla ${inYear(d.year)}`,
      }),
    });
    $('monthly-table').innerHTML = `<table><thead><tr><th>Hónap</th><th class="num">${d.year}</th><th class="num">Számlák</th><th class="num">${d.year - 1}</th></tr></thead><tbody>
      ${d.monthly.map((m, i) => `<tr><td>${MONTH_NAMES[i]}</td><td class="num">${huf(m.amount)}</td><td class="num">${m.count} db</td><td class="num">${huf(m.previousAmount)}</td></tr>`).join('')}
      <tr><td class="cell-main">Összesen</td><td class="num cell-main">${huf(d.invoicedTotal)}</td><td class="num">${d.invoiceCount} db</td><td class="num">${huf(d.monthly.reduce((s, m) => s + m.previousAmount, 0))}</td></tr>
    </tbody></table>`;
  }

  function renderStatus() {
    const d = data;
    const parts = [
      { key: 'sent', name: 'Kiküldve', tone: 'good', color: css('--status-good'), icon: 'check', ...d.sent },
      { key: 'generated', name: 'Nem küldött', tone: 'warning', color: css('--status-warning'), icon: 'hourglass', ...d.generated, darkInk: true },
      { key: 'failed', name: 'Sikertelen', tone: 'critical', color: css('--status-critical'), icon: 'triangle-alert', ...d.failed },
    ];
    const total = parts.reduce((s, p) => s + p.count, 0);
    $('status-sub').textContent = total ? `${total} számla ${inYear(d.year)}, darabszám szerint.` : `${inYear(d.year)} még nincs számla.`;
    const bar = total
      ? `<div class="segbar" role="img" aria-label="${escapeHtml(parts.map((p) => `${p.name}: ${p.count} db (${Math.round((p.count / total) * 100)}%)`).join(', '))}">
          ${parts.filter((p) => p.count > 0).map((p) => {
            const share = Math.round((p.count / total) * 100);
            return `<span class="seg${p.darkInk ? ' dark-ink' : ''}" data-key="${p.key}" style="flex:${p.count} 1 0;background:${p.color}">${share}%</span>`;
          }).join('')}
        </div>`
      : '<div class="segbar empty"></div>';
    $('status-chart').innerHTML = `${bar}
      <div class="status-list">
        ${parts.map((p) => `<div class="status-item${p.count ? '' : ' zero'}">
          <span class="ico ${p.tone}">${ICON(p.icon)}</span>
          <div><div class="name"><span class="sw" style="background:${p.color}"></span>${p.name}</div><div class="amt">${huf(p.amount)}</div></div>
          <span class="num">${p.count} db</span>
        </div>`).join('')}
      </div>
      ${d.failed.count || d.generated.count ? '<a class="link status-link" href="billing.html">Megnyitás a Számlázásban →</a>' : ''}`;
    // A túl keskeny szelet nem kap feliratot (nem vágjuk le) — az értéke a listában és a buborékban van.
    $('status-chart').querySelectorAll('.seg').forEach((seg) => {
      if (seg.scrollWidth > seg.clientWidth) seg.textContent = '';
      const p = parts.find((x) => x.key === seg.dataset.key);
      const content = { title: p.name, rows: [{ color: p.color, label: `${Math.round((p.count / total) * 100)}%`, value: `${p.count} db` }], foot: huf(p.amount) };
      seg.addEventListener('pointermove', (e) => showTip(content, e.clientX, e.clientY));
      seg.addEventListener('pointerleave', hideTip);
    });
  }

  function renderProperties() {
    const list = data.byProperty;
    if (!list.length) {
      $('property-chart').innerHTML = `<div class="panel-empty">${inYear(data.year)} még nincs kiszámlázott összeg.</div>`;
      return;
    }
    const limit = 10;
    const max = list[0].amount || 1;
    $('property-chart').innerHTML = `<div class="hbars">${list.slice(0, limit).map((p) => `
      <div class="hbar-row">
        <div class="hbar-name"><a href="property-detail.html?id=${encodeURIComponent(p.propertyId)}">${escapeHtml(p.name)}</a><span class="cnt">${p.count} db</span></div>
        <div class="hbar-line"><span class="hbar" style="width:calc((100% - 120px) * ${(p.amount / max).toFixed(4)})"></span><span class="hbar-val">${huf(p.amount)}</span></div>
      </div>`).join('')}</div>
      ${list.length > limit ? `<div class="more-note">+ ${list.length - limit} további ingatlan, összesen ${huf(list.slice(limit).reduce((s, p) => s + p.amount, 0))}</div>` : ''}`;
  }

  function renderUpcoming() {
    const events = data.upcoming;
    if (!events.length) {
      $('upcoming').innerHTML = '<div class="panel-empty">A következő 30 napban nincs esemény.</div>';
      return;
    }
    const KIND = {
      billing: { icon: 'receipt', label: 'Számla kiküldése' },
      moveIn: { icon: 'arrow-right', label: 'Beköltözés' },
      moveOut: { icon: 'arrow-left', label: 'Kiköltözés' },
    };
    const limit = 8;
    $('upcoming').innerHTML = `<div class="events">${events.slice(0, limit).map((e) => {
      const date = new Date(`${e.date}T00:00:00`);
      const k = KIND[e.kind] || KIND.billing;
      const prop = `<a href="property-detail.html?id=${encodeURIComponent(e.propertyId)}">${escapeHtml(e.propertyName)}</a>`;
      const what = e.tenantName
        ? `<a href="tenant-detail.html?id=${encodeURIComponent(e.tenantId)}">${escapeHtml(e.tenantName)}</a> · ${prop}`
        : prop;
      return `<div class="event">
        <div class="date"><b>${date.getDate()}</b><small>${MONTH_SHORT[date.getMonth()]} · ${WEEKDAY[date.getDay()]}</small></div>
        <div class="what"><div class="k">${ICON(k.icon)}${k.label}</div><div class="d">${what}</div></div>
      </div>`;
    }).join('')}</div>
    ${events.length > limit ? `<div class="more-note">+ ${events.length - limit} további esemény</div>` : ''}`;
  }

  function renderUtility() {
    const d = data;
    const series = d.utilityCategories.map((name, i) => ({
      name,
      color: name === 'Egyéb' && i === d.utilityCategories.length - 1 ? css('--series-other') : css(CATEGORY_VARS[i] || '--series-other'),
      values: d.utilityMonthly.map((m) => m.values[i]),
    }));
    legend($('utility-legend'), series);
    const totals = d.utilityMonthly.map((m) => m.values.reduce((s, v) => s + v, 0));
    renderColumns($('utility-chart'), {
      series,
      stacked: true,
      emptyText: `${inYear(d.year)} még nincs felvett rezsi.`,
      ariaLabel: `Rezsi havonta kategóriánként, ${d.year}`,
      tooltip: (i) => ({
        title: MONTH_NAMES[i],
        rows: series.slice().reverse().map((s) => ({ color: s.color, label: s.name, value: huf(s.values[i]) })),
        foot: `Összesen: ${huf(totals[i])}`,
      }),
    });
    $('utility-table').innerHTML = series.length
      ? `<table><thead><tr><th>Hónap</th>${series.map((s) => `<th class="num">${escapeHtml(s.name)}</th>`).join('')}<th class="num">Összesen</th></tr></thead><tbody>
        ${MONTH_NAMES.map((m, i) => `<tr><td>${m}</td>${series.map((s) => `<td class="num">${huf(s.values[i])}</td>`).join('')}<td class="num cell-main">${huf(totals[i])}</td></tr>`).join('')}
      </tbody></table>`
      : '<div class="panel-empty">Nincs adat.</div>';
  }

  function renderAll() {
    if (!data) return;
    renderKpis();
    renderMonthly();
    renderStatus();
    renderProperties();
    renderUpcoming();
    renderUtility();
  }

  async function load(year) {
    $('dash').classList.add('loading'); // újratöltés közben a régi kép marad, halványítva
    try {
      const res = await apiFetch(`/api/dashboard${year ? `?year=${year}` : ''}`);
      const body = await readJson(res);
      if (!res.ok) throw new Error(errorMessage(res, body, 'Nem sikerült betölteni a vezérlőpultot.'));
      data = body;
      const select = $('year-select');
      select.innerHTML = data.years.map((y) => `<option value="${y}"${y === data.year ? ' selected' : ''}>${y}</option>`).join('');
      $('page-error').classList.remove('show');
      renderAll();
    } catch (e) {
      $('page-error').textContent = e.message;
      $('page-error').classList.add('show');
    } finally {
      $('dash').classList.remove('loading');
    }
  }

  const name = (getAuth()?.user?.fullName || '').trim().split(/\s+/).pop();
  if (name) $('greeting').textContent = `Szia, ${name}!`;

  $('year-select').addEventListener('change', (e) => load(Number(e.target.value)));
  let resizeTimer;
  let lastWidth = $('dash').clientWidth;
  window.addEventListener('resize', () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      if ($('dash').clientWidth === lastWidth) return;
      lastWidth = $('dash').clientWidth;
      hideTip();
      renderMonthly();
      renderUtility();
    }, 120);
  });
  load();
})();
