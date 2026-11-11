// Ingatlan adatlap (property-detail.html?id=...). A panelek értékei helyben szerkeszthetők: a módosított
// tábla jobb felső sarkában megjelenő Mentés gombra (vagy Enterre) a teljes ingatlan PUT-tal mentődik — a szerver
// a frissített adatlapot adja vissza, abból frissül a fejléc és a számolt értékek (pl. a munkanapra tolt
// következő kiállítás). A mezőket mentés után nem rajzoljuk újra, hogy a fókusz ne ugorjon el.
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
    { path: 'invoicePrefix', label: 'Bizonylat előtag', placeholder: 'pl. A22', upper: true },
    { path: 'billingDayOfMonth', label: 'Kiállítás napja', type: 'number', integer: true, min: 1, max: 28, required: true, placeholder: '1–28' },
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
    const opts = (typeof field.options === 'function' ? field.options() : field.options).map((o) => (typeof o === 'string' ? { value: o, label: o } : o));
    control = `<select class="iinput" id="${id}"><option value="">${escapeHtml(field.emptyLabel ? field.emptyLabel() : 'Nincs megadva')}</option>${opts
      .map((o) => `<option value="${escapeHtml(o.value)}"${o.value === value ? ' selected' : ''}>${escapeHtml(o.label)}</option>`).join('')}</select>`;
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

// A mezők nem mentődnek maguktól: a módosított tábla jobb felső sarkában megjelenő Mentés gomb menti a tábla
// összes módosított mezőjét egyszerre, a Mégsem visszaírja őket (ld. change-guard.js).
const sheet = ChangeGuard.sheet({
  getValue: (field) => getValue(field.path),
  parse: parseInput,
  format: (field, value) => {
    if (value == null) return '';
    if (field.type === 'number') return field.group ? formatNumber(value, value % 1 ? 2 : 0) : String(value).replace('.', ',');
    return String(value);
  },
  save: async (changes) => {
    const draft = structuredClone(property);
    changes.forEach(({ field, value }) => setValue(draft, field.path, value));
    const error = await save(draft);
    if (error) showError(error);
    return error;
  },
});

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

// A nem szerkeszthető, számolt értékek: következő kiállítás, deviza-utótag, összegzés.
function renderComputed() {
  const next = String(property.nextBillingAt);
  const [date, time] = next.split('T');
  const [y, m, d] = date.split('-');
  const shifted = Number(d) !== property.billingDayOfMonth;
  const nextEl = document.getElementById('next-billing');
  if (nextEl) {
    nextEl.innerHTML = property.isActive
      ? `${y}.${m}.${d}. ${time.slice(0, 5)}${shifted ? ' <span class="dim">(munkanapra tolva)</span>' : ''}`
      : '<span class="dim">Inaktív, nem állít ki bizonylatot</span>';
  }
  document.querySelectorAll('[data-suffix]').forEach((el) => {
    const field = Object.values(FIELDS).flat().find((f) => f.path === el.dataset.suffix);
    el.textContent = field.suffix();
  });
  renderSummary();
}

// Cégnél, összekötött számlázóval az ingatlan áfája is beállítható (üresen a cég alapértelmezése).
let company = null;
const VAT_LABELS = { TAM: 'Tárgyi adómentes (TAM)', AAM: 'Alanyi adómentes (AAM)', 27: '27%', 18: '18%', 5: '5%', 0: '0%' };
const VAT_OPTIONS = ['TAM', 'AAM', '27', '18', '5', '0'].map((c) => ({ value: c, label: VAT_LABELS[c] }));
const VAT_FIELDS = [
  { path: 'sheet.rentVat', label: 'Bérleti díj áfája', type: 'select', options: VAT_OPTIONS, emptyLabel: () => `Alapértelmezés: ${VAT_LABELS[company?.defaultRentVat] || 'TAM'}` },
  { path: 'sheet.utilityVat', label: 'Rezsi áfája', type: 'select', options: VAT_OPTIONS, emptyLabel: () => `Alapértelmezés: ${VAT_LABELS[company?.defaultUtilityVat] || 'TAM'}` },
];

