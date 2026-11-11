// Bérlő adatlap (tenant-detail.html?id=...) — az ingatlan-adatlap mintájára: helyben szerkeszthető adatok
// panelekben (minden módosítás a teljes bérlő PUT-jával mentődik), alatta a bérlő bizonylatai.
requireAuth();

const tenantId = new URLSearchParams(location.search).get('id');
if (!tenantId) location.replace('tenants.html');

let tenant = null;
let properties = [];
let invoices = [];
let ledger = null;
let company = null;

const editor = createInlineEditor({ getRecord: () => tenant, save });

const FIELDS = {
  basic: [
    { path: 'name', label: 'Név', required: true },
    { path: 'email', label: 'E-mail', type: 'email', multiple: true, placeholder: 'ide megy a bizonylat; több cím vesszővel' },
    { path: 'phone', label: 'Telefon' },
    { path: 'taxId', label: 'Adószám' },
    { path: 'address', label: 'Cím a bizonylaton', placeholder: 'ha eltér az ingatlan címétől' },
  ],
  lease: () => [
    { path: 'propertyId', label: 'Ingatlan', type: 'select', options: properties.map((p) => ({ value: p.id, label: p.name })) },
    { path: 'moveInDate', label: 'Beköltözés', type: 'date', placeholder: 'ÉÉÉÉ. HH. NN.' },
    { path: 'moveOutDate', label: 'Kiköltözés', type: 'date', placeholder: 'ÉÉÉÉ. HH. NN.' },
    { path: 'rentCurrency', label: 'Bérleti díj devizája', type: 'select', required: true, options: [{ value: 'HUF', label: 'Forint (Ft)' }, { value: 'EUR', label: 'Euró (EUR), MNB-árfolyamon átszámítva' }] },
    { path: 'rentPaymentTiming', label: 'Lakbér fizetése', type: 'select', required: true, options: [
      { value: 'Advance', label: 'Előre (tárgyhó)' },
      { value: 'Arrears', label: 'Utólag (előző hó)' },
    ] },
    // A rezsitételek csatolmányai (pl. szolgáltatói számlák) a bizonylat levelével: ha nincs beállítva, kézi
    // bizonylatkészítésnél a program rákérdez, az időzített küldés pedig mellékletek nélkül megy.
    { path: 'invoiceAttachments', label: 'Rezsi mellékletek', type: 'select', required: true, options: [
      { value: 'Ask', label: 'Nincs beállítva (rákérdez)' },
      { value: 'Send', label: 'Kapja a bizonylattal' },
      { value: 'DontSend', label: 'Nem kapja' },
    ] },
    // Csak magánszemély bérbeadónál: ha a bérlő fizeti be az SZJA-t, a bizonylaton a lakbér 15%-a levonódik.
    ...(company && !company.isBusiness ? [{ path: 'tenantPaysSzja', label: 'SZJA-t fizeti', type: 'select', required: true, options: [
      { value: 'false', label: 'A bérbeadó' },
      { value: 'true', label: 'A bérlő (−15%)' },
    ] }] : []),
  ],
  notes: [
    { path: 'notes', label: 'Megjegyzés', type: 'textarea', wide: true, placeholder: 'Bármi, amit a bérlőről tudni kell (szerződés, kaució, kulcsok…)' },
  ],
};

async function save(draft) {
  const payload = {
    name: draft.name, email: draft.email, phone: draft.phone, address: draft.address, taxId: draft.taxId,
    propertyId: draft.propertyId || null, moveInDate: draft.moveInDate, moveOutDate: draft.moveOutDate,
    notes: draft.notes, rentCurrency: draft.rentCurrency,
    rentPaymentTiming: draft.rentPaymentTiming,
    invoiceAttachments: draft.invoiceAttachments,
    tenantPaysSzja: draft.tenantPaysSzja === true || draft.tenantPaysSzja === 'true',
  };
  const res = await apiFetch(`/api/tenants/${tenantId}`, { method: 'PUT', body: JSON.stringify(payload) });
  const data = await readJson(res);
  if (!res.ok) {
    showError(errorMessage(res, data, 'Nem sikerült menteni.'));
    return errorMessage(res, data, 'Nem sikerült menteni.');
  }
  tenant = data;
  hideError();
  renderHeader();
  renderProperty();
  renderSummary();
  return null;
}

