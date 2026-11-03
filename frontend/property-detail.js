// Ingatlan adatlap (property-detail.html?id=...). A panelek értékei helyben szerkeszthetők: egy mező
// elhagyásakor (vagy Enterre) a teljes ingatlan PUT-tal mentődik — a szerver a frissített adatlapot adja
// vissza, abból frissül a fejléc és a számolt értékek (pl. a munkanapra tolt következő számlázás).
// A mezőket mentés után nem rajzoljuk újra, hogy a fókusz ne ugorjon el.
requireAuth();

const propertyId = new URLSearchParams(location.search).get('id');
if (!propertyId) location.replace('properties.html');

const PROPERTY_TYPES = ['Lakás', 'Családi ház', 'Iroda', 'Üzlethelyiség', 'Garázs', 'Egyéb'];
const INVOICE_STATUS = { Sent: 'Kiküldve', Generated: 'Nem küldött', Failed: 'Sikertelen' };
const ICON = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;

let property = null;
let invoices = [];
let utilityEntries = [];
let allOwners = [];

// ---------- helyben szerkeszthető mezők ----------

// Egy mező leírása: hol van az értéke (path), milyen vezérlő, és hogyan alakul át mentéshez.
// path: 'name' | 'sheet.city' | 'billingTime' (különleges: óra+perc egy mezőben)
const FIELDS = {
  basic: [
    { path: 'name', label: 'Megnevezés', required: true, placeholder: 'pl. Tata, Kocsi u. 17. A22' },
    { path: 'sheet.propertyType', label: 'Típus', type: 'select', options: PROPERTY_TYPES },
    { path: 'sheet.postalCode', label: 'Irányítószám', placeholder: 'pl. 2890' },
    { path: 'sheet.city', label: 'Település', placeholder: 'pl. Tata' },
    { path: 'sheet.street', label: 'Utca, házszám', placeholder: 'pl. Kocsi utca 17–19.' },
    { path: 'sheet.floorDoor', label: 'Emelet, ajtó', placeholder: 'pl. 4. em. 22.' },
    { path: 'sheet.hrsz', label: 'Helyrajzi szám', placeholder: 'pl. 1234/5/A/22' },
  ],
  billing: [
    { path: 'rentAmount', label: 'Bérleti díj', type: 'number', group: true, required: true, suffix: () => rentCurrencyLabel() },
    { path: 'sheet.deposit', label: 'Kaució', type: 'number', group: true, suffix: () => 'Ft' },
    { path: 'invoicePrefix', label: 'Számla előtag', placeholder: 'pl. A22', upper: true },
    { path: 'billingDayOfMonth', label: 'Számlázási nap', type: 'number', integer: true, min: 1, max: 28, required: true, placeholder: '1–28' },
    { path: 'billingTime', label: 'Kiküldés ideje', type: 'time', required: true, placeholder: 'óó:pp' },
  ],
  tech: [
    { path: 'sheet.floorArea', label: 'Alapterület', type: 'number', suffix: () => 'm²' },
    { path: 'sheet.rooms', label: 'Szobák', type: 'number', step: '0.5', placeholder: 'pl. 2,5' },
    { path: 'sheet.buildYear', label: 'Építés éve', type: 'number', integer: true },
    { path: 'sheet.electricityMeter', label: 'Villanyóra száma' },
    { path: 'sheet.gasMeter', label: 'Gázóra száma' },
    { path: 'sheet.waterMeter', label: 'Vízóra száma' },
  ],
  notes: [
    { path: 'sheet.notes', label: 'Megjegyzés', type: 'textarea', wide: true, placeholder: 'Bármi, amit az ingatlanról tudni kell (kulcsok, szerződés, felújítás…)' },
  ],
};

function getValue(path) {
  if (path === 'billingTime') {
    return `${String(property.billingHour).padStart(2, '0')}:${String(property.billingMinute).padStart(2, '0')}`;
  }
  return path.split('.').reduce((obj, key) => obj?.[key], property);
}