function renderPanels() {
  FIELDS.billing = FIELDS.billing.filter((f) => !VAT_FIELDS.includes(f));
  if (company?.invoiceProvider) FIELDS.billing.push(...VAT_FIELDS);
  document.getElementById('props-basic').innerHTML = FIELDS.basic.map(renderField).join('');
  document.getElementById('props-billing').innerHTML = FIELDS.billing.map(renderField).join('')
    + '<div class="prop"><dt>Következő kiállítás</dt><dd id="next-billing"></dd></div>';
  document.getElementById('props-tech').innerHTML = FIELDS.tech.map(renderField).join('');
  document.getElementById('props-notes').innerHTML = FIELDS.notes.map(renderField).join('');
  sheet.bind(Object.values(FIELDS).flat());
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
    row('E-mail', t.email ? splitEmails(t.email).map((a) => `<a class="link" href="mailto:${escapeHtml(a)}">${escapeHtml(a)}</a>`).join('<br>') : '<span class="dim">–</span>'),
    row('Telefon', t.phone ? escapeHtml(t.phone) : '<span class="dim">–</span>'),
    row('Beköltözés', t.moveInDate ? formatDate(t.moveInDate) : '<span class="dim">–</span>'),
    ...(t.leaseEndDate ? [row('Szerződés lejárata', `${formatDate(t.leaseEndDate)} ${leaseEndTag(t.leaseEndDate, company?.leaseExpiryNoticeDays)}`)] : []),
    row('Díj devizája', t.rentCurrency === 'HUF' ? 'Forint' : `${escapeHtml(t.rentCurrency)} <span class="dim">(MNB-árfolyamon átszámítva)</span>`),
  ].join('');
}

// Idei csempék a fejléc alatt: a rezsi megoszlása körcikk-diagramon, a bevétel (az idei időszakokra kiállított
// bizonylatok) és a kiadások (az idén az aktuális hónapig felvett rezsi; a jövőbeli hónapok előre kitöltött rezsije
// nem számít). A megnevezés szabad szöveg, ezért kis-nagybetűtől függetlenül csoportosítunk; a legnagyobb öt
// tétel kap külön szeletet, a többi „Egyéb”. A jóváírás (negatív összegű tétel) nem szelet: külön sorban áll.
const TILE_CATEGORY_LIMIT = 5;
function renderYearTiles() {
  const el = document.getElementById('year-tiles');
  const now = new Date();
  const year = now.getFullYear(), month = now.getMonth() + 1;
  const issued = invoices.filter((i) => i.status !== 'Failed' && !i.cancelledAt && !i.cancelsInvoiceId && i.periodYear === year);
  const income = issued.reduce((sum, i) => sum + Number(i.amountTotal), 0);
  const costs = utilityEntries.filter((u) => u.year === year && u.month <= month && Number(u.amount) !== 0);
  const expense = costs.reduce((sum, u) => sum + Number(u.amount), 0);

  const groups = new Map();
  for (const u of costs) {
    const name = (u.label || '').trim() || 'Rezsi';
    const key = name.toLowerCase();
    const g = groups.get(key) || { name, value: 0 };
    g.value += Number(u.amount);
    groups.set(key, g);
  }
  const positive = [...groups.values()].filter((g) => g.value > 0).sort((a, b) => b.value - a.value || a.name.localeCompare(b.name, 'hu'));
  const credit = [...groups.values()].filter((g) => g.value < 0).reduce((sum, g) => sum + g.value, 0);
  const top = positive.length > TILE_CATEGORY_LIMIT + 1 ? positive.slice(0, TILE_CATEGORY_LIMIT) : positive;
  const rest = positive.slice(top.length);
  const slices = top.map((g, i) => ({ name: g.name, value: g.value, color: VizCharts.categoryColor(el, i, false) }));
  if (rest.length) slices.push({ name: `Egyéb (${rest.length} tétel)`, value: rest.reduce((sum, g) => sum + g.value, 0), color: VizCharts.categoryColor(el, 0, true) });
  const total = slices.reduce((sum, s) => sum + s.value, 0);
  const pct = (v) => `${Math.round((v / total) * 100)}%`;

  const legend = slices.map((s) => `<div class="row"><span class="sw" style="background:${s.color}"></span><span class="name" title="${escapeHtml(s.name)}">${escapeHtml(s.name)}</span>`
    + `<span class="val">${formatCurrency(s.value)}</span><span class="pct">${pct(s.value)}</span></div>`).join('')
    + (credit < 0 ? `<div class="row credit"><span class="sw"></span><span class="name">Jóváírás</span><span class="val">${formatCurrency(credit)}</span><span class="pct"></span></div>` : '');
  const tile = (label, value, sub) => `<div class="stat-tile"><span class="stat-tile-label">${escapeHtml(label)}</span>`
    + `<span class="stat-tile-value">${value}</span><span class="stat-tile-sub">${sub}</span></div>`;
  el.innerHTML = `<div class="stat-tile wide"><span class="stat-tile-label">Rezsi megoszlása (${year})</span>`
    + `<div class="stat-tile-chart"><div class="viz-plot" id="year-donut"></div>`
    + `<div class="donut-legend">${legend || '<span class="dim">Idén még nincs rögzített rezsi.</span>'}</div></div></div>`
    + tile(`Bevétel (${year})`, formatCurrency(income), issued.length ? `${issued.length} db kiállított bizonylat` : 'még nincs kiállított bizonylat')
    + tile(`Kiadások (${year})`, formatCurrency(expense), costs.length ? `rezsi ${MONTH_NAMES[month - 1].toLowerCase()}ig, ${costs.length} tétel` : 'még nincs rögzített rezsi');
  VizCharts.renderDonut(document.getElementById('year-donut'), {
    slices,
    ariaLabel: `A rezsi megoszlása tételenként, ${year}`,
    tooltip: (i) => ({ title: slices[i].name, rows: [{ color: slices[i].color, label: `${year}`, value: formatCurrency(slices[i].value) }], foot: `A rezsi ${pct(slices[i].value)}-a` }),
  });
}