function showError(message) { const el = document.getElementById('load-error'); el.textContent = message; el.classList.add('show'); }
function hideError() { document.getElementById('load-error').classList.remove('show'); }

function isCurrent() {
  const today = new Date().toISOString().slice(0, 10);
  return !!tenant.propertyId && (!tenant.moveOutDate || tenant.moveOutDate >= today);
}

function renderHeader() {
  document.title = `${tenant.name} | Lakáskezelő`;
  document.getElementById('p-name').textContent = tenant.name;
  document.getElementById('crumb-name').textContent = tenant.name;
  document.getElementById('p-status').innerHTML = !tenant.propertyId
    ? '<span class="status-tag inactive">Nincs ingatlanhoz rendelve</span>'
    : isCurrent() ? '<span class="status-tag ok">Jelenlegi bérlő</span>' : '<span class="status-tag inactive">Kiköltözött</span>';
  document.getElementById('p-sub').textContent = [tenant.propertyName, tenant.email, tenant.phone].filter(Boolean).join(', ')
    || 'Add meg az elérhetőségeket, a bizonylat az e-mail címre megy.';
}

function renderProperty() {
  const prop = properties.find((p) => p.id === tenant.propertyId);
  const link = document.getElementById('property-link');
  const body = document.getElementById('property-body');
  if (!prop) {
    link.hidden = true;
    body.innerHTML = '<div class="panel-empty">Nincs ingatlanhoz rendelve. A Bérlet panelen választhatsz.</div>';
    return;
  }
  link.hidden = false;
  link.href = `property-detail.html?id=${encodeURIComponent(prop.id)}`;
  const currency = tenant.rentCurrency === 'HUF' ? 'Ft' : tenant.rentCurrency;
  body.innerHTML = [
    detailRow('Megnevezés', `<a class="link" href="property-detail.html?id=${encodeURIComponent(prop.id)}">${escapeHtml(prop.name)}</a>`),
    detailRow('Bérleti díj', `${formatNumber(prop.rentAmount, prop.rentAmount % 1 ? 2 : 0)} ${escapeHtml(currency)} / hó`),
    detailRow('Kiállítás napja', `minden hónap ${prop.billingDayOfMonth}. napja`),
    detailRow('Állapot', prop.isActive ? '<span class="status-tag ok">Aktív</span>' : '<span class="status-tag inactive">Inaktív</span>'),
  ].join('');
}

// Egyenleg-sáv a fejléc alatt: egyenleg (tartozás / túlfizetés / rendezett), kiállítva, befizetve, lejárt hátralék,
// utolsó befizetés; gyorsgombok a befizetés rögzítéséhez és a folyószámlához.
function renderBalanceBand() {
  const band = document.getElementById('balance-band');
  const balance = Number(ledger ? ledger.balance : tenant.balance) || 0;
  const today = new Date().toISOString().slice(0, 10);
  const overdue = invoices.filter((i) => invoiceIsActive(i) && i.status !== 'Failed' && i.dueDate < today && invoiceRemaining(i) > 0);
  // A bizonylathoz nem kötött befizetések is csökkentik: a lejárt hátralék legfeljebb a teljes tartozás.
  // A még bizonylatra nem került rezsi nem lehet lejárt, ezért az a felső határból kimarad.
  const billedBalance = balance - (Number(ledger?.unbilledTotal) || 0);
  const overdueSum = Math.min(overdue.reduce((s, i) => s + invoiceRemaining(i), 0), Math.max(billedBalance, 0));
  const lastPayment = ledger?.entries.find((e) => e.kind === 'payment');
  const state = balance > 0 ? 'due' : balance < 0 ? 'over' : 'settled';
  const stateText = { due: 'tartozás', over: 'túlfizetés', settled: 'rendezett' }[state];
  const stat = (label, value, cls = '') => `<div class="bb-stat${cls ? ` ${cls}` : ''}"><span>${escapeHtml(label)}</span><b>${value}</b></div>`;
  band.className = `balance-band is-${state}`;
  band.innerHTML = `
    <div class="bb-main">
      <span class="bb-label">Egyenleg</span>
      <span class="bb-value">${formatCurrency(Math.abs(balance))}</span>
      <span class="bb-state">${stateText}</span>
    </div>
    <div class="bb-stats">
      ${stat('Kiállítva', formatCurrency(ledger ? ledger.chargedTotal : 0))}
      ${ledger && Number(ledger.unbilledTotal) > 0 ? stat('Rezsi, bizonylatra vár', formatCurrency(ledger.unbilledTotal)) : ''}
      ${stat('Befizetve', formatCurrency(ledger ? ledger.paidTotal : 0))}
      ${stat('Lejárt hátralék', overdueSum > 0 ? `${formatCurrency(overdueSum)} <small>${overdue.length} lejárt bizonylatból</small>` : '–', overdueSum > 0 ? 'is-alert' : '')}
      ${stat('Utolsó befizetés', lastPayment ? `${formatDate(lastPayment.date)} <small>${formatCurrency(-lastPayment.amount)}</small>` : '–')}
    </div>
    <div class="bb-actions">
      <button class="btn btn-secondary btn-sm" type="button" data-bb="ledger">Folyószámla</button>
      <button class="btn btn-primary btn-sm" type="button" data-bb="pay">${DETAIL_ICON('plus')}Befizetés rögzítése</button>
    </div>`;
  band.hidden = false;
  band.querySelector('[data-bb="pay"]').addEventListener('click', openPayment);
  band.querySelector('[data-bb="ledger"]').addEventListener('click', () => {
    document.querySelector('.record-tab[data-tab="balance"]').click();
    document.querySelector('.record-tabs').scrollIntoView({ behavior: 'smooth', block: 'start' });
  });
}