function setValue(target, path, value) {
  if (path === 'billingTime') {
    const [h, m] = String(value).split(':').map(Number);
    target.billingHour = h;
    target.billingMinute = m;
    return;
  }
  const keys = path.split('.');
  const last = keys.pop();
  keys.reduce((obj, key) => obj[key], target)[last] = value;
}

function renderField(field) {
  const value = getValue(field.path);
  const id = `f-${field.path.replace(/\./g, '-')}`;
  let control;
  if (field.type === 'select') {
    control = `<select class="iinput" id="${id}"><option value="">Nincs megadva</option>${field.options
      .map((o) => `<option value="${escapeHtml(o)}"${o === value ? ' selected' : ''}>${escapeHtml(o)}</option>`).join('')}</select>`;
  } else if (field.type === 'textarea') {
    control = `<textarea class="iinput" id="${id}" placeholder="${escapeHtml(field.placeholder || '')}">${escapeHtml(value ?? '')}</textarea>`;
  } else {
    // Számnál és időpontnál is sima szövegmező: a böngésző saját number/time vezérlője (léptető nyilak,
    // AM/PM a nem magyar nyelvű böngészőben) nem illik a helyben szerkesztett értékek közé.
    const mode = field.type === 'number' ? ' inputmode="decimal"' : (field.type === 'time' ? ' inputmode="numeric"' : '');
    const shown = field.type === 'number' && value != null
      ? (field.group ? formatNumber(value, value % 1 ? 2 : 0) : String(value).replace('.', ','))
      : (value ?? '');
    const type = 'text';
    control = `<input class="iinput" id="${id}" type="${type}"${mode} value="${escapeHtml(shown)}" placeholder="${escapeHtml(field.placeholder || (field.required ? '' : '–'))}" autocomplete="off">`;
  }
  const suffix = field.suffix ? `<span class="suffix" data-suffix="${field.path}">${escapeHtml(field.suffix())}</span>` : '';
  return `<div class="prop${field.wide ? ' wide' : ''}">
    <dt><label for="${id}">${escapeHtml(field.label)}</label></dt>
    <dd><div class="ifield"><div class="ictl">${control}</div>${suffix}<span class="istate" data-state="${field.path}"></span></div></dd>
  </div>`;
}

// A beírt szöveg → mentendő érték. Hibánál { error } jön vissza.
function parseInput(field, raw) {
  const text = raw.trim();
  if (!text) {
    if (field.required) return { error: 'Kötelező mező.' };
    return { value: null };
  }
  if (field.type === 'number') {
    const n = Number(text.replace(/[\s\u00A0]/g, '').replace(',', '.'));
    if (!Number.isFinite(n)) return { error: 'Számot adj meg.' };
    if (field.integer && !Number.isInteger(n)) return { error: 'Egész számot adj meg.' };
    if (field.min != null && n < field.min) return { error: `Legalább ${field.min}.` };
    if (field.max != null && n > field.max) return { error: `Legfeljebb ${field.max}.` };
    return { value: n };
  }
  if (field.type === 'time') {
    const m = /^(\d{1,2})[:.](\d{2})$/.exec(text);
    if (!m || Number(m[1]) > 23 || Number(m[2]) > 59) return { error: 'Óra:perc alakban, pl. 08:00.' };
    return { value: `${m[1].padStart(2, '0')}:${m[2]}` };
  }
  return { value: field.upper ? text.toUpperCase() : text };
}