function renderSummary() {
  renderYearTiles();
  const year = new Date().getFullYear();
  const issued = invoices.filter((i) => i.status !== 'Failed' && (!i.cancelledAt && !i.cancelsInvoiceId));
  const last = issued[0];
  const thisYear = issued.filter((i) => i.periodYear === year).reduce((sum, i) => sum + Number(i.amountTotal), 0);
  const failed = invoices.filter((i) => i.status === 'Failed' && (!i.cancelledAt && !i.cancelsInvoiceId)).length;
  // A következő kiküldés időszakának rezsije kerül a bizonylatra — ezt nézzük, nem az aktuális hónapot.
  // Ha a hónapnak már vannak tételei, azokból számolunk (a rezsi ablak bezárása után is friss); különben a
  // szerver állapota dönt (pl. csak fix rezsi tételek: maguktól bekerülnek).
  // Egy hónap csúszással fizető bérlőnél a bizonylat az előző hónap rezsijét viszi (utilityPeriod…).
  const utilityYear = property.utilityPeriodYear || property.nextPeriodYear;
  const utilityMonth = property.utilityPeriodMonth || property.nextPeriodMonth;
  const lagged = utilityYear !== property.nextPeriodYear || utilityMonth !== property.nextPeriodMonth;
  const nextEntries = utilityEntries.filter((u) => u.year === utilityYear && u.month === utilityMonth);
  const utilityState = nextEntries.length ? (nextEntries.some((u) => u.isPending) ? 'Pending' : 'Ok') : property.utilityState;
  const nextLabel = `${MONTH_NAMES[property.nextPeriodMonth - 1].toLowerCase()}i bizonylathoz`;
  const row = (label, value) => `<div class="prop"><dt>${label}</dt><dd>${value}</dd></div>`;
  document.getElementById('summary').innerHTML = [
    row('Utolsó bizonylat', last ? `<span class="mono">${escapeHtml(last.number)}</span> <span class="dim">${formatDate(last.issuedAt)}</span>` : '<span class="dim">Még nincs</span>'),
    row(`Kiállítva ${year}-ben`, formatCurrency(thisYear)),
    row('Kiállított bizonylatok', `${issued.length} db${failed ? ` <span class="status-tag missing">${failed} sikertelen</span>` : ''}`),
    row(`Rezsi a ${nextLabel}`, utilityStateTag(utilityState, nextEntries.length ? 'Rögzítve' : 'Automatikus')
      + (lagged ? ` <span class="dim">${MONTH_NAMES[utilityMonth - 1].toLowerCase()}i rezsi</span>` : '')),
    row('Kaució', property.sheet.deposit != null ? formatCurrency(property.sheet.deposit) : '<span class="dim">–</span>'),
  ].join('');
}

