// A dokumentum adatlapja: adatok (helyben szerkeszthetők), verziók, napló és az előnézet (alapból 60%).
// A ?from=property:<id> | tenant:<id> | owner:<id> paraméter mondja meg, honnan nyitották meg (morzsamenü,
// vissza-lépés törlés után).
const docId = new URLSearchParams(location.search).get('id');
if (!docId) location.replace('properties.html');
const fromParam = new URLSearchParams(location.search).get('from') || '';

const LOG_PREVIEW = 6;
const EVENT_TEXT = {
  Uploaded: 'Feltöltötte', NewVersion: 'Új verziót töltött fel', Viewed: 'Megtekintette', Downloaded: 'Letöltötte', Updated: 'Módosította az adatait',
};
const CONTEXTS = {
  property: { list: 'properties.html', listLabel: 'Ingatlanok', page: 'property-detail.html', label: 'Ingatlan' },
  tenant: { list: 'tenants.html', listLabel: 'Bérlők', page: 'tenant-detail.html', label: 'Bérlő' },
  owner: { list: 'owners.html', listLabel: 'Tulajdonosok', page: 'owner-detail.html', label: 'Tulajdonos' },
};

let doc = null;
let versions = [];
let events = [];
let types = [];
let choices = { properties: [], tenants: [], owners: [] };
let selectedVersion = null; // null = a jelenlegi
let showAllLog = false;

const $ = (id) => document.getElementById(id);
const preview = Docs.createPreview($('pv-stage'), { onInfo: renderPreviewHead });
let lastInfo = { pages: 0, zoomable: false, zoom: Docs.DEFAULT_ZOOM };

function showError(message) {
  const el = $('load-error');
  el.textContent = message;
  el.classList.add('show');
}

// ---------- kontextus (honnan jött) ----------

function context() {
  const [kind, id] = fromParam.split(':');
  const nameOf = { property: doc.propertyName, tenant: doc.tenantName, owner: doc.ownerName };
  const idOf = { property: doc.propertyId, tenant: doc.tenantId, owner: doc.ownerId };
  if (CONTEXTS[kind] && id && idOf[kind] === id) return { kind, id, name: nameOf[kind] };
  const fallback = ['property', 'tenant', 'owner'].find((k) => idOf[k]);
  return fallback ? { kind: fallback, id: idOf[fallback], name: nameOf[fallback] } : null;
}

const contextUrl = (ctx) => (ctx ? `${CONTEXTS[ctx.kind].page}?id=${encodeURIComponent(ctx.id)}#documents` : 'properties.html');

// ---------- megjelenítés ----------

function renderHeader() {
  const ctx = context();
  document.title = `${doc.name} | Lakáskezelő`;
  $('d-name').textContent = doc.name;
  $('crumbs').innerHTML = ctx
    ? `<li><a href="${CONTEXTS[ctx.kind].list}">${CONTEXTS[ctx.kind].listLabel}</a></li>
       <li><a href="${CONTEXTS[ctx.kind].page}?id=${encodeURIComponent(ctx.id)}">${escapeHtml(ctx.name)}</a></li>
       <li><a href="${contextUrl(ctx)}">Dokumentumok</a></li>`
    : '<li>Dokumentumok</li>';
  const parts = [];
  if (doc.propertyId) parts.push(`Ingatlan: <a class="link" href="property-detail.html?id=${encodeURIComponent(doc.propertyId)}#documents">${escapeHtml(doc.propertyName)}</a>`);
  if (doc.tenantId) parts.push(`Bérlő: <a class="link" href="tenant-detail.html?id=${encodeURIComponent(doc.tenantId)}#documents">${escapeHtml(doc.tenantName)}</a>`);
  if (doc.ownerId) parts.push(`Tulajdonos: <a class="link" href="owner-detail.html?id=${encodeURIComponent(doc.ownerId)}#documents">${escapeHtml(doc.ownerName)}</a>`);
  $('d-sub').innerHTML = parts.join('<span class="sep"></span>');
}

const FIELDS = () => [
  { path: 'name', label: 'Név', required: true },
  { path: 'documentTypeId', label: 'Dokumentumtípus', type: 'select',
    options: [...types.map((t) => ({ value: t.id, label: t.name })), { value: Docs.MANAGE, label: 'Típusok szerkesztése…' }] },
  { path: 'propertyId', label: 'Ingatlan', type: 'select', options: withCurrent(choices.properties, doc.propertyId, doc.propertyName) },
  { path: 'tenantId', label: 'Bérlő', type: 'select', options: withCurrent(choices.tenants, doc.tenantId, doc.tenantName) },
  { path: 'ownerId', label: 'Tulajdonos', type: 'select', options: withCurrent(choices.owners, doc.ownerId, doc.ownerName) },
  { path: 'note', label: 'Megjegyzés', type: 'textarea', placeholder: 'pl. mit tartalmaz' },
];

