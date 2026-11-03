// Számla készítése és a számla-műveletek (ingatlan-adatlap, Számla csomagok, bérlő/tulajdonos adatlap):
// „Számla készítése” ablak, generálás folyamatjelzővel, újrapróbálás és törlés.
// A szerver (POST /api/invoices/generate/stream) soronként jelzi a lépéseket (application/x-ndjson):
// {"type":"plan","steps":[…]}, {"type":"step","step":"pdf"}, …, {"type":"done","invoice":{…}} | {"type":"error",…}.
// A sáv a valós lépésekhez igazodik; egy lépésen belül lassan kúszik, hogy látszódjon, hogy dolgozik.
(function () {
  const STEP_LABELS = {
    data: 'Adatok összegyűjtése (bérleti díj, rezsi tételek)',
    pdf: 'Bizonylat (PDF) elkészítése',
    upload: 'Mentés a tárhelyre',
    email: 'E-mail küldése a bérlőnek',
    save: 'Rögzítés',
  };
  const DEFAULT_PLAN = ['data', 'pdf', 'email', 'save'];
  const ICON = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;

  const style = document.createElement('style');
  style.textContent = `
    .gp-bar{height:10px;border-radius:999px;background:var(--accent-bg);overflow:hidden;margin:6px 0 4px;}
    .gp-fill{height:100%;width:0;border-radius:999px;background:var(--accent);transition:width .45s ease, background-color .2s ease;
      background-image:linear-gradient(45deg,rgba(255,255,255,.18) 25%,transparent 25%,transparent 50%,rgba(255,255,255,.18) 50%,rgba(255,255,255,.18) 75%,transparent 75%,transparent);
      background-size:16px 16px;animation:gp-stripes .8s linear infinite;}
    .gp-fill.done{animation:none;background-image:none;background-color:var(--green-solid);}
    .gp-fill.failed{animation:none;background-image:none;background-color:var(--coral-solid);}
    @keyframes gp-stripes{from{background-position:16px 0}to{background-position:0 0}}
    .gp-pct{display:flex;justify-content:space-between;font-size:12.5px;font-weight:600;color:var(--ink-soft);margin-bottom:14px;}
    .gp-steps{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:9px;}
    .gp-steps li{display:flex;align-items:center;gap:10px;font-size:13.5px;color:var(--muted);font-weight:500;}
    .gp-steps .dot{width:22px;height:22px;border-radius:50%;border:1.5px solid var(--line-strong);display:flex;align-items:center;justify-content:center;flex-shrink:0;}
    .gp-steps .dot svg{width:12px;height:12px;}
    .gp-steps li.active{color:var(--ink);font-weight:600;}
    .gp-steps li.active .dot{border-color:var(--accent);border-top-color:transparent;animation:gp-spin .8s linear infinite;}
    .gp-steps li.ok{color:var(--ink-soft);}
    .gp-steps li.ok .dot{border-color:var(--green-solid);background:var(--green-solid);color:#fff;}
    @keyframes gp-spin{to{transform:rotate(360deg)}}
    .gp-result{margin-top:16px;padding:12px 14px;border-radius:10px;font-size:13.5px;font-weight:600;display:none;}
    .gp-result.show{display:block;}
    .gp-result.ok{background:var(--green-bg);color:var(--green);border:1px solid var(--green-border);}
    .gp-result.warn{background:var(--amber-bg);color:var(--amber);border:1px solid var(--amber-border);}
    .gp-result.error{background:var(--coral-bg);color:var(--coral);border:1px solid var(--coral-border);}
    .gp-result small{display:block;font-weight:500;margin-top:3px;}
    @media (prefers-reduced-motion: reduce){ .gp-fill,.gp-steps li.active .dot{animation:none;} .gp-fill{transition:none;} }
  `;
  document.head.appendChild(style);

  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.innerHTML = `
    <div class="modal" role="dialog" aria-modal="true" aria-labelledby="gp-title">
      <div class="modal-head"><div><h2 id="gp-title">Számla generálása</h2><p id="gp-sub"></p></div></div>
      <div class="modal-body">
        <div class="gp-bar" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-labelledby="gp-title"><div class="gp-fill" id="gp-fill"></div></div>
        <div class="gp-pct"><span id="gp-state">Indítás…</span><span id="gp-pct">0%</span></div>
        <ul class="gp-steps" id="gp-steps"></ul>
        <div class="gp-result" id="gp-result" role="status"></div>
      </div>
      <div class="modal-footer"><button class="btn btn-primary" type="button" id="gp-close" disabled>Generálás folyamatban…</button></div>
    </div>`;
  document.body.appendChild(overlay);
  const $ = (id) => overlay.querySelector(`#${id}`);

  let plan = DEFAULT_PLAN;
  let current = -1;
  let pct = 0;
  let creep = null;

  function setPct(value) {
    pct = Math.max(pct, Math.min(100, value));
    $('gp-fill').style.width = `${pct}%`;
    $('gp-pct').textContent = `${Math.round(pct)}%`;
    overlay.querySelector('.gp-bar').setAttribute('aria-valuenow', Math.round(pct));
  }
  function renderSteps() {
    $('gp-steps').innerHTML = plan.map((s, i) => {
      const cls = i < current ? 'ok' : i === current ? 'active' : '';
      return `<li class="${cls}"><span class="dot">${i < current ? ICON('check') : ''}</span>${STEP_LABELS[s] || s}</li>`;
    }).join('');
  }
  // Egy lépésen belül a sáv a lépés sávjának ~85%-áig kúszik, a következő lépésnél ugrik tovább.
  function enterStep(index) {
    current = index;
    renderSteps();
    const share = 100 / plan.length;
    setPct(share * index);
    $('gp-state').textContent = STEP_LABELS[plan[index]] || 'Folyamatban…';
    clearInterval(creep);
    const ceiling = share * (index + 0.85);
    creep = setInterval(() => setPct(pct + (ceiling - pct) * 0.08), 200);
  }
  function finish(kind, title, detail) {
    clearInterval(creep);
    current = kind === 'error' ? current : plan.length;
    renderSteps();
    const fill = $('gp-fill');
    fill.classList.toggle('done', kind !== 'error');
    fill.classList.toggle('failed', kind === 'error');
    if (kind !== 'error') setPct(100);
    $('gp-state').textContent = kind === 'error' ? 'Megszakadt' : 'Kész';
    const result = $('gp-result');
    result.className = `gp-result show ${kind}`;
    result.innerHTML = `${escapeHtml(title)}${detail ? `<small>${escapeHtml(detail)}</small>` : ''}`;
    const close = $('gp-close');
    close.disabled = false;
    close.textContent = 'Bezárás';
    close.focus();
  }

  // Visszaadja a kész számlát (InvoiceDto), vagy null-t, ha a generálás nem jutott el a mentésig.
  window.generateInvoiceWithProgress = function ({ propertyId, year, month, propertyName }) {
    plan = DEFAULT_PLAN;
    current = -1;
    pct = 0;
    $('gp-fill').className = 'gp-fill';
    $('gp-fill').style.width = '0';
    $('gp-pct').textContent = '0%';
    $('gp-state').textContent = 'Indítás…';
    $('gp-result').className = 'gp-result';
    $('gp-sub').textContent = `${propertyName ? `${propertyName} · ` : ''}${year}. ${MONTH_NAMES[month - 1].toLowerCase()}`;
    const close = $('gp-close');
    close.disabled = true;
    close.textContent = 'Generálás folyamatban…';
    renderSteps();
    overlay.classList.add('open');

    return new Promise((resolve) => {
      let invoice = null;
      close.onclick = () => { overlay.classList.remove('open'); resolve(invoice); };

      (async () => {
        try {
          const res = await apiFetch('/api/invoices/generate/stream', { method: 'POST', body: JSON.stringify({ propertyId, year, month }) });
          if (!res.ok || !res.body) {
            const data = await res.json().catch(() => ({}));
            finish('error', 'Nem sikerült elindítani a generálást.', errorMessage(res, data, ''));
            return;
          }
          const reader = res.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';
          let ended = false;
          const handle = (msg) => {
            if (msg.type === 'plan' && Array.isArray(msg.steps) && msg.steps.length) { plan = msg.steps; renderSteps(); }
            else if (msg.type === 'step') { const i = plan.indexOf(msg.step); if (i >= 0) enterStep(i); }
            else if (msg.type === 'done') {
              ended = true;
              invoice = msg.invoice;
              if (invoice.status === 'Failed') finish('error', 'A számla nem készült el.', invoice.errorMessage || '');
              else if (invoice.status === 'Sent') finish('ok', `Kész: ${invoice.number} — elküldve a bérlőnek.`, `Összeg: ${formatCurrency(invoice.amountTotal)}`);
              else finish('warn', `Kész: ${invoice.number} — legenerálva, de e-mailben nem ment ki.`, 'A bérlőnek nincs e-mail címe, vagy a küldés nem sikerült. A PDF a Számlázás oldalon letölthető.');
            } else if (msg.type === 'error') { ended = true; finish('error', msg.message || 'Hiba történt a generálás során.'); }
          };
          for (;;) {
            const { value, done } = await reader.read();
            if (value) buffer += decoder.decode(value, { stream: true });
            let nl;
            while ((nl = buffer.indexOf('\n')) >= 0) {
              const line = buffer.slice(0, nl).trim();
              buffer = buffer.slice(nl + 1);
              if (line) { try { handle(JSON.parse(line)); } catch { /* hiányos sor — kihagyjuk */ } }
            }
            if (done) break;
          }
          if (!ended) finish('error', 'A kapcsolat megszakadt a generálás közben.', 'Frissítsd az oldalt, és nézd meg a számla állapotát.');
        } catch (e) {
          finish('error', 'Hiba történt a generálás során.', e.message || '');
        }
      })();
    });
  };
})();