function bindFields(fields) {
  fields.forEach((field) => {
    const input = document.getElementById(`f-${field.path.replace(/\./g, '-')}`);
    if (!input) return;
    let lastSaved = input.value;
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && field.type !== 'textarea') { e.preventDefault(); input.blur(); }
      if (e.key === 'Escape') { input.value = lastSaved; input.blur(); }
    });
    input.addEventListener('change', async () => {
      const state = document.querySelector(`[data-state="${field.path}"]`);
      const parsed = parseInput(field, input.value);
      if (parsed.error) {
        input.setAttribute('aria-invalid', 'true');
        state.className = 'istate err';
        state.textContent = parsed.error;
        return;
      }
      input.removeAttribute('aria-invalid');
      state.className = 'istate';
      state.textContent = 'Mentés…';
      const draft = structuredClone(property);
      setValue(draft, field.path, parsed.value);
      const error = await save(draft);
      if (error) {
        input.setAttribute('aria-invalid', 'true');
        state.className = 'istate err';
        state.textContent = 'Hiba';
        state.title = error;
        showError(error);
        return;
      }
      if (field.group && parsed.value != null) input.value = formatNumber(parsed.value, parsed.value % 1 ? 2 : 0);
      if (field.type === 'time') input.value = parsed.value;
      lastSaved = input.value;
      state.className = 'istate ok';
      state.textContent = 'Mentve';
      setTimeout(() => { if (state.textContent === 'Mentve') state.textContent = ''; }, 2000);
    });
  });
}

// A teljes ingatlan mentése (a szerver PUT-ot vár minden mezővel). Hibánál a hibaüzenetet adja vissza.
async function save(draft) {
  const payload = {
    name: draft.name,
    rentAmount: draft.rentAmount,
    invoicePrefix: draft.invoicePrefix,
    billingDayOfMonth: draft.billingDayOfMonth,
    billingHour: draft.billingHour,
    billingMinute: draft.billingMinute,
    isActive: draft.isActive,
    owners: draft.owners.map((o) => ({ ownerId: o.ownerId, share: o.share })),
    sheet: draft.sheet,
  };
  const res = await apiFetch(`/api/properties/${propertyId}`, { method: 'PUT', body: JSON.stringify(payload) });
  const data = await readJson(res);
  if (!res.ok) return errorMessage(res, data, 'Nem sikerült menteni.');
  property = data;
  hideError();
  renderHeader();
  renderComputed();
  return null;
}

function showError(message) {
  const el = document.getElementById('load-error');
  el.textContent = message;
  el.classList.add('show');
}
function hideError() { document.getElementById('load-error').classList.remove('show'); }

// ---------- megjelenítés ----------

function currentTenant() {
  const today = new Date().toISOString().slice(0, 10);
  return property.tenants.find((t) => !t.moveOutDate || t.moveOutDate >= today) || null;
}

function rentCurrencyLabel() {
  const c = currentTenant()?.rentCurrency || 'HUF';
  return c === 'HUF' ? 'Ft' : c;
}

function addressLine() {
  const s = property.sheet;
  return [[s.postalCode, s.city].filter(Boolean).join(' '), s.street, s.floorDoor].filter(Boolean).join(', ');
}

function renderHeader() {
  document.title = `${property.name} | Lakáskezelő`;
  document.getElementById('p-name').textContent = property.name;
  document.getElementById('crumb-name').textContent = property.name;
  document.getElementById('p-status').innerHTML = property.isActive
    ? '<span class="status-tag ok">Aktív</span>'
    : '<span class="status-tag inactive">Inaktív</span>';
  const s = property.sheet;
  const facts = [
    addressLine(),
    s.propertyType,
    s.floorArea != null ? `${formatNumber(s.floorArea, s.floorArea % 1 ? 1 : 0)} m²` : null,
    s.rooms != null ? `${String(s.rooms).replace('.', ',')} szoba` : null,
  ].filter(Boolean);
  document.getElementById('p-sub').textContent = facts.join(', ') || 'Töltsd ki az adatlapot: cím, típus, alapterület…';
  document.getElementById('toggle-active-btn').textContent = property.isActive ? 'Inaktiválás' : 'Aktiválás';
}

