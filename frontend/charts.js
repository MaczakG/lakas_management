// Közös, függőség nélküli SVG-diagramok (Vezérlőpult, Rezsi felvétel) — a CSP csak saját szkriptet enged,
// build lépés nincs. Szabályok: egy y-tengely; vékony oszlopok (max. 24 px) lekerekített véggel, alul
// szögletesen; 2 px rés az egymás melletti / egymásra rakott elemek közt; minden érték elérhető a buborékban,
// a feliratban vagy a táblázatban is. A színek a legközelebbi .viz-root CSS-változóiból jönnek (charts.css).
window.VizCharts = (function () {
  const NS = 'http://www.w3.org/2000/svg';
  const MONTH_SHORT = ['jan', 'feb', 'már', 'ápr', 'máj', 'jún', 'júl', 'aug', 'szep', 'okt', 'nov', 'dec'];
  // A kategória-sorrend színtévesztésre ellenőrzött (ne keverd); a „többi” mindig szürke.
  const CATEGORY_VARS = ['--series-1', '--series-2', '--series-3', '--series-4', '--series-5'];
  const cssVar = (el, name) => getComputedStyle(el.closest('.viz-root') || document.documentElement).getPropertyValue(name).trim();
  const categoryColor = (el, i, isOther) => cssVar(el, isOther ? '--series-other' : (CATEGORY_VARS[i] || '--series-other'));

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
  // onSelect(i): kattintásra / Enterre (pl. a hónap megnyitása); capTotal: egymásra rakottnál a legnagyobb
  // havi összeg kap feliratot. labels: az X tengely feliratai (alapból a 12 hónap); a hosszú felirat a sávjához vágódik.
  function renderColumns(container, { series, stacked, tooltip, capSeries, capTotal, emptyText, ariaLabel, onSelect, labels }) {
    const css = (name) => cssVar(container, name);
    container.replaceChildren();
    const W = Math.max(280, container.clientWidth);
    const H = 250;
    const top = 22, bottom = 26;
    const plotH = H - top - bottom;
    const n = labels ? labels.length : 12;
    if (n === 0) {
      const empty = document.createElement('div');
      empty.className = 'viz-empty';
      empty.textContent = emptyText || '';
      container.appendChild(empty);
      return;
    }
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
    const every = !labels && band < 30 ? 2 : 1;
    const maxChars = Math.max(3, Math.floor((band - 8) / 6.4));
    for (let i = 0; i < n; i += every) {
      const full = labels ? labels[i] : MONTH_SHORT[i];
      const tick = svgEl('text', { x: left + band * i + band / 2, y: H - 8, 'text-anchor': 'middle', class: 'tick' }, svg);
      tick.textContent = full.length > maxChars ? `${full.slice(0, maxChars - 1).trimEnd()}…` : full;
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
        if (capTotal && totals[i] > 0 && (!capPos || totals[i] > capPos.v)) capPos = { v: totals[i], x: x + w / 2, y: y(totals[i]) };
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
      const hit = svgEl('rect', { x: left + band * i, y: top, width: band, height: plotH + bottom, class: `hit${onSelect ? ' selectable' : ''}`, tabindex: 0 }, svg);
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
      if (onSelect) {
        hit.setAttribute('role', 'button');
        hit.addEventListener('click', () => { hideTip(); onSelect(i); });
        hit.addEventListener('keydown', (e) => {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); hideTip(); onSelect(i); }
        });
      }
    }
  }

  function legend(el, items) {
    el.innerHTML = items.map((it) => `<span class="key"><span class="sw" style="background:${it.color}"></span>${escapeHtml(it.name)}</span>`).join('');
  }

  return { MONTH_SHORT, cssVar, categoryColor, compact, huf, inYear, showTip, hideTip, renderColumns, legend };
})();