function renderInvoices() {
  document.getElementById('count-invoices').textContent = invoices.length || '';
  const body = document.getElementById('invoices-body');
  if (!invoices.length) {
    body.innerHTML = '<div class="panel-empty">Ehhez az ingatlanhoz még nincs bizonylat.</div>';
    return;
  }
  body.innerHTML = `<table>
    <thead><tr><th>Sorszám</th><th>Időszak</th><th>Bérlő</th><th>Kiállítva</th><th class="num">Összeg</th><th>Állapot</th><th></th></tr></thead>
    <tbody>${invoices.map((i) => `
      <tr${(i.cancelledAt || i.cancelsInvoiceId) ? ' class="is-archived"' : ''}>
        <td><a class="mono link" href="${invoiceDetailUrl(i, `property:${propertyId}`)}">${escapeHtml(i.number)}</a></td>
        <td>${i.periodYear}. ${MONTH_NAMES[i.periodMonth - 1].toLowerCase()}</td>
        <td>${escapeHtml(i.tenantName || '–')}</td>
        <td>${formatDate(i.issuedAt)}</td>
        <td class="num">${formatCurrency(i.amountTotal)}</td>
        <td>${invoiceStatusTag(i, INVOICE_STATUS)}</td>
        <td class="num">${invoiceActionButtons(i)}</td>
      </tr>`).join('')}</tbody></table>`;
}