// A nem szerkeszthető, számolt értékek: következő számlázás, deviza-utótag, összegzés.
function renderComputed() {
  const next = String(property.nextBillingAt);
  const [date, time] = next.split('T');
  const [y, m, d] = date.split('-');
  const shifted = Number(d) !== property.billingDayOfMonth;
  const nextEl = document.getElementById('next-billing');
  if (nextEl) {
    nextEl.innerHTML = property.isActive
      ? `${y}.${m}.${d}. ${time.slice(0, 5)}${shifted ? ' <span class="dim">(munkanapra tolva)</span>' : ''}`
      : '<span class="dim">Inaktív, nem számláz</span>';
  }
  document.querySelectorAll('[data-suffix]').forEach((el) => {
    const field = Object.values(FIELDS).flat().find((f) => f.path === el.dataset.suffix);
    el.textContent = field.suffix();
  });
  renderSummary();
}

function renderPanels() {
  document.getElementById('props-basic').innerHTML = FIELDS.basic.map(renderField).join('');
  document.getElementById('props-billing').innerHTML = FIELDS.billing.map(renderField).join('')
    + '<div class="prop"><dt>Következő számlázás</dt><dd id="next-billing"></dd></div>';
  document.getElementById('props-tech').innerHTML = FIELDS.tech.map(renderField).join('');
  document.getElementById('props-notes').innerHTML = FIELDS.notes.map(renderField).join('');
  Object.values(FIELDS).forEach(bindFields);
  renderOwners();
  renderTenant();
}

function renderOwners() {
  const owners = property.owners;
  document.getElementById('count-owners').textContent = owners.length || '';
  const body = document.getElementById('owners-body');
  if (!owners.length) {
    body.innerHTML = '<div class="panel-empty">Nincs tulajdonos megadva.</div>';
    return;
  }
  const equal = owners.every((o) => !o.share);
  body.innerHTML = owners.map((o) => `
    <div class="owner-line"><b><a class="link" href="owner-detail.html?id=${encodeURIComponent(o.ownerId)}">${escapeHtml(o.ownerName)}</a></b><span class="share-chip">${escapeHtml(o.share || (equal ? `1/${owners.length}` : '–'))}</span></div>
  `).join('');
}

function renderTenant() {
  const t = currentTenant();
  const body = document.getElementById('tenant-body');
  if (!t) {
    body.innerHTML = '<div class="panel-empty">Nincs jelenlegi bérlő.</div>';
    return;
  }
  const row = (label, value) => `<div class="prop"><dt>${label}</dt><dd>${value}</dd></div>`;
  body.innerHTML = [
    row('Név', `<a class="link" href="tenant-detail.html?id=${encodeURIComponent(t.id)}">${escapeHtml(t.name)}</a>`),
    row('E-mail', t.email ? `<a class="link" href="mailto:${escapeHtml(t.email)}">${escapeHtml(t.email)}</a>` : '<span class="dim">–</span>'),
    row('Telefon', t.phone ? escapeHtml(t.phone) : '<span class="dim">–</span>'),
    row('Beköltözés', t.moveInDate ? formatDate(t.moveInDate) : '<span class="dim">–</span>'),
    row('Díj devizája', t.rentCurrency === 'HUF' ? 'Forint' : `${escapeHtml(t.rentCurrency)} <span class="dim">(MNB-árfolyamon számlázva)</span>`),
  ].join('');
}

function renderSummary() {
  const year = new Date().getFullYear();
  const issued = invoices.filter((i) => i.status !== 'Failed');
  const last = issued[0];
  const thisYear = issued.filter((i) => i.periodYear === year).reduce((sum, i) => sum + Number(i.amountTotal), 0);
  const failed = invoices.filter((i) => i.status === 'Failed').length;
  // A következő kiküldés időszakának rezsije kerül a számlára — ezt nézzük, nem az aktuális hónapot.
  const utilityNext = utilityEntries.some((u) => u.year === property.nextPeriodYear && u.month === property.nextPeriodMonth);
  const nextLabel = `${MONTH_NAMES[property.nextPeriodMonth - 1].toLowerCase()}i számlához`;
  const row = (label, value) => `<div class="prop"><dt>${label}</dt><dd>${value}</dd></div>`;
  document.getElementById('summary').innerHTML = [
    row('Utolsó számla', last ? `<span class="mono">${escapeHtml(last.number)}</span> <span class="dim">${formatDate(last.issuedAt)}</span>` : '<span class="dim">Még nincs</span>'),
    row(`Számlázva ${year}-ben`, formatCurrency(thisYear)),
    row('Kiállított számlák', `${issued.length} db${failed ? ` <span class="status-tag missing">${failed} sikertelen</span>` : ''}`),
    row(`Rezsi a ${nextLabel}`, utilityNext ? '<span class="status-tag ok">Rögzítve</span>' : '<span class="status-tag missing">Hiányzik</span>'),
    row('Kaució', property.sheet.deposit != null ? formatCurrency(property.sheet.deposit) : '<span class="dim">–</span>'),
  ].join('');
}

