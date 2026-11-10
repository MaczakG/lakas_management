// Vezérlőpult — a diagramok a közös charts.js-ből (VizCharts) jönnek.
requireAuth();

(function () {
  const $ = (id) => document.getElementById(id);
  const ICON = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const css = (name) => getComputedStyle($('dash')).getPropertyValue(name).trim();

  let data = null;

  const { MONTH_SHORT, huf, inYear, showTip, hideTip, renderColumns, legend, categoryColor } = VizCharts;

  // ---------- szakaszok ----------
  function renderKpis() {
    const d = data;
    $('kpi-invoiced-label').textContent = `Kiállítva ${inYear(d.year)}`;
    $('kpi-invoiced').textContent = huf(d.invoicedTotal);
    $('kpi-invoiced-sub').textContent = `${d.invoiceCount} db bizonylat${d.failed.count ? `, ${d.failed.count} sikertelen nélkül` : ''}`;
    const range = d.compareUntilMonth === 12 ? 'teljes év' : `jan–${MONTH_SHORT[d.compareUntilMonth - 1]}`;
    if (d.previousYearToDate > 0) {
      const pct = ((d.invoicedTotal - d.previousYearToDate) / d.previousYearToDate) * 100;
      const up = pct >= 0;
      const pctText = `${up ? '+' : '−'}${Math.abs(pct).toLocaleString('hu-HU', { maximumFractionDigits: 1 })}%`;
      $('kpi-delta').innerHTML = `<span class="${up ? 'up' : 'down'}">${ICON(up ? 'arrow-up' : 'arrow-down')}</span>
        <span class="${up ? 'up' : 'down'}">${pctText}</span>
        <span class="vs">${d.year - 1} azonos időszakához képest (${range}: ${escapeHtml(huf(d.previousYearToDate))})</span>`;
    } else {
      $('kpi-delta').innerHTML = `<span class="vs">${inYear(d.year - 1)} ugyanebben az időszakban nem volt bizonylat.</span>`;
    }
    $('kpi-utility-label').textContent = `Rezsi ${inYear(d.year)}`;
    $('kpi-utility').textContent = huf(d.utilityTotal);
    // Egy kiválasztott ingatlannál a kihasználtság helyett az ingatlan állapota és a mai bérlő látszik.
    $('occupancy-meter').hidden = !!d.propertyId;
    if (d.propertyId) {
      const selected = d.properties.find((p) => p.id === d.propertyId);
      $('kpi-occupancy-label').textContent = 'Ingatlan állapota';
      $('kpi-occupancy').textContent = !selected?.isActive ? 'Inaktív' : d.rentedProperties ? 'Kiadva' : 'Üres';
      $('kpi-occupancy-sub').textContent = d.currentTenantName ? `Bérlő: ${d.currentTenantName}` : 'Nincs jelenlegi bérlő.';
      return;
    }
    $('kpi-occupancy-label').textContent = 'Kiadott ingatlanok';
    $('kpi-occupancy').textContent = `${d.rentedProperties} / ${d.activeProperties}`;
    const pct = d.activeProperties ? Math.round((d.rentedProperties / d.activeProperties) * 100) : 0;
    $('occupancy-meter').querySelector('span').style.width = `${pct}%`;
    $('occupancy-meter').setAttribute('aria-valuenow', pct);
    const vacant = d.activeProperties - d.rentedProperties;
    $('kpi-occupancy-sub').textContent = d.activeProperties
      ? `${pct}% kiadva, ${vacant ? `${vacant} üres` : 'nincs üres'}`
      : 'Még nincs aktív ingatlan.';
  }

  function renderMonthly() {
    const d = data;
    const cur = { name: String(d.year), color: css('--viz-accent'), values: d.monthly.map((m) => m.amount) };
    const prev = { name: String(d.year - 1), color: css('--viz-context'), values: d.monthly.map((m) => m.previousAmount) };
    $('monthly-title').textContent = `Kiállított összeg havonta (${d.year})`;
    legend($('monthly-legend'), [cur, prev]);
    renderColumns($('monthly-chart'), {
      series: [prev, cur],
      stacked: false,
      capSeries: 1,
      emptyText: 'Ebben az évben még nincs bizonylat.',
      ariaLabel: `Kiállított összeg havonta, ${d.year} és ${d.year - 1}`,
      tooltip: (i) => ({
        title: `${MONTH_NAMES[i]}`,
        rows: [
          { color: cur.color, label: cur.name, value: huf(cur.values[i]) },
          { color: prev.color, label: prev.name, value: huf(prev.values[i]) },
        ],
        foot: `${d.monthly[i].count} db bizonylat ${inYear(d.year)}`,
      }),
    });
    $('monthly-table').innerHTML = `<table><thead><tr><th>Hónap</th><th class="num">${d.year}</th><th class="num">Bizonylatok</th><th class="num">${d.year - 1}</th></tr></thead><tbody>
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
    $('status-sub').textContent = total ? `${total} bizonylat` : `${inYear(d.year)} még nincs bizonylat.`;
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
      ${d.failed.count || d.generated.count ? '<a class="link status-link" href="invoice-bundles.html">Megnyitás a Bizonylatok között<svg class="inline-ico" viewBox="0 0 24 24" aria-hidden="true"><use href="assets/icons/icons.svg#arrow-right"/></svg></a>' : ''}`;
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
    // Egy ingatlannál az ingatlanonkénti bontásnak nincs értelme: a kártya eltűnik, a közelgő események teljes szélességet kapnak.
    $('property-card').hidden = !!data.propertyId;
    $('upcoming-card').classList.toggle('span-3', !!data.propertyId);
    if (data.propertyId) return;
    const list = data.byProperty;
    if (!list.length) {
      $('property-chart').innerHTML = `<div class="panel-empty">${inYear(data.year)} még nincs kiállított bizonylat.</div>`;
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
      billing: { icon: 'receipt', label: 'Bizonylat kiküldése' },
      moveIn: { icon: 'arrow-right', label: 'Beköltözés' },
      moveOut: { icon: 'arrow-left', label: 'Kiköltözés' },
    };
    const limit = 8;
    $('upcoming').innerHTML = `<div class="events">${events.slice(0, limit).map((e) => {
      const date = new Date(`${e.date}T00:00:00`);
      const k = KIND[e.kind] || KIND.billing;
      const prop = `<a href="property-detail.html?id=${encodeURIComponent(e.propertyId)}">${escapeHtml(e.propertyName)}</a>`;
      const what = e.tenantName
        ? `<a href="tenant-detail.html?id=${encodeURIComponent(e.tenantId)}">${escapeHtml(e.tenantName)}</a>, ${prop}`
        : prop;
      return `<div class="event">
        <div class="date"><b>${date.getDate()}</b><small>${MONTH_SHORT[date.getMonth()]}.</small></div>
        <div class="what"><div class="k">${ICON(k.icon)}${k.label}</div><div class="d">${what}</div></div>
      </div>`;
    }).join('')}</div>
    ${events.length > limit ? `<div class="more-note">+ ${events.length - limit} további esemény</div>` : ''}`;
  }

  function renderUtility() {
    const d = data;
    const series = d.utilityCategories.map((name, i) => ({
      name,
      color: categoryColor($('dash'), i, name === 'Egyéb' && i === d.utilityCategories.length - 1),
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

  let propertyId = ''; // '' = összes ingatlan

  async function load(year) {
    $('dash').classList.add('loading'); // újratöltés közben a régi kép marad, halványítva
    try {
      const query = new URLSearchParams();
      if (year) query.set('year', year);
      if (propertyId) query.set('propertyId', propertyId);
      const res = await apiFetch(`/api/dashboard${query.size ? `?${query}` : ''}`);
      const body = await readJson(res);
      if (!res.ok) throw new Error(errorMessage(res, body, 'Nem sikerült betölteni a vezérlőpultot.'));
      data = body;
      const select = $('year-select');
      select.innerHTML = data.years.map((y) => `<option value="${y}"${y === data.year ? ' selected' : ''}>${y}</option>`).join('');
      $('property-select').innerHTML = '<option value="">Összes ingatlan</option>' + data.properties.map((p) =>
        `<option value="${escapeHtml(p.id)}"${p.id === data.propertyId ? ' selected' : ''}>${escapeHtml(p.name)}${p.isActive ? '' : ' (inaktív)'}</option>`).join('');
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
  $('property-select').addEventListener('change', (e) => { propertyId = e.target.value; load(data?.year); });
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