function renderSummary() {
  renderBalanceBand();
  const year = new Date().getFullYear();
  const issued = invoices.filter((i) => i.status !== 'Failed' && (!i.cancelledAt && !i.cancelsInvoiceId));
  const total = issued.reduce((s, i) => s + Number(i.amountTotal), 0);
  const thisYear = issued.filter((i) => i.periodYear === year).reduce((s, i) => s + Number(i.amountTotal), 0);
  const last = issued[0];
  const failed = invoices.length - issued.length;
  document.getElementById('summary').innerHTML = [
    detailRow('Bérlet kezdete', tenant.moveInDate ? formatDate(tenant.moveInDate) : '<span class="dim">–</span>'),
    detailRow('Kiállítva összesen', formatCurrency(total)),
    detailRow(`Kiállítva ${year}-ben`, formatCurrency(thisYear)),
    detailRow('Bizonylatok', `${issued.length} db${failed ? ` <span class="status-tag missing">${failed} sikertelen</span>` : ''}`),
    detailRow('Utolsó bizonylat', last ? `${escapeHtml(last.number)}, ${formatCurrency(last.amountTotal)}` : '<span class="dim">–</span>'),
  ].join('');
}

function renderPanels() {
  const lease = FIELDS.lease();
  document.getElementById('props-basic').innerHTML = FIELDS.basic.map(editor.renderField).join('');
  document.getElementById('props-lease').innerHTML = lease.map(editor.renderField).join('');
  document.getElementById('props-notes').innerHTML = FIELDS.notes.map(editor.renderField).join('');
  editor.bind([...FIELDS.basic, ...lease, ...FIELDS.notes]);
}

// Pozitív egyenleg = a bérlő tartozik; negatív = túlfizetés.
function balanceHtml(balance) {
  const b = Number(balance) || 0;
  if (b > 0) return `<b class="bal-due">${formatCurrency(b)}</b> <span class="dim">tartozás</span>`;
  if (b < 0) return `<b class="bal-over">${formatCurrency(-b)}</b> <span class="dim">túlfizetés</span>`;
  return `${formatCurrency(0)} <span class="dim">rendezett</span>`;
}