function renderInvoices() {
  document.getElementById('count-invoices').textContent = invoices.length || '';
  const body = document.getElementById('invoices-body');
  if (!invoices.length) {
    body.innerHTML = '<div class="panel-empty">Ehhez az ingatlanhoz még nincs számla.</div>';
    return;
  }
  body.innerHTML = `<table>
    <thead><tr><th>Sorszám</th><th>Időszak</th><th>Bérlő</th><th>Kiállítva</th><th class="num">Összeg</th><th>Állapot</th><th></th></tr></thead>
    <tbody>${invoices.map((i) => `
      <tr>
        <td><span class="mono">${escapeHtml(i.number)}</span></td>
        <td>${i.periodYear}. ${MONTH_NAMES[i.periodMonth - 1].toLowerCase()}</td>
        <td>${escapeHtml(i.tenantName || '–')}</td>
        <td>${formatDate(i.issuedAt)}</td>
        <td class="num">${formatCurrency(i.amountTotal)}</td>
        <td><span class="status-tag ${escapeHtml(i.status)}">${INVOICE_STATUS[i.status] || escapeHtml(i.status)}</span></td>
        <td class="num">${invoiceActionButtons(i)}</td>
      </tr>`).join('')}</tbody></table>`;
}

function renderUtility() {
  const periods = new Map();
  utilityEntries.forEach((u) => {
    const key = `${u.year}-${String(u.month).padStart(2, '0')}`;
    const p = periods.get(key) || { year: u.year, month: u.month, count: 0, total: 0, locked: false };
    p.count += 1;
    p.total += Number(u.amount);
    p.locked = p.locked || u.isLocked;
    periods.set(key, p);
  });
  const list = [...periods.entries()].sort((a, b) => b[0].localeCompare(a[0])).map(([, p]) => p);
  document.getElementById('count-utility').textContent = list.length || '';
  const body = document.getElementById('utility-body');
  if (!list.length) {
    body.innerHTML = '<div class="panel-empty">Még nincs rögzített rezsi.</div>';
    return;
  }
  body.innerHTML = `<table>
    <thead><tr><th>Időszak</th><th class="num">Tételek</th><th class="num">Összeg</th><th>Állapot</th></tr></thead>
    <tbody>${list.map((p) => `
      <tr class="clickable" onclick="openUtility(${p.year}, ${p.month})">
        <td class="cell-main">${p.year}. ${MONTH_NAMES[p.month - 1].toLowerCase()}</td>
        <td class="num">${p.count}</td>
        <td class="num">${formatCurrency(p.total)}</td>
        <td>${p.locked ? '<span class="status-tag inactive">Lezárva</span>' : '<span class="status-tag ok">Rögzítve</span>'}</td>
      </tr>`).join('')}</tbody></table>`;
}

