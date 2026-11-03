// A bizonylat adatlapja: adatok, tételek, kapcsolódó bizonylatok (ugyanannak az ingatlannak ugyanarra az időszakra:
// az eredeti, a sztornója, az új bizonylat), és a PDF előnézete (alapból 60%). Műveletek: PDF letöltése, újrapróbálás
// (sikertelennél), sztornózás (aktív, kiállított bizonylatnál). A ?from= paraméter: bundles | property:<id> |
// tenant:<id> | owner:<id> — a morzsamenühöz.
const invoiceId = new URLSearchParams(location.search).get('id');
if (!invoiceId) location.replace('invoice-bundles.html');
const fromParam = new URLSearchParams(location.search).get('from') || '';

const STATUS_LABELS = { Sent: 'Kiküldve', Generated: 'Nem küldött', Failed: 'Sikertelen' };
let invoice = null;
let detail = null;

const $ = (id) => document.getElementById(id);
const preview = Docs.createPreview($('pv-stage'), { onInfo: renderPreviewHead });

function showError(message) {
  const el = $('load-error');
  el.textContent = message;
  el.classList.add('show');
}

const periodText = (i) => `${i.periodYear}. ${MONTH_NAMES[i.periodMonth - 1].toLowerCase()}`;

function crumbs() {
  const [kind, id] = fromParam.split(':');
  const back = {
    property: ['Ingatlanok', 'properties.html', `property-detail.html?id=${encodeURIComponent(id)}#invoices`, invoice.propertyName],
    tenant: ['Bérlők', 'tenants.html', `tenant-detail.html?id=${encodeURIComponent(id)}#invoices`, invoice.tenantName],
    owner: ['Tulajdonosok', 'owners.html', `owner-detail.html?id=${encodeURIComponent(id)}#invoices`, null],
  }[kind];
  if (back && id) {
    return `<li><a href="${back[1]}">${back[0]}</a></li>${back[3] ? `<li><a href="${back[2]}">${escapeHtml(back[3])}</a></li>` : `<li><a href="${back[2]}">Adatlap</a></li>`}<li><a href="${back[2]}">Bizonylatok</a></li>`;
  }
  return `<li><a href="invoice-bundles.html">Bizonylatok</a></li><li>${invoice.periodYear}. ${MONTH_NAMES[invoice.periodMonth - 1]}</li>`;
}

function render() {
  document.title = `${invoice.number} | Lakáskezelő`;
  $('crumbs').innerHTML = crumbs();
  $('i-number').textContent = invoice.cancelsInvoiceId ? `${invoice.number} (sztornó)` : invoice.number;
  $('i-status').innerHTML = invoiceStatusTag(invoice, STATUS_LABELS);
  $('i-sub').innerHTML = [
    `<a class="link" href="property-detail.html?id=${encodeURIComponent(invoice.propertyId)}">${escapeHtml(invoice.propertyName)}</a>`,
    invoice.tenantName ? escapeHtml(invoice.tenantName) : null,
    periodText(invoice),
  ].filter(Boolean).join('<span class="sep"></span>');

  const active = invoiceIsActive(invoice);
  $('download-btn').hidden = invoice.status === 'Failed';
  $('retry-btn').hidden = !(active && invoice.status === 'Failed');
  $('cancel-btn').hidden = !(active && invoice.status !== 'Failed');

  const original = detail.related.find((r) => r.id === invoice.cancelsInvoiceId);
  const storno = detail.related.find((r) => r.cancelsInvoiceId === invoice.id);
  const link = (r) => `<a class="link mono" href="${invoiceDetailUrl(r, fromParam)}">${escapeHtml(r.number)}</a>`;
  const rows = [
    detailRow('Sorszám', `<span class="mono">${escapeHtml(invoice.number)}</span>`),
    detailRow('Bizonylat', invoice.cancelsInvoiceId ? `Sztornó bizonylat${original ? `<small class="prop-sub">a(z) ${link(original)} sztornója</small>` : ''}` : 'Számviteli bizonylat'),
    detailRow('Állapot', invoiceStatusTag(invoice, STATUS_LABELS)),
    detailRow('Ingatlan', `<a class="link" href="property-detail.html?id=${encodeURIComponent(invoice.propertyId)}">${escapeHtml(invoice.propertyName)}</a>`),
    detailRow('Bérlő', escapeHtml(invoice.tenantName || '–')),
    detailRow('Időszak', periodText(invoice)),
    detailRow('Kiállítva', formatDate(invoice.issuedAt)),
    detailRow('Fizetési határidő', formatDate(invoice.dueDate)),
    detailRow('Kiküldve', invoice.sentAt ? Docs.formatDateTime(invoice.sentAt) : '<span class="dim">–</span>'),
    detailRow('Összeg', `<b>${formatCurrency(invoice.amountTotal)}</b>`),
  ];
  if (invoice.cancelledAt) {
    rows.push(detailRow('Sztornózva', `${Docs.formatDateTime(invoice.cancelledAt)}${detail.cancelledByName ? `<small class="prop-sub">${escapeHtml(detail.cancelledByName)}</small>` : ''}${storno ? `<small class="prop-sub">sztornó: ${link(storno)}</small>` : ''}`));
  }
  if (invoice.errorMessage) rows.push(detailRow('Hiba', `<span class="inv-error">${escapeHtml(invoice.errorMessage)}</span>`));
  if (detail.rentConversionNote) rows.push(detailRow('Árfolyam', `<span class="inv-note">${escapeHtml(detail.rentConversionNote)}</span>`));
  $('props').innerHTML = rows.join('');

  $('lines').innerHTML = invoice.lines.length ? `<table>
    <thead><tr><th>Tétel</th><th class="num">Összeg</th></tr></thead>
    <tbody>${invoice.lines.map((l) => `<tr><td>${escapeHtml(l.label)}</td><td class="num">${formatCurrency(l.amount)}</td></tr>`).join('')}
      <tr class="inv-total"><td>Összesen</td><td class="num">${formatCurrency(invoice.amountTotal)}</td></tr></tbody></table>`
    : '<div class="panel-empty">Nincsenek tételek.</div>';

  $('related-panel').hidden = !detail.related.length;
  $('count-related').textContent = detail.related.length || '';
  $('related').innerHTML = detail.related.map((r) => `
    <a class="inv-related-row" href="${invoiceDetailUrl(r, fromParam)}">
      <span class="mono">${escapeHtml(r.number)}</span>
      <span class="inv-related-meta">${formatDate(r.issuedAt)} · ${formatCurrency(r.amountTotal)}</span>
      ${invoiceStatusTag(r, STATUS_LABELS)}
    </a>`).join('');
}