function renderLedger() {
  const body = document.getElementById('balance-body');
  if (!ledger) { body.innerHTML = '<div class="panel-empty">Nem sikerült betölteni az egyenleget.</div>'; return; }
  document.getElementById('count-balance').textContent = ledger.entries.length || '';
  if (!ledger.entries.length) {
    body.innerHTML = '<div class="panel-empty">Még nincs kiállított bizonylat vagy rögzített befizetés.</div>';
    return;
  }
  body.innerHTML = `<div class="ledger-totals">
      <div><span>Kiállítva</span><b>${formatCurrency(ledger.chargedTotal)}</b></div>
      ${Number(ledger.unbilledTotal) > 0 ? `<div><span>Rezsi, még nincs bizonylaton</span><b>${formatCurrency(ledger.unbilledTotal)}</b></div>` : ''}
      <div><span>Befizetve</span><b>${formatCurrency(ledger.paidTotal)}</b></div>
      <div><span>Egyenleg</span><b>${balanceHtml(ledger.balance)}</b></div>
    </div>
    <table>
    <thead><tr><th>Dátum</th><th>Tétel</th><th class="num">Terhelés</th><th class="num">Befizetés</th><th class="num">Egyenleg</th><th></th></tr></thead>
    <tbody>${ledger.entries.map((e) => `
      <tr${e.isCancelled ? ' class="is-archived"' : ''}>
        <td>${formatDate(e.date)}</td>
        <td>${e.kind === 'utility'
          ? `${escapeHtml(e.label)} <span class="status-tag warn" title="A következő bizonylatra kerül">még nincs bizonylaton</span>`
          : e.kind === 'invoice'
          ? `<a class="mono link" href="invoice-detail.html?id=${encodeURIComponent(e.invoiceId)}&from=${encodeURIComponent(`tenant:${tenantId}`)}">${escapeHtml(e.invoiceNumber)}</a> <span class="dim">${escapeHtml(e.label)}</span>${e.isCancelled ? ' <span class="status-tag cancelled">Sztornózott</span>' : ''}`
          : `${escapeHtml(e.label)}${e.invoiceId ? ` <a class="mono link" href="invoice-detail.html?id=${encodeURIComponent(e.invoiceId)}&from=${encodeURIComponent(`tenant:${tenantId}`)}">${escapeHtml(e.invoiceNumber || '')}</a>` : ''}${e.note ? ` <span class="dim">${escapeHtml(e.note)}</span>` : ''}`}</td>
        <td class="num">${e.kind !== 'payment' ? formatCurrency(e.amount) : ''}</td>
        <td class="num">${e.kind === 'payment' ? formatCurrency(-e.amount) : ''}</td>
        <td class="num">${formatCurrency(e.balance)}</td>
        <td class="num">${e.kind === 'payment' ? `<button class="icon-btn" type="button" title="${e.invoiceId ? 'Befizetés törlése (a bizonylat fizetett jelölése is megszűnik)' : 'Befizetés törlése'}" aria-label="Befizetés törlése" data-del-payment="${escapeHtml(e.id)}">${DETAIL_ICON('trash')}</button>` : ''}</td>
      </tr>`).join('')}</tbody></table>`;
  body.querySelectorAll('[data-del-payment]').forEach((b) => b.addEventListener('click', () => deletePayment(b.dataset.delPayment)));
}

async function loadLedger() {
  const res = await apiFetch(`/api/tenants/${tenantId}/ledger`);
  ledger = res.ok ? await readJson(res) : null;
  renderLedger();
  renderSummary();
}