function renderTenantsTab() {
  const tenants = [...property.tenants].sort((a, b) => (b.moveInDate || '').localeCompare(a.moveInDate || ''));
  document.getElementById('count-tenants').textContent = tenants.length || '';
  const body = document.getElementById('tenants-body');
  if (!tenants.length) {
    body.innerHTML = '<div class="panel-empty">Ehhez az ingatlanhoz még nincs bérlő rendelve. A Bérlők oldalon rendelhetsz hozzá.</div>';
    return;
  }
  const current = currentTenant();
  body.innerHTML = `<table>
    <thead><tr><th>Név</th><th>E-mail</th><th>Telefon</th><th>Beköltözés</th><th>Kiköltözés</th><th>Deviza</th><th>Állapot</th></tr></thead>
    <tbody>${tenants.map((t) => `
      <tr>
        <td class="cell-main"><a class="link" href="tenant-detail.html?id=${encodeURIComponent(t.id)}">${escapeHtml(t.name)}</a></td>
        <td>${escapeHtml(t.email || '–')}</td>
        <td>${escapeHtml(t.phone || '–')}</td>
        <td>${formatDate(t.moveInDate)}</td>
        <td>${formatDate(t.moveOutDate)}</td>
        <td>${t.rentCurrency === 'HUF' ? 'Ft' : escapeHtml(t.rentCurrency)}</td>
        <td>${current && t.id === current.id ? '<span class="status-tag ok">Jelenlegi</span>' : '<span class="status-tag inactive">Korábbi</span>'}</td>
      </tr>`).join('')}</tbody></table>`;
}

window.openUtility = (year, month) => {
  window.openUtilityModal(propertyId, property.name, year, month, { onSaved: loadUtility, onClose: loadUtility });
};

// ---------- fülek ----------

function activateTab(name) {
  document.querySelectorAll('.record-tab').forEach((b) => b.classList.toggle('active', b.dataset.tab === name));
  document.querySelectorAll('.record-tab-pane').forEach((p) => { p.hidden = p.dataset.pane !== name; });
  document.querySelectorAll('[data-tab-action]').forEach((b) => { b.hidden = b.dataset.tabAction !== name; });
  try { localStorage.setItem('propertyDetailTab', name); } catch { /* privát mód: nem baj */ }
}
document.querySelectorAll('.record-tab').forEach((b) => b.addEventListener('click', () => activateTab(b.dataset.tab)));
// Új rezsi alapból a következő kiküldés időszakára (annak a számlájára kerül).
document.getElementById('add-utility').addEventListener('click', () => {
  window.openUtility(property.nextPeriodYear, property.nextPeriodMonth);
});

// ---------- masonry: a panelek a mindenkori legrövidebb oszlopba kerülnek ----------

function layoutMasonry() {
  const grid = document.getElementById('masonry');
  const panels = [...grid.querySelectorAll(':scope > .panel, :scope > .masonry-col > .panel')];
  const cols = Math.max(1, Math.min(3, Math.floor(grid.clientWidth / 330)));
  grid.querySelectorAll('.masonry-col').forEach((c) => c.remove());
  grid.classList.add('is-laid-out');
  grid.style.setProperty('--cols', cols);
  const columns = Array.from({ length: cols }, () => {
    const col = document.createElement('div');
    col.className = 'masonry-col';
    grid.appendChild(col);
    return col;
  });
  panels.forEach((panel) => {
    const shortest = columns.reduce((a, b) => (a.offsetHeight <= b.offsetHeight ? a : b));
    shortest.appendChild(panel);
  });
}
let resizeTimer;
window.addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(layoutMasonry, 120); });

// ---------- fejléc-műveletek ----------

document.getElementById('create-invoice-btn').addEventListener('click', () => openInvoiceCreateDialog({ propertyId }));
// Számla készítése / újrapróbálás / törlés után az adatlap (számlák, összegzés, rezsi-zárolás) frissül.
document.addEventListener('invoices-changed', () => load());

document.getElementById('toggle-active-btn').addEventListener('click', async () => {
  if (property.isActive && !(await uiConfirm('Inaktív ingatlanra nem megy ki automatikus számla. Biztosan inaktiválod?', { title: 'Inaktiválás', confirmText: 'Inaktiválás' }))) return;
  const error = await save({ ...structuredClone(property), isActive: !property.isActive });
  if (error) showError(error);
});