function withCurrent(list, id, name) {
  const options = list.map((x) => ({ value: x.id, label: x.name }));
  if (id && !options.some((o) => o.value === id)) options.push({ value: id, label: name || '–' });
  return options;
}

const editor = createInlineEditor({
  getRecord: () => doc,
  save: async (draft) => {
    const res = await apiFetch(`/api/documents/${docId}`, {
      method: 'PUT',
      body: JSON.stringify({
        name: draft.name, documentTypeId: draft.documentTypeId || null, note: draft.note || null,
        propertyId: draft.propertyId || null, tenantId: draft.tenantId || null, ownerId: draft.ownerId || null,
      }),
    });
    const data = await readJson(res);
    if (!res.ok) return errorMessage(res, data, 'Nem sikerült menteni.');
    doc = data;
    renderHeader();
    loadMeta();
    return null;
  },
});

function renderProps() {
  const current = versions.find((v) => v.number === doc.currentVersion);
  const fields = FIELDS();
  $('props').innerHTML = [
    ...fields.slice(0, 2).map(editor.renderField),
    detailRow('Fájl', `<span class="doc-file-name">${escapeHtml(doc.fileName)}</span>`),
    detailRow('Formátum', escapeHtml(Docs.typeLabel(doc))),
    detailRow('Méret', Docs.formatSize(doc.sizeBytes)),
    detailRow('Verzió', `${doc.currentVersion}. <span class="dim">jelenlegi</span>`),
    detailRow('Feltöltve', `${current ? Docs.formatDateTime(current.uploadedAt) : Docs.formatDateTime(doc.uploadedAt)}${current?.uploadedByName ? `<small class="prop-sub">${escapeHtml(current.uploadedByName)}</small>` : ''}`),
    ...fields.slice(2).map(editor.renderField),
  ].join('');
  editor.bind(fields);
}

// A típus-választó „Típusok szerkesztése…” eleme nem ment, hanem megnyitja a típusok ablakát (a mentő
// eseménykezelő előtt, a befoglaló elemen elkapva).
$('props').addEventListener('change', (e) => {
  if (e.target.id !== 'f-documentTypeId' || e.target.value !== Docs.MANAGE) return;
  e.stopImmediatePropagation();
  e.target.value = doc.documentTypeId || '';
  openTypes();
}, true);

async function openTypes() {
  Docs.openTypeManager(async () => {
    types = await Docs.loadTypes(true);
    const res = await apiFetch(`/api/documents/${docId}`);
    if (res.ok) doc = (await res.json()).document;
    renderProps();
  });
}
$('types-btn').addEventListener('click', openTypes);

function renderVersions() {
  $('count-versions').textContent = versions.length || '';
  const shown = selectedVersion ?? doc.currentVersion;
  $('versions').innerHTML = versions.map((v) => `
    <div class="doc-version${v.number === shown ? ' active' : ''}">
      <button type="button" class="doc-version-main" data-version="${v.number}" title="Megtekintés">
        <span class="v-badge">v${v.number}</span>
        <span class="v-text"><b>${escapeHtml(v.fileName)}</b>
          <small>${Docs.formatDateTime(v.uploadedAt)} · ${Docs.formatSize(v.sizeBytes)}${v.uploadedByName ? ` · ${escapeHtml(v.uploadedByName)}` : ''}${v.number === doc.currentVersion ? ' · jelenlegi' : ''}</small></span>
      </button>
      <button class="icon-btn" type="button" data-version-download="${v.number}" title="Letöltés" aria-label="Letöltés">${Docs.ICO('download')}</button>
    </div>`).join('');
}

function renderLog() {
  const list = showAllLog ? events : events.slice(0, LOG_PREVIEW);
  $('log-all-btn').hidden = events.length <= LOG_PREVIEW;
  $('log-all-btn').textContent = showAllLog ? 'Kevesebb' : 'Teljes napló';
  $('log').innerHTML = list.length ? list.map((e) => `
    <div class="doc-log-row">
      <div><b>${escapeHtml(e.userName || 'Ismeretlen')}</b><small>${EVENT_TEXT[e.type] || escapeHtml(e.type)}${e.versionNumber ? ` · ${e.versionNumber}. verzió` : ''}</small></div>
      <time>${Docs.formatDateTime(e.at)}</time>
    </div>`).join('') : '<div class="panel-empty">Még nincs bejegyzés.</div>';
}
$('log-all-btn').addEventListener('click', () => { showAllLog = !showAllLog; renderLog(); });