// ---------- Számla készítése ablak ----------
// propertyId megadásakor az ingatlan rögzített (adatlap), különben választható (Számla csomagok).
// Alapból a következő kiküldés időszaka (annak a rezsije kerül rá). onDone: a generálás után hívódik.
(function () {
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.innerHTML = `
    <div class="modal" role="dialog" aria-modal="true" aria-labelledby="ic-title">
      <div class="modal-head">
        <div><h2 id="ic-title">Számla készítése</h2><p id="ic-sub">Válaszd ki az időszakot. A számlára az időszak rezsi tételei kerülnek.</p></div>
        <button class="modal-close" type="button" data-ic-close aria-label="Bezárás">✕</button>
      </div>
      <div class="modal-body">
        <div class="form-error" id="ic-error"></div>
        <form id="ic-form">
          <div class="field" id="ic-property-field"><label for="ic-property">Ingatlan</label><select id="ic-property" required></select></div>
          <div class="field-row">
            <div class="field"><label for="ic-year">Év</label><input type="number" id="ic-year" min="2000" max="2100" required></div>
            <div class="field"><label for="ic-month">Hónap</label><select id="ic-month" required></select></div>
          </div>
          <p class="hint" id="ic-hint"></p>
        </form>
      </div>
      <div class="modal-footer">
        <button class="btn btn-secondary" type="button" data-ic-close>Mégse</button>
        <button class="btn btn-primary" type="submit" form="ic-form" id="ic-submit">Számla készítése</button>
      </div>
    </div>`;
  document.body.appendChild(overlay);
  const $ = (id) => overlay.querySelector(`#${id}`);
  $('ic-month').innerHTML = MONTH_NAMES.map((n, i) => `<option value="${i + 1}">${n}</option>`).join('');

  let properties = [];
  let onDone = null;
  const close = () => overlay.classList.remove('open');
  overlay.querySelectorAll('[data-ic-close]').forEach((b) => b.addEventListener('click', close));
  overlay.addEventListener('mousedown', (e) => { if (e.target === overlay) close(); });
  overlay.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !document.querySelector('.ui-menu')) close(); });

  function applyDefaults() {
    const p = properties.find((x) => x.id === $('ic-property').value);
    const now = new Date();
    $('ic-year').value = p?.nextPeriodYear ?? now.getFullYear();
    $('ic-month').value = String(p?.nextPeriodMonth ?? now.getMonth() + 1);
    $('ic-hint').textContent = p
      ? `A következő automatikus kiküldés: ${String(p.nextBillingAt).slice(0, 10).replace(/-/g, '. ')}. — a ${MONTH_NAMES[p.nextPeriodMonth - 1].toLowerCase()}i számla.${p.isActive ? '' : ' Az ingatlan inaktív.'}`
      : '';
  }
  $('ic-property').addEventListener('change', applyDefaults);

  window.openInvoiceCreateDialog = async function ({ propertyId = null, onDone: done = null } = {}) {
    onDone = done;
    $('ic-error').classList.remove('show');
    const res = await apiFetch('/api/properties');
    properties = res.ok ? await res.json() : [];
    const list = propertyId ? properties.filter((p) => p.id === propertyId) : properties.filter((p) => p.isActive);
    $('ic-property').innerHTML = list.map((p) => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
    $('ic-property-field').hidden = !!propertyId;
    $('ic-sub').textContent = propertyId && list[0]
      ? `${list[0].name} — válaszd ki az időszakot. A számlára az időszak rezsi tételei kerülnek.`
      : 'Válaszd ki az ingatlant és az időszakot. A számlára az időszak rezsi tételei kerülnek.';
    if (!list.length) {
      await uiAlert('Nincs aktív ingatlan, amihez számla készülhetne.', { title: 'Számla készítése' });
      return;
    }
    applyDefaults();
    overlay.classList.add('open');
    setTimeout(() => (propertyId ? $('ic-month') : $('ic-property')).focus(), 50);
  };

  $('ic-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const propertyId = $('ic-property').value;
    const year = Number($('ic-year').value);
    const month = Number($('ic-month').value);
    const property = properties.find((p) => p.id === propertyId);
    if (!propertyId || !(year >= 2000 && year <= 2100)) {
      $('ic-error').textContent = 'Válassz ingatlant és érvényes évet.';
      $('ic-error').classList.add('show');
      return;
    }
    // Ha az időszakra már van számla, a generálás azt frissíti (ugyanazzal a sorszámmal) — ezt előbb jóvá kell hagyni.
    const invRes = await apiFetch(`/api/properties/${propertyId}/invoices`);
    const existing = invRes.ok ? (await invRes.json()).find((i) => i.periodYear === year && i.periodMonth === month) : null;
    if (existing && existing.status !== 'Failed') {
      const sent = existing.status === 'Sent';
      const ok = await uiConfirm(
        `Erre az időszakra már van ${sent ? 'kiküldött' : 'legenerált'} számla: ${existing.number} (${formatCurrency(existing.amountTotal)}).\n\n`
        + `Újrakészítéskor ugyanezzel a sorszámmal, a mostani adatokkal készül el${sent ? ', és a bérlő e-mailben újra megkapja' : ''}.`,
        { title: 'Számla újrakészítése', confirmText: 'Újrakészítés', danger: sent });
      if (!ok) return;
    }
    close();
    await generateInvoiceWithProgress({ propertyId, year, month, propertyName: property?.name });
    document.dispatchEvent(new CustomEvent('invoices-changed'));
    if (onDone) onDone();
  });
})();