document.getElementById('delete-btn').addEventListener('click', async () => {
  if (!(await uiConfirm(`Biztosan törlöd ezt az ingatlant: „${property.name}”? Ez nem vonható vissza.`, { title: 'Törlés', confirmText: 'Törlés', danger: true }))) return;
  const res = await apiFetch(`/api/properties/${propertyId}`, { method: 'DELETE' });
  if (res.ok || res.status === 204) {
    location.href = 'properties.html';
    return;
  }
  showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni az ingatlant (lehet, hogy már van hozzá számla).'));
});

// ---------- tulajdonosok szerkesztése ----------

function ownerRowHtml(ownerId, share) {
  return `<div class="owner-row">
    <select class="owner-select" aria-label="Tulajdonos"><option value="">Válassz…</option>${allOwners
      .map((o) => `<option value="${o.id}"${o.id === ownerId ? ' selected' : ''}>${escapeHtml(o.name)}</option>`).join('')}</select>
    <input type="text" class="owner-share" placeholder="pl. 1/2" aria-label="Tulajdoni hányad" value="${escapeHtml(share || '')}">
    <button class="icon-btn danger" type="button" title="Eltávolítás" aria-label="Eltávolítás" onclick="this.parentElement.remove()">${ICON('trash')}</button>
  </div>`;
}

document.getElementById('edit-owners-btn').addEventListener('click', async () => {
  if (!allOwners.length) allOwners = await (await apiFetch('/api/owners')).json();
  const rows = document.getElementById('owner-rows');
  rows.innerHTML = property.owners.length
    ? property.owners.map((o) => ownerRowHtml(o.ownerId, o.share)).join('')
    : ownerRowHtml('', '');
  document.getElementById('owners-error').classList.remove('show');
  document.getElementById('owners-overlay').classList.add('open');
});
document.getElementById('add-owner-row').addEventListener('click', () => {
  document.getElementById('owner-rows').insertAdjacentHTML('beforeend', ownerRowHtml('', ''));
});
document.querySelectorAll('[data-close-owners]').forEach((b) => b.addEventListener('click', () => {
  document.getElementById('owners-overlay').classList.remove('open');
}));
document.getElementById('save-owners-btn').addEventListener('click', async () => {
  const owners = [...document.querySelectorAll('#owner-rows .owner-row')]
    .map((row) => ({ ownerId: row.querySelector('.owner-select').value, share: row.querySelector('.owner-share').value.trim() || null }))
    .filter((o) => o.ownerId);
  const error = await save({ ...structuredClone(property), owners });
  if (error) {
    const el = document.getElementById('owners-error');
    el.textContent = error;
    el.classList.add('show');
    return;
  }
  document.getElementById('owners-overlay').classList.remove('open');
  renderOwners();
  layoutMasonry();
});

// ---------- betöltés ----------

async function loadUtility() {
  const res = await apiFetch(`/api/properties/${propertyId}/utility-costs`);
  utilityEntries = res.ok ? await res.json() : [];
  renderUtility();
  renderSummary();
}

async function load() {
  const [pRes, iRes, uRes] = await Promise.all([
    apiFetch(`/api/properties/${propertyId}`),
    apiFetch(`/api/properties/${propertyId}/invoices`),
    apiFetch(`/api/properties/${propertyId}/utility-costs`),
  ]);
  if (pRes.status === 404) {
    document.getElementById('p-name').textContent = 'Nincs ilyen ingatlan';
    showError('Az ingatlan nem található. Lehet, hogy törölték.');
    return;
  }
  property = await pRes.json();
  invoices = iRes.ok ? await iRes.json() : [];
  utilityEntries = uRes.ok ? await uRes.json() : [];

  renderHeader();
  renderPanels();
  renderComputed();
  renderInvoices();
  renderUtility();
  renderTenantsTab();
  layoutMasonry();

  let tab = 'invoices';
  try { tab = localStorage.getItem('propertyDetailTab') || tab; } catch { /* nem baj */ }
  activateTab(['invoices', 'utility', 'tenants'].includes(tab) ? tab : 'invoices');
}

load();