function renderUtility() {
  const periods = new Map();
  utilityEntries.forEach((u) => {
    const key = `${u.year}-${String(u.month).padStart(2, '0')}`;
    const p = periods.get(key) || { year: u.year, month: u.month, count: 0, total: 0, entries: [] };
    p.count += 1;
    p.total += Number(u.amount);
    p.entries.push(u);
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
        <td>${utilityPeriodTag(p.entries)}</td>
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

// ---------- Rezsi tételek fül (havonta ismétlődő rezsi) ----------

let utilityItems = [];
let editingItemId = null;
const FORWARD_HINT = {
  true: 'A bérleti díj már tartalmazza: nem kerül külön a bérlő bizonylatára, csak a tulajdonosi elszámolásban szerepel.',
  false: 'Külön sorként rákerül a bérlő bizonylatára a bérleti díj mellé.',
};

function renderUtilityItems() {
  document.getElementById('count-utilityitems').textContent = utilityItems.length || '';
  const body = document.getElementById('utilityitems-body');
  if (!utilityItems.length) {
    body.innerHTML = '<div class="panel-empty">Még nincs rezsi tétel. Az „Új rezsi tétel” gombbal veheted fel a havonta ismétlődő rezsit.</div>';
    return;
  }
  body.innerHTML = `<table>
    <thead><tr><th>Megnevezés</th><th class="num">Összeg</th><th>Fix</th><th>Tovább számlázandó</th><th></th></tr></thead>
    <tbody>${utilityItems.map((i) => `
      <tr>
        <td class="cell-main">${escapeHtml(i.label)}</td>
        <td class="num">${i.isFixed ? formatCurrency(i.amount) : '<span class="dim">havonta kitöltendő</span>'}</td>
        <td>${i.isFixed ? '<span class="status-tag ok">Fix</span>' : '<span class="status-tag warn">Nem fix</span>'}</td>
        <td title="${escapeHtml(FORWARD_HINT[i.forwardBilled])}">${i.forwardBilled ? '<span class="status-tag inactive">Igen</span>' : 'Nem'}</td>
        <td class="num row-actions"><button class="btn btn-ghost btn-sm" type="button" onclick="editUtilityItem('${i.id}')">Szerkesztés</button>
          <button class="btn btn-danger btn-sm" type="button" onclick="deleteUtilityItem('${i.id}')">Törlés</button></td>
      </tr>`).join('')}</tbody></table>`;
}

async function loadUtilityItems() {
  const res = await apiFetch(`/api/properties/${propertyId}/utility-items`);
  utilityItems = res.ok ? await res.json() : [];
  renderUtilityItems();
}

// A rezsi tételek változása után a következő bizonylat rezsi-állapota is változhat (Összegzés tábla).
async function refreshUtilityState() {
  const res = await apiFetch(`/api/properties/${propertyId}`);
  if (!res.ok) return;
  property.utilityState = (await res.json()).utilityState;
  renderSummary();
}

function syncItemForm() {
  const fixed = document.getElementById('ui-item-fixed').checked;
  // Az összeg mező a helyén marad (a pipák ne ugráljanak): nem fix tételnél nem tölthető ki.
  const amount = document.getElementById('ui-item-amount');
  amount.disabled = !fixed;
  amount.placeholder = fixed ? '' : 'havonta kell kitölteni';
  if (!fixed) amount.value = '';
  document.getElementById('ui-item-fixed-hint').textContent = fixed
    ? 'Minden hónapban ezzel az összeggel kerül a hónap rezsijébe.'
    : 'Az összeget havonta kell kitölteni. Amíg nincs kitöltve, a tétel nem kerül bizonylatra.';
  document.getElementById('ui-item-forward-hint').textContent = FORWARD_HINT[document.getElementById('ui-item-forward').checked];
}

function openItemDialog(item = null) {
  editingItemId = item?.id || null;
  document.getElementById('ui-item-title').textContent = item ? 'Rezsi tétel szerkesztése' : 'Új rezsi tétel';
  document.getElementById('ui-item-label').value = item?.label || '';
  document.getElementById('ui-item-fixed').checked = item ? item.isFixed : true;
  document.getElementById('ui-item-amount').value = item?.amount ?? '';
  document.getElementById('ui-item-forward').checked = item?.forwardBilled || false;
  document.getElementById('ui-item-error').classList.remove('show');
  syncItemForm();
  document.getElementById('ui-item-overlay').classList.add('open');
  setTimeout(() => document.getElementById('ui-item-label').focus(), 50);
}
const closeItemDialog = () => document.getElementById('ui-item-overlay').classList.remove('open');

window.editUtilityItem = (id) => openItemDialog(utilityItems.find((i) => i.id === id));
window.deleteUtilityItem = async (id) => {
  const item = utilityItems.find((i) => i.id === id);
  if (!item) return;
  const ok = await uiConfirm(`Törlöd a(z) „${item.label}” rezsi tételt? A már elkészült havi tételek megmaradnak, de a következő hónapokba már nem kerül be.`,
    { title: 'Rezsi tétel törlése', confirmText: 'Törlés', danger: true });
  if (!ok) return;
  const res = await apiFetch(`/api/properties/${propertyId}/utility-items/${id}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 204) { await uiAlert(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'), { title: 'Törlés sikertelen' }); return; }
  await loadUtilityItems();
  refreshUtilityState();
};

document.getElementById('add-utility-item').addEventListener('click', () => openItemDialog());
document.querySelectorAll('[data-close-item]').forEach((b) => b.addEventListener('click', closeItemDialog));
document.getElementById('ui-item-overlay').addEventListener('mousedown', (e) => { if (e.target.id === 'ui-item-overlay') closeItemDialog(); });
document.getElementById('ui-item-fixed').addEventListener('change', syncItemForm);
document.getElementById('ui-item-forward').addEventListener('change', syncItemForm);
document.getElementById('ui-item-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const errorEl = document.getElementById('ui-item-error');
  const fail = (message) => { errorEl.textContent = message; errorEl.classList.add('show'); };
  const isFixed = document.getElementById('ui-item-fixed').checked;
  const amount = Number(document.getElementById('ui-item-amount').value);
  const payload = {
    label: document.getElementById('ui-item-label').value.trim(),
    isFixed,
    amount: isFixed ? amount : null,
    forwardBilled: document.getElementById('ui-item-forward').checked,
  };
  if (!payload.label) { fail('Add meg a tétel megnevezését.'); return; }
  if (isFixed && !(amount > 0)) { fail('Fix tételnél add meg az összeget.'); return; }
  const save = document.getElementById('ui-item-save');
  save.disabled = true;
  try {
    const res = await apiFetch(`/api/properties/${propertyId}/utility-items${editingItemId ? `/${editingItemId}` : ''}`,
      { method: editingItemId ? 'PUT' : 'POST', body: JSON.stringify(payload) });
    if (!res.ok) { fail(errorMessage(res, await readJson(res), 'Nem sikerült menteni.')); return; }
    closeItemDialog();
    await loadUtilityItems();
    refreshUtilityState();
  } finally {
    save.disabled = false;
  }
});

// ---------- Beköltözés fül ----------

// Egy szabad szöveg az ingatlanhoz (kapukód, wifi, mérőórák…), amit egy gombbal ki lehet küldeni a bérlőnek.
// A szöveg és a „kiküldhető” pipa a fülsáv jobb oldalán megjelenő Mentés gombbal mentődik; a kiküldés mindig a
// mentett szöveget viszi, ezért mentetlen módosítással nem indítható.
let moveIn = null;          // a szerver utolsó válasza
const MOVEIN_MAX = 5000;
const mi = (id) => document.getElementById(id);

function moveInState(text, kind) {
  const el = mi('mi-state');
  el.textContent = text || '';
  el.className = `movein-state${kind ? ` is-${kind}` : ''}`;
}

function moveInRecipient() {
  return moveIn?.recipients.find((r) => r.tenantId === mi('mi-recipient').value) || moveIn?.recipients[0] || null;
}

function moveInDirty() {
  if (!moveIn) return false;
  return (mi('mi-text').value.replace(/\r\n?/g, '\n').trim() || null) !== (moveIn.text || null) || mi('mi-sendable').checked !== moveIn.sendable;
}
ChangeGuard.watch(moveInDirty);

function renderMoveInSide() {
  const recipients = moveIn.recipients;
  const select = mi('mi-recipient');
  const keep = select.value;
  select.innerHTML = recipients.map((r) => `<option value="${escapeHtml(r.tenantId)}">${escapeHtml(r.name)}</option>`).join('');
  if (recipients.some((r) => r.tenantId === keep)) select.value = keep;
  mi('mi-recipient-field').hidden = recipients.length < 2;

  const r = moveInRecipient();
  const info = mi('mi-recipient-info');
  if (!r) {
    info.innerHTML = '<span class="dim">Az ingatlannak nincs jelenlegi vagy leendő bérlője.</span>';
  } else {
    const sent = r.sentAt
      ? `<span class="status-tag ok">Kiküldve</span> <span class="dim">${formatDate(r.sentAt)}</span>`
      : '<span class="status-tag inactive">Még nem ment ki</span>';
    info.innerHTML = `${recipients.length < 2 ? `<strong>${escapeHtml(r.name)}</strong><br>` : ''}${
      r.email ? escapeHtml(r.email) : '<span class="dim">Nincs e-mail címe</span>'}<br>${sent}`;
  }

  const dirty = moveInDirty();
  mi('mi-save').hidden = !dirty;
  let reason = '';
  if (dirty) reason = 'Előbb mentsd el a módosítást.';
  else if (!moveIn.text) reason = 'Előbb írd be és mentsd el a tudnivalókat.';
  else if (!moveIn.sendable) reason = 'A szöveg belső feljegyzés: pipáld be, hogy kiküldhető.';
  else if (!r) reason = 'Rendelj bérlőt az ingatlanhoz a Bérlők oldalon.';
  else if (!r.email) reason = 'Add meg a bérlő e-mail címét az adatlapján.';
  else if (!moveIn.emailConfigured) reason = 'Az e-mail küldés nincs beállítva.';
  mi('mi-send').disabled = !!reason;
  mi('mi-send-hint').textContent = reason || (r.sentAt ? 'Újra is kiküldheted, például ha változott a szöveg.' : '');
}

function renderMoveIn() {
  mi('mi-text').value = moveIn.text || '';
  mi('mi-sendable').checked = moveIn.sendable;
  mi('mi-count').textContent = `${mi('mi-text').value.length} / ${MOVEIN_MAX}`;
  renderMoveInSide();
}

function moveInChanged() {
  mi('mi-count').textContent = `${mi('mi-text').value.length} / ${MOVEIN_MAX}`;
  moveInState(moveInDirty() ? 'Nincs mentve' : '');
  renderMoveInSide();
}
mi('mi-text').addEventListener('input', moveInChanged);
mi('mi-sendable').addEventListener('change', moveInChanged);
mi('mi-recipient').addEventListener('change', renderMoveInSide);

mi('mi-cancel-btn').addEventListener('click', () => {
  renderMoveIn();
  moveInState('');
});

mi('mi-save-btn').addEventListener('click', async () => {
  if (!moveInDirty()) return;
  const buttons = [mi('mi-save-btn'), mi('mi-cancel-btn')];
  buttons.forEach((b) => { b.disabled = true; });
  moveInState('Mentés…');
  try {
    const res = await apiFetch(`/api/properties/${propertyId}/move-in-info`, {
      method: 'PUT',
      body: JSON.stringify({ text: mi('mi-text').value, sendable: mi('mi-sendable').checked }),
    });
    if (!res.ok) {
      moveInState((await res.json().catch(() => null))?.message || 'A mentés nem sikerült.', 'error');
      return;
    }
    moveIn = await res.json();
    moveInState('Mentve', 'ok');
  } catch {
    moveInState('A mentés nem sikerült.', 'error');
  } finally {
    buttons.forEach((b) => { b.disabled = false; });
    renderMoveInSide();
  }
});

mi('mi-send').addEventListener('click', async () => {
  if (moveInDirty()) return;
  const r = moveInRecipient();
  if (!r?.email) return;
  const again = r.sentAt ? ` Egyszer már megkapta (${formatDate(r.sentAt)}).` : '';
  const ok = await uiConfirm(`Kiküldöd a beköltözési tudnivalókat e-mailben?\nCímzett: ${r.name} (${r.email}).${again}`,
    { title: 'Beköltözési tudnivalók kiküldése', confirmText: 'Kiküldés' });
  if (!ok) return;
  const btn = mi('mi-send');
  btn.disabled = true;
  moveInState('Küldés…');
  try {
    const res = await apiFetch(`/api/properties/${propertyId}/move-in-info/send`, { method: 'POST', body: JSON.stringify({ tenantId: r.tenantId }) });
    if (!res.ok) {
      moveInState('');
      await uiAlert((await res.json().catch(() => null))?.message || 'Az e-mailt nem sikerült elküldeni.', { title: 'Nem sikerült' });
      return;
    }
    moveIn = await res.json();
    moveInState('Kiküldve', 'ok');
  } catch {
    moveInState('');
    await uiAlert('Az e-mailt nem sikerült elküldeni. Ellenőrizd az internetkapcsolatot.', { title: 'Nem sikerült' });
  } finally {
    renderMoveInSide();
  }
});

async function loadMoveIn() {
  const res = await apiFetch(`/api/properties/${propertyId}/move-in-info`);
  if (!res.ok) return;
  moveIn = await res.json();
  renderMoveIn();
}

// ---------- fülek ----------

function activateTab(name) {
  document.querySelectorAll('.record-tab').forEach((b) => b.classList.toggle('active', b.dataset.tab === name));
  document.querySelectorAll('.record-tab-pane').forEach((p) => { p.hidden = p.dataset.pane !== name; });
  document.querySelectorAll('[data-tab-action]').forEach((b) => { b.hidden = b.dataset.tabAction !== name; });
  try { localStorage.setItem('propertyDetailTab', name); } catch { /* privát mód: nem baj */ }
}
document.querySelectorAll('.record-tab').forEach((b) => b.addEventListener('click', () => activateTab(b.dataset.tab)));
// Új rezsi alapból a következő bizonylat rezsi-hónapjára (annak a bizonylatára kerül).
document.getElementById('add-utility').addEventListener('click', () => {
  window.openUtility(property.utilityPeriodYear || property.nextPeriodYear, property.utilityPeriodMonth || property.nextPeriodMonth);
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
// Bizonylat készítése / újrapróbálás / törlés után az adatlap (bizonylatok, összegzés, rezsi-zárolás) frissül.
document.addEventListener('invoices-changed', () => load());

document.getElementById('toggle-active-btn').addEventListener('click', async () => {
  if (property.isActive && !(await uiConfirm('Inaktív ingatlanra nem megy ki automatikus bizonylat. Biztosan inaktiválod?', { title: 'Inaktiválás', confirmText: 'Inaktiválás' }))) return;
  const error = await save({ ...structuredClone(property), isActive: !property.isActive });
  if (error) showError(error);
});

document.getElementById('delete-btn').addEventListener('click', async () => {
  if (!(await uiConfirm(`Biztosan törlöd ezt az ingatlant: „${property.name}”? Ez nem vonható vissza.`, { title: 'Törlés', confirmText: 'Törlés', danger: true }))) return;
  const res = await apiFetch(`/api/properties/${propertyId}`, { method: 'DELETE' });
  if (res.ok || res.status === 204) {
    ChangeGuard.allowLeave();
    location.href = 'properties.html';
    return;
  }
  showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni az ingatlant (lehet, hogy már van hozzá bizonylat).'));
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

// ---------- dokumentumok ----------

// A fül egyszer épül fel (az első betöltéskor); a bérlő-választóban az ingatlan bérlői, a jelenlegi elöl.
let documentsMounted = false;
function mountPropertyDocuments() {
  if (documentsMounted) return;
  documentsMounted = true;
  mountDocuments({
    scope: { propertyId },
    container: '#documents-body',
    count: '#count-documents',
    uploadButton: '#upload-doc',
    links: async (doc) => {
      const current = currentTenant();
      const tenants = [...property.tenants].sort((a, b) => (a === current ? -1 : b === current ? 1 : (b.moveInDate || '').localeCompare(a.moveInDate || '')));
      const options = tenants.map((t) => ({ value: t.id, label: t === current ? `${t.name} (jelenlegi)` : t.name }));
      if (doc?.tenantId && !options.some((o) => o.value === doc.tenantId)) options.push({ value: doc.tenantId, label: doc.tenantName });
      return [{ key: 'tenantId', label: 'Bérlőhöz is tartozik', options, value: '', hint: 'Pl. bérleti szerződésnél: így a bérlő adatlapján is megjelenik.' }];
    },
  });
}

// ---------- betöltés ----------

async function loadUtility() {
  const res = await apiFetch(`/api/properties/${propertyId}/utility-costs`);
  utilityEntries = res.ok ? await res.json() : [];
  renderUtility();
  renderSummary();
}

async function load() {
  const [pRes, iRes, uRes, cRes] = await Promise.all([
    apiFetch(`/api/properties/${propertyId}`),
    apiFetch(`/api/properties/${propertyId}/invoices`),
    apiFetch(`/api/properties/${propertyId}/utility-costs`),
    company ? null : apiFetch('/api/company'),
  ]);
  if (cRes?.ok) company = await cRes.json();
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
  // #fülnév a címben (pl. a dokumentum adatlapjáról visszalépve: …#documents) elsőbbséget kap.
  const tabs = ['invoices', 'utility', 'utilityitems', 'tenants', 'movein', 'documents'];
  if (tabs.includes(location.hash.slice(1))) tab = location.hash.slice(1);
  activateTab(tabs.includes(tab) ? tab : 'invoices');
  mountPropertyDocuments();
  loadMoveIn();
  loadUtilityItems();
}

load();