// ---------- Számla-műveletek a számlatáblákban (onclick-ből hívva) ----------
window.invoiceRetry = async (propertyId, year, month, propertyName) => {
  await generateInvoiceWithProgress({ propertyId, year, month, propertyName });
  document.dispatchEvent(new CustomEvent('invoices-changed'));
};

// Kiküldött számla törlése csak szándékos felülbírálással (pl. téves teszt-küldés) — a sorszámot be kell gépelni.
window.invoiceDelete = async (id, number, status) => {
  const sent = status === 'Sent';
  if (sent) {
    const typed = await uiPrompt(
      `Ez a számla már ki lett küldve a bérlőnek. Csak téves vagy teszt számlát törölj!\n\nA törléshez gépeld be a sorszámát: ${number}`,
      { title: 'Kiküldött számla törlése', confirmText: 'Végleges törlés', danger: true, input: { placeholder: number } });
    if (typed === null) return;
    if (typed.trim() !== number) {
      await uiAlert('A beírt sorszám nem egyezik, a számla nem lett törölve.', { title: 'Törlés megszakítva' });
      return;
    }
  } else if (!(await uiConfirm(`Biztosan törlöd a(z) ${number} számlát? Ez nem vonható vissza.`, { title: 'Számla törlése', confirmText: 'Törlés', danger: true }))) {
    return;
  }
  const res = await apiFetch(`/api/invoices/${id}${sent ? '?force=true' : ''}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 204) {
    await uiAlert(errorMessage(res, await readJson(res), 'Nem sikerült törölni a számlát.'), { title: 'Törlés sikertelen' });
    return;
  }
  document.dispatchEvent(new CustomEvent('invoices-changed'));
};

// A számlatáblák művelet-gombjai: PDF letöltése, újrapróbálás (sikertelennél), törlés.
window.invoiceActionButtons = function (i) {
  const icon = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const buttons = [];
  if (i.status !== 'Failed') {
    buttons.push(`<button class="icon-btn" type="button" title="PDF letöltése" aria-label="PDF letöltése" onclick="downloadFile('/api/invoices/${i.id}/pdf', ${jsArg(i.number + '.pdf')})">${icon('download')}</button>`);
  } else {
    buttons.push(`<button class="icon-btn" type="button" title="Újrapróbálás" aria-label="Újrapróbálás" onclick="invoiceRetry(${jsArg(i.propertyId)}, ${i.periodYear}, ${i.periodMonth}, ${jsArg(i.propertyName)})">${icon('rotate-ccw')}</button>`);
  }
  buttons.push(`<button class="icon-btn danger" type="button" title="Számla törlése" aria-label="Számla törlése" onclick="invoiceDelete(${jsArg(i.id)}, ${jsArg(i.number)}, ${jsArg(i.status)})">${icon('trash')}</button>`);
  return `<span class="invoice-actions">${buttons.join('')}</span>`;
};