function renderPreviewHead(info) {
  const bits = [escapeHtml(`${invoice?.number || ''}.pdf`)];
  if (info.pages) bits.push(`${info.pages} oldal`);
  $('pv-title').innerHTML = bits.join('<span class="sep"></span>');
  $('pv-zoom').hidden = !info.zoomable;
  $('zoom-val').textContent = `${Math.round(info.zoom * 100)}%`;
}
$('zoom-in').addEventListener('click', () => preview.zoomIn());
$('zoom-out').addEventListener('click', () => preview.zoomOut());
$('zoom-val').addEventListener('click', () => preview.reset());

function showPdf() {
  if (invoice.status === 'Failed') {
    $('pv-title').textContent = 'Előnézet';
    $('pv-stage').innerHTML = `<div class="dv-message">${Docs.ICO('triangle-alert')}<h3>A bizonylat nem készült el</h3><p></p></div>`;
    $('pv-stage').querySelector('p').textContent = invoice.errorMessage || 'Próbáld újra az Újrapróbálás gombbal.';
    return;
  }
  preview.showFile(
    { name: invoice.number, fileName: `${invoice.number}.pdf`, contentType: 'application/pdf' },
    async () => {
      const res = await fetch(`${API_BASE}/api/invoices/${invoiceId}/pdf`, { headers: authHeaders() });
      if (res.status === 401) { clearAuth(); location.href = 'login.html'; throw new Error('Nincs érvényes munkamenet.'); }
      if (!res.ok) throw new Error((await readJson(res)).message || 'Nem sikerült betölteni a PDF-et.');
      return res.blob();
    },
    () => downloadFile(`/api/invoices/${invoiceId}/pdf`, `${invoice.number}.pdf`));
}

$('download-btn').addEventListener('click', () => downloadFile(`/api/invoices/${invoiceId}/pdf`, `${invoice.number}.pdf`));
$('retry-btn').addEventListener('click', () => invoiceRetry(invoice.propertyId, invoice.periodYear, invoice.periodMonth, invoice.propertyName));
$('cancel-btn').addEventListener('click', () => invoiceCancel(invoice.id, invoice.number, invoice.status === 'Sent'));
document.addEventListener('invoices-changed', () => load());

async function load() {
  const res = await apiFetch(`/api/invoices/${invoiceId}`);
  if (res.status === 404) {
    $('i-number').textContent = 'Nem található';
    showError('A bizonylat nem található.');
    return;
  }
  if (!res.ok) { showError(errorMessage(res, await readJson(res), 'Nem sikerült betölteni a bizonylatot.')); return; }
  detail = await res.json();
  const changed = !invoice || invoice.status !== detail.invoice.status;
  invoice = detail.invoice;
  render();
  if (changed) showPdf();
}

load();