function renderPreviewHead(info) {
  lastInfo = info;
  const v = versions.find((x) => x.number === (selectedVersion ?? doc?.currentVersion));
  const name = v?.fileName || doc?.fileName || '';
  const bits = [escapeHtml(name)];
  if (info.pages > 1 || (info.pages === 1 && v?.contentType === 'application/pdf')) bits.push(`${info.pages} oldal`);
  if (v && doc && v.number !== doc.currentVersion) bits.push(`<span class="pv-old">${v.number}. verzió (korábbi)</span>`);
  $('pv-title').innerHTML = bits.join('<span class="sep"></span>');
  $('pv-zoom').hidden = !info.zoomable;
  $('zoom-val').textContent = `${Math.round(info.zoom * 100)}%`;
}
$('zoom-in').addEventListener('click', () => preview.zoomIn());
$('zoom-out').addEventListener('click', () => preview.zoomOut());
$('zoom-val').addEventListener('click', () => preview.reset());

async function showVersion(number) {
  selectedVersion = number === doc.currentVersion ? null : number;
  renderVersions();
  const v = versions.find((x) => x.number === number);
  renderPreviewHead({ ...lastInfo, pages: 0, zoomable: false });
  await preview.show(doc, v);
  // A megtekintés a naplóba kerül — frissítjük (a szerkesztés alatt álló adatokat nem rajzoljuk újra).
  loadMeta();
}

$('versions').addEventListener('click', (e) => {
  const dl = e.target.closest('[data-version-download]');
  if (dl) {
    const v = versions.find((x) => x.number === Number(dl.dataset.versionDownload));
    Docs.downloadDocument(doc, v.number, v.fileName).then(loadMeta);
    return;
  }
  const b = e.target.closest('[data-version]');
  if (b) showVersion(Number(b.dataset.version));
});

// ---------- fejléc-műveletek ----------

$('download-btn').addEventListener('click', () => {
  const v = versions.find((x) => x.number === (selectedVersion ?? doc.currentVersion));
  Docs.downloadDocument(doc, v?.number, v?.fileName).then(loadMeta);
});

$('version-btn').addEventListener('click', async () => {
  const btn = $('version-btn');
  const label = btn.querySelector('span');
  btn.disabled = true;
  const updated = await Docs.uploadNewVersion(doc, (p) => { label.textContent = `Feltöltés… ${Math.round(p * 100)}%`; });
  label.textContent = 'Új verzió';
  btn.disabled = false;
  if (!updated) return;
  doc = updated;
  selectedVersion = null;
  await loadMeta();
  renderHeader();
  renderProps();
  showVersion(doc.currentVersion);
});

$('delete-btn').addEventListener('click', async () => {
  const ok = await uiConfirm(`A(z) „${doc.name}” dokumentum minden verziójával és naplójával véglegesen törlődik. Ez nem vonható vissza.`,
    { title: 'Dokumentum törlése', confirmText: 'Törlés', danger: true });
  if (!ok) return;
  const res = await apiFetch(`/api/documents/${docId}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 204) { showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni.')); return; }
  location.href = contextUrl(context());
});

// ---------- betöltés ----------

async function loadMeta() {
  const res = await apiFetch(`/api/documents/${docId}`);
  if (!res.ok) return;
  const data = await res.json();
  versions = data.versions;
  events = data.events;
  renderVersions();
  renderLog();
}

(async () => {
  const [dRes, tList, pRes, tRes, oRes] = await Promise.all([
    apiFetch(`/api/documents/${docId}`), Docs.loadTypes(), apiFetch('/api/properties'), apiFetch('/api/tenants'), apiFetch('/api/owners'),
  ]);
  if (dRes.status === 404) {
    $('d-name').textContent = 'Nem található';
    showError('A dokumentum nem található. Lehet, hogy törölték.');
    document.querySelectorAll('.detail-actions .btn').forEach((b) => { b.disabled = true; });
    return;
  }
  const data = await dRes.json();
  doc = data.document;
  versions = data.versions;
  events = data.events;
  types = tList;
  choices = {
    properties: pRes.ok ? await pRes.json() : [],
    tenants: tRes.ok ? await tRes.json() : [],
    owners: oRes.ok ? await oRes.json() : [],
  };
  renderHeader();
  renderProps();
  renderVersions();
  renderLog();
  showVersion(doc.currentVersion);
})();