async function deletePayment(id) {
  if (!(await uiConfirm('Biztosan törlöd ezt a befizetést?', { title: 'Befizetés törlése', confirmText: 'Törlés', danger: true }))) return;
  const res = await apiFetch(`/api/tenants/${tenantId}/payments/${id}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 204) { await uiAlert(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'), { title: 'Törlés sikertelen' }); return; }
  // A bizonylathoz kötött befizetés törlése a bizonylat fizetett jelölését is megszünteti → a bizonylatok is frissülnek.
  document.dispatchEvent(new CustomEvent('invoices-changed'));
}

// Befizetés rögzítése: dátum (alapból ma), összeg (alapból a tartozás), megjegyzés.
const payModal = document.getElementById('pay-overlay');
function openPayment() {
  const today = new Date();
  document.getElementById('pay-date').value = `${today.getFullYear()}. ${String(today.getMonth() + 1).padStart(2, '0')}. ${String(today.getDate()).padStart(2, '0')}.`;
  const due = ledger && ledger.balance > 0 ? ledger.balance : '';
  document.getElementById('pay-amount').value = due;
  document.getElementById('pay-note').value = '';
  document.getElementById('pay-error').classList.remove('show');
  payModal.classList.add('open');
  document.getElementById('pay-amount').focus();
}
function closePayment() { payModal.classList.remove('open'); }
document.getElementById('add-payment').addEventListener('click', openPayment);
document.getElementById('pay-close').addEventListener('click', closePayment);
document.getElementById('pay-cancel').addEventListener('click', closePayment);
payModal.addEventListener('click', (e) => { if (e.target === payModal) closePayment(); });
document.getElementById('pay-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const err = document.getElementById('pay-error');
  const fail = (msg) => { err.textContent = msg; err.classList.add('show'); };
  const m = /^(\d{4})[.\-/ ]\s*(\d{1,2})[.\-/ ]\s*(\d{1,2})\.?$/.exec(document.getElementById('pay-date').value.trim());
  if (!m) { fail('Dátum: ÉÉÉÉ. HH. NN.'); return; }
  const date = `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}`;
  const amount = Number(String(document.getElementById('pay-amount').value).replace(/[\s ]/g, '').replace(',', '.'));
  if (!Number.isFinite(amount) || amount <= 0) { fail('Adj meg nullánál nagyobb összeget.'); return; }
  const btn = document.getElementById('pay-save');
  btn.disabled = true;
  try {
    const res = await apiFetch(`/api/tenants/${tenantId}/payments`, {
      method: 'POST', body: JSON.stringify({ date, amount, note: document.getElementById('pay-note').value.trim() || null }),
    });
    if (!res.ok) { fail(errorMessage(res, await readJson(res), 'Nem sikerült rögzíteni a befizetést.')); return; }
    closePayment();
    await loadLedger();
  } finally {
    btn.disabled = false;
  }
});

function renderInvoices() {
  document.getElementById('count-invoices').textContent = invoices.length || '';
  document.getElementById('invoices-body').innerHTML = renderInvoiceTable(invoices, 'Ennek a bérlőnek még nincs bizonylata.', { showTenant: false, from: `tenant:${tenantId}` });
}

document.getElementById('delete-btn').addEventListener('click', async () => {
  if (!(await uiConfirm(`Biztosan törlöd a bérlőt: ${tenant.name}?`, { title: 'Törlés', confirmText: 'Törlés', danger: true }))) return;
  const res = await apiFetch(`/api/tenants/${tenantId}`, { method: 'DELETE' });
  if (res.ok) { ChangeGuard.allowLeave(); location.href = 'tenants.html'; return; }
  showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'));
});

(async () => {
  const [tRes, pRes, iRes, lRes, cRes] = await Promise.all([
    apiFetch(`/api/tenants/${tenantId}`), apiFetch('/api/properties'), apiFetch(`/api/tenants/${tenantId}/invoices`),
    apiFetch(`/api/tenants/${tenantId}/ledger`), apiFetch('/api/company'),
  ]);
  if (tRes.status === 404) { showError('A bérlő nem található.'); document.getElementById('p-name').textContent = 'Nem található'; return; }
  tenant = await readJson(tRes);
  properties = pRes.ok ? await readJson(pRes) : [];
  invoices = iRes.ok ? await readJson(iRes) : [];
  ledger = lRes.ok ? await readJson(lRes) : null;
  company = cRes.ok ? await readJson(cRes) : null;
  renderHeader();
  renderPanels();
  renderProperty();
  renderSummary();
  renderInvoices();
  renderLedger();
  setupTabs('tenantDetailTab', 'invoices');
  mountDocuments({
    scope: { tenantId },
    container: '#documents-body',
    count: '#count-documents',
    uploadButton: '#upload-doc',
    links: async (doc) => {
      const options = properties.map((p) => ({ value: p.id, label: p.name }));
      if (doc?.propertyId && !options.some((o) => o.value === doc.propertyId)) options.push({ value: doc.propertyId, label: doc.propertyName });
      return [{ key: 'propertyId', label: 'Ingatlanhoz is tartozik', options, value: tenant.propertyId || '', hint: 'Így az ingatlan adatlapján is megjelenik.' }];
    },
  });
  layoutMasonry();
})();

// Újrapróbálás / törlés után a bizonylatok és az összegzés frissül.
document.addEventListener('invoices-changed', async () => {
  const res = await apiFetch(`/api/tenants/${tenantId}/invoices`);
  if (!res.ok) return;
  invoices = await readJson(res);
  renderSummary();
  renderInvoices();
  await loadLedger();
});
