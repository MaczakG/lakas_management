// Bérlő adatlap (tenant-detail.html?id=...) — az ingatlan-adatlap mintájára: helyben szerkeszthető adatok
// panelekben (minden módosítás a teljes bérlő PUT-jával mentődik), alatta a bérlő számlái.
requireAuth();

const tenantId = new URLSearchParams(location.search).get('id');
if (!tenantId) location.replace('tenants.html');

let tenant = null;
let properties = [];
let invoices = [];

const editor = createInlineEditor({ getRecord: () => tenant, save });

const FIELDS = {
  basic: [
    { path: 'name', label: 'Név', required: true },
    { path: 'email', label: 'E-mail', type: 'email', placeholder: 'ide megy a számla' },
    { path: 'phone', label: 'Telefon' },
    { path: 'taxId', label: 'Adószám' },
    { path: 'address', label: 'Számlázási cím', placeholder: 'ha eltér az ingatlan címétől' },
  ],
  lease: () => [
    { path: 'propertyId', label: 'Ingatlan', type: 'select', options: properties.map((p) => ({ value: p.id, label: p.name })) },
    { path: 'moveInDate', label: 'Beköltözés', type: 'date', placeholder: 'ÉÉÉÉ. HH. NN.' },
    { path: 'moveOutDate', label: 'Kiköltözés', type: 'date', placeholder: 'ÉÉÉÉ. HH. NN.' },
    { path: 'rentCurrency', label: 'Bérleti díj devizája', type: 'select', required: true, options: [{ value: 'HUF', label: 'Forint (Ft)' }, { value: 'EUR', label: 'Euró (EUR), MNB-árfolyamon számlázva' }] },
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
    || 'Add meg az elérhetőségeket, a számla az e-mail címre megy.';
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
    detailRow('Számlázási nap', `minden hónap ${prop.billingDayOfMonth}. napja`),
    detailRow('Állapot', prop.isActive ? '<span class="status-tag ok">Aktív</span>' : '<span class="status-tag inactive">Inaktív</span>'),
  ].join('');
}

function renderSummary() {
  const year = new Date().getFullYear();
  const issued = invoices.filter((i) => i.status !== 'Failed' && !i.archivedAt);
  const total = issued.reduce((s, i) => s + Number(i.amountTotal), 0);
  const thisYear = issued.filter((i) => i.periodYear === year).reduce((s, i) => s + Number(i.amountTotal), 0);
  const last = issued[0];
  const failed = invoices.length - issued.length;
  document.getElementById('summary').innerHTML = [
    detailRow('Bérlet kezdete', tenant.moveInDate ? formatDate(tenant.moveInDate) : '<span class="dim">–</span>'),
    detailRow('Kiszámlázva összesen', formatCurrency(total)),
    detailRow(`Kiszámlázva ${year}-ben`, formatCurrency(thisYear)),
    detailRow('Számlák', `${issued.length} db${failed ? ` <span class="status-tag missing">${failed} sikertelen</span>` : ''}`),
    detailRow('Utolsó számla', last ? `${escapeHtml(last.number)}, ${formatCurrency(last.amountTotal)}` : '<span class="dim">–</span>'),
  ].join('');
}

function renderPanels() {
  const lease = FIELDS.lease();
  document.getElementById('props-basic').innerHTML = FIELDS.basic.map(editor.renderField).join('');
  document.getElementById('props-lease').innerHTML = lease.map(editor.renderField).join('');
  document.getElementById('props-notes').innerHTML = FIELDS.notes.map(editor.renderField).join('');
  editor.bind([...FIELDS.basic, ...lease, ...FIELDS.notes]);
}

function renderInvoices() {
  document.getElementById('count-invoices').textContent = invoices.length || '';
  document.getElementById('invoices-body').innerHTML = renderInvoiceTable(invoices, 'Ennek a bérlőnek még nincs számlája.', { showTenant: false });
}

document.getElementById('delete-btn').addEventListener('click', async () => {
  if (!(await uiConfirm(`Biztosan törlöd a bérlőt: ${tenant.name}?`, { title: 'Törlés', confirmText: 'Törlés', danger: true }))) return;
  const res = await apiFetch(`/api/tenants/${tenantId}`, { method: 'DELETE' });
  if (res.ok) { location.href = 'tenants.html'; return; }
  showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'));
});

(async () => {
  const [tRes, pRes, iRes] = await Promise.all([
    apiFetch(`/api/tenants/${tenantId}`), apiFetch('/api/properties'), apiFetch(`/api/tenants/${tenantId}/invoices`),
  ]);
  if (tRes.status === 404) { showError('A bérlő nem található.'); document.getElementById('p-name').textContent = 'Nem található'; return; }
  tenant = await readJson(tRes);
  properties = pRes.ok ? await readJson(pRes) : [];
  invoices = iRes.ok ? await readJson(iRes) : [];
  renderHeader();
  renderPanels();
  renderProperty();
  renderSummary();
  renderInvoices();
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

// Újrapróbálás / törlés után a számlák és az összegzés frissül.
document.addEventListener('invoices-changed', async () => {
  const res = await apiFetch(`/api/tenants/${tenantId}/invoices`);
  if (!res.ok) return;
  invoices = await readJson(res);
  renderSummary();
  renderInvoices();
});
