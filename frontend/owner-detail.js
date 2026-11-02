// Tulajdonos adatlap (owner-detail.html?id=...) — az ingatlan-adatlap mintájára: helyben szerkeszthető adatok
// panelekben, alatta a tulajdonos ingatlanai (hányaddal) és az ingatlanok számlái.
requireAuth();

const ownerId = new URLSearchParams(location.search).get('id');
if (!ownerId) location.replace('owners.html');

let owner = null;
let ownedProperties = [];
let invoices = [];

const editor = createInlineEditor({ getRecord: () => owner, save });

const FIELDS = {
  basic: [
    { path: 'name', label: 'Név', required: true },
    { path: 'email', label: 'E-mail', type: 'email' },
    { path: 'phone', label: 'Telefon' },
    { path: 'address', label: 'Cím' },
  ],
  finance: [
    { path: 'taxId', label: 'Adószám / adóazonosító' },
    { path: 'bankAccount', label: 'Bankszámlaszám', placeholder: 'ide utalják a bérleti díjat' },
  ],
  notes: [
    { path: 'notes', label: 'Megjegyzés', type: 'textarea', wide: true, placeholder: 'Bármi, amit a tulajdonosról tudni kell' },
  ],
};

async function save(draft) {
  const payload = {
    name: draft.name, email: draft.email, phone: draft.phone, taxId: draft.taxId,
    bankAccount: draft.bankAccount, address: draft.address, notes: draft.notes,
  };
  const res = await apiFetch(`/api/owners/${ownerId}`, { method: 'PUT', body: JSON.stringify(payload) });
  const data = await readJson(res);
  if (!res.ok) {
    showError(errorMessage(res, data, 'Nem sikerült menteni.'));
    return errorMessage(res, data, 'Nem sikerült menteni.');
  }
  owner = data;
  hideError();
  renderHeader();
  return null;
}

function showError(message) { const el = document.getElementById('load-error'); el.textContent = message; el.classList.add('show'); }
function hideError() { document.getElementById('load-error').classList.remove('show'); }

function renderHeader() {
  document.title = `Lakáskezelő — ${owner.name}`;
  document.getElementById('p-name').textContent = owner.name;
  document.getElementById('crumb-name').textContent = owner.name;
  const active = ownedProperties.filter((p) => p.isActive).length;
  document.getElementById('p-status').innerHTML = ownedProperties.length
    ? `<span class="status-tag ok">${ownedProperties.length} ingatlan</span>` : '<span class="status-tag inactive">Nincs ingatlana</span>';
  document.getElementById('p-sub').textContent = [owner.email, owner.phone, active !== ownedProperties.length ? `${active} aktív ingatlan` : null]
    .filter(Boolean).join(' · ') || 'Add meg az elérhetőségeket és a bankszámlaszámot.';
}

// A tulajdoni hányad "1/2" alakú tört; üresen egyenlő arány az ingatlan tulajdonosai között (itt nem számoljuk).
function shareValue(share) {
  const m = /^(\d+)\/(\d+)$/.exec(share || '');
  return m ? Number(m[1]) / Number(m[2]) : null;
}

function renderSummary() {
  const year = new Date().getFullYear();
  const issued = invoices.filter((i) => i.status !== 'Failed');
  const thisYear = issued.filter((i) => i.periodYear === year).reduce((s, i) => s + Number(i.amountTotal), 0);
  const active = ownedProperties.filter((p) => p.isActive);
  const rented = active.filter((p) => p.tenantName).length;
  document.getElementById('summary').innerHTML = [
    detailRow('Ingatlanok', `${ownedProperties.length} db${active.length !== ownedProperties.length ? ` (${active.length} aktív)` : ''}`),
    detailRow('Kiadva', active.length ? `${rented} / ${active.length}` : '<span class="dim">—</span>'),
    detailRow(`Számlázva ${year}-ben`, `${formatCurrency(thisYear)} <span class="dim">(az ingatlanok teljes összege)</span>`),
    detailRow('Számlák', `${issued.length} db`),
  ].join('');
}

function renderPanels() {
  document.getElementById('props-basic').innerHTML = FIELDS.basic.map(editor.renderField).join('');
  document.getElementById('props-finance').innerHTML = FIELDS.finance.map(editor.renderField).join('');
  document.getElementById('props-notes').innerHTML = FIELDS.notes.map(editor.renderField).join('');
  editor.bind([...FIELDS.basic, ...FIELDS.finance, ...FIELDS.notes]);
}

function renderProperties() {
  document.getElementById('count-properties').textContent = ownedProperties.length || '';
  const body = document.getElementById('properties-body');
  if (!ownedProperties.length) {
    body.innerHTML = '<div class="panel-empty">Ehhez a tulajdonoshoz még nincs ingatlan rendelve — az ingatlan adatlapján, a Tulajdonosok panelen adhatod hozzá.</div>';
    return;
  }
  body.innerHTML = `<table>
    <thead><tr><th>Ingatlan</th><th>Település</th><th>Tulajdoni hányad</th><th>Jelenlegi bérlő</th><th class="num">Bérleti díj</th><th>Állapot</th></tr></thead>
    <tbody>${ownedProperties.map((p) => {
      const share = shareValue(p.share);
      return `<tr class="clickable" onclick="location.href='property-detail.html?id=${encodeURIComponent(p.propertyId)}'">
        <td class="cell-main">${escapeHtml(p.name)}</td>
        <td>${escapeHtml(p.city || '—')}</td>
        <td>${p.share ? `${escapeHtml(p.share)}${share != null ? ` <span class="dim">(${Math.round(share * 100)}%)</span>` : ''}` : '<span class="dim">egyenlő arány</span>'}</td>
        <td>${escapeHtml(p.tenantName || '—')}</td>
        <td class="num">${formatNumber(p.rentAmount, p.rentAmount % 1 ? 2 : 0)}</td>
        <td>${p.isActive ? '<span class="status-tag ok">Aktív</span>' : '<span class="status-tag inactive">Inaktív</span>'}</td>
      </tr>`;
    }).join('')}</tbody></table>`;
}

function renderInvoices() {
  document.getElementById('count-invoices').textContent = invoices.length || '';
  document.getElementById('invoices-body').innerHTML = renderInvoiceTable(invoices, 'A tulajdonos ingatlanaihoz még nincs számla.');
}

document.getElementById('delete-btn').addEventListener('click', async () => {
  if (!confirm(`Biztosan törlöd a tulajdonost: ${owner.name}?`)) return;
  const res = await apiFetch(`/api/owners/${ownerId}`, { method: 'DELETE' });
  if (res.ok) { location.href = 'owners.html'; return; }
  showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'));
});

(async () => {
  const [oRes, pRes, iRes] = await Promise.all([
    apiFetch(`/api/owners/${ownerId}`), apiFetch(`/api/owners/${ownerId}/properties`), apiFetch(`/api/owners/${ownerId}/invoices`),
  ]);
  if (oRes.status === 404) { showError('A tulajdonos nem található.'); document.getElementById('p-name').textContent = 'Nem található'; return; }
  owner = await readJson(oRes);
  ownedProperties = pRes.ok ? await readJson(pRes) : [];
  invoices = iRes.ok ? await readJson(iRes) : [];
  renderHeader();
  renderPanels();
  renderSummary();
  renderProperties();
  renderInvoices();
  setupTabs('ownerDetailTab', 'properties');
  layoutMasonry();
})();
