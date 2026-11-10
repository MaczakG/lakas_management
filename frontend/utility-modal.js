// Megosztott rezsi-felviteli popup — a properties.html (Rezsi tab) és az utility-entry.html
// egyaránt ezt hívja meg (window.openUtilityModal), hogy egy adott ingatlan egy adott hónapjához
// egy helyen lehessen tételeket megnézni/felvenni/törölni. Ugyanúgy egy önálló, betöltéskor a
// document.body végére beszúrt fragment, mint a nav.js sidebar-ja — nincs build lépés, nincs
// modul-rendszer, ezért egy közös script a duplikáció elkerülésének módja két oldal között.
(function () {
  const styleTag = document.createElement('style');
  styleTag.textContent = `
    #um-overlay{z-index:300;}
    #um-overlay .modal{max-width:760px;}
    #um-existing-tbody td:nth-child(2){white-space:nowrap;}
    /* Telefonon a sor két szintre törik: fent a megnevezés és az összeg, alatta a gombok (nem kell oldalra görgetni). */
    @media (max-width:640px){
      #um-overlay thead{display:none;}
      #um-overlay .card-table table,#um-existing-tbody{display:block;width:100%;min-width:0;}
      #um-existing-tbody tr:not(.empty-row){display:grid;grid-template-columns:minmax(0,1fr) auto;column-gap:10px;}
      #um-existing-tbody tr:not(.empty-row) td:not(.row-actions){border-bottom:none;padding-bottom:4px;}
      #um-existing-tbody td.row-actions{grid-column:1 / -1;display:flex;flex-wrap:wrap;align-items:center;gap:6px;justify-content:flex-start;white-space:normal;padding-top:4px;text-align:left;}
    }
    .um-draft-row{display:grid;grid-template-columns:1fr 1fr auto;gap:10px;align-items:flex-end;margin-bottom:10px;}
    .um-draft-row .field{margin-bottom:0;}
    .um-draft-row .um-note-field{grid-column:1 / -1;}
    .um-draft-row{padding-bottom:12px;border-bottom:1px solid var(--line);margin-bottom:12px;}
    .um-entry-note{font-size:12px;color:var(--muted);margin-top:2px;white-space:pre-wrap;overflow-wrap:anywhere;}
    #um-existing-tbody .um-edit-note{margin-top:6px;}
    .um-draft-row button svg{width:16px;height:16px;display:block;margin:auto;}
    .um-draft-row button{width:42px;height:42px;border:1px solid var(--line-strong);background:var(--surface);cursor:pointer;color:var(--coral);flex-shrink:0;}
    .um-total{font-size:13px;font-weight:600;color:var(--ink-soft);margin:4px 0 16px;}
    .um-existing-total{font-size:13px;font-weight:600;color:var(--ink-soft);margin:10px 0 20px;text-align:right;}
    #um-existing-tbody input{height:36px;font-size:13.5px;}
    #um-existing-tbody .row-actions{white-space:nowrap;}
    .um-note{display:none;font-size:13px;line-height:1.5;color:var(--ink-soft);border-left:2px solid var(--line-strong);padding:2px 0 2px 12px;margin:0 0 18px;}
    .um-note.show{display:block;}
    .um-atts{display:flex;flex-wrap:wrap;gap:6px;margin-top:6px;}
    .um-atts:empty{display:none;}
    .um-att{display:inline-flex;align-items:center;gap:5px;max-width:100%;padding:3px 4px 3px 8px;border:1px solid var(--line);border-radius:var(--radius-sm);background:var(--surface-2);font-size:12px;font-weight:500;color:var(--ink-soft);}
    .um-att svg{width:13px;height:13px;flex-shrink:0;color:var(--muted);}
    .um-att a,.um-att .um-att-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:inherit;text-decoration:none;}
    .um-att a:hover{color:var(--accent);text-decoration:underline;}
    .um-att .dim{flex-shrink:0;}
    .um-att button{display:inline-flex;align-items:center;justify-content:center;width:20px;height:20px;padding:0;border:none;background:none;color:var(--muted);cursor:pointer;border-radius:4px;flex-shrink:0;}
    .um-att button:hover{color:var(--coral);background:var(--coral-bg);}
    .um-att button svg{width:12px;height:12px;color:inherit;}
    .um-draft-row .um-att button{border:none;width:20px;height:20px;background:none;color:var(--muted);}
    .um-draft-row .um-file-field{grid-column:1 / -1;display:flex;flex-direction:column;gap:6px;}
    .um-draft-row .um-attach-btn,#um-existing-tbody .um-attach-btn{display:inline-flex;align-items:center;gap:6px;width:auto;height:32px;padding:0 10px;border:1px solid var(--line-strong);border-radius:var(--radius-sm);background:var(--surface);color:var(--ink);font:inherit;font-size:12.5px;font-weight:600;cursor:pointer;align-self:flex-start;}
    .um-draft-row .um-attach-btn svg,#um-existing-tbody .um-attach-btn svg{width:14px;height:14px;margin:0;display:inline-block;color:var(--muted);}
    .um-flags{display:inline-flex;flex-wrap:wrap;gap:8px;margin-left:8px;vertical-align:middle;}
    .um-flags .status-tag{font-size:12px;}
    .um-check{display:flex;align-items:center;gap:8px;font-size:13px;font-weight:500;color:var(--ink);cursor:pointer;}
    .um-check input{width:16px;height:16px;accent-color:var(--accent);flex-shrink:0;}
    .um-draft-row .um-forward-field{grid-column:1 / -1;}
    #um-existing-tbody .um-check{margin-top:6px;}
    .um-prepare{display:none;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin:0 0 16px;padding:10px 12px;border:1px solid var(--line);border-radius:var(--radius-sm);background:var(--surface-2);font-size:13px;color:var(--ink-soft);}
    .um-prepare.show{display:flex;}
    .um-prepare .btn{height:34px;padding:0 12px;font-size:12.5px;flex-shrink:0;}
    .um-section-title{font-family:var(--font-display);font-weight:700;font-size:14.5px;margin:0 0 10px;color:var(--ink);}
  `;
  document.head.appendChild(styleTag);

  document.body.insertAdjacentHTML('beforeend', `
    <div class="modal-overlay" id="um-overlay">
      <div class="modal">
        <div class="modal-head">
          <div><h2 id="um-title">Rezsi tételek</h2><p id="um-subtitle"></p></div>
          <button aria-label="Bezárás" class="modal-close" id="um-close" type="button"><svg viewBox="0 0 24 24" aria-hidden="true"><use href="assets/icons/icons.svg#x"/></svg></button>
        </div>
        <div class="modal-body">
          <div class="um-note" id="um-late-note"></div>
          <div class="form-error" id="um-locked-note"></div>

          <div class="um-prepare" id="um-prepare"><span id="um-prepare-text">Az ingatlan rezsi tételeiből elkészíthető a hónap rezsije.</span>
            <button class="btn btn-secondary" type="button" id="um-prepare-btn">Rezsi tételek behúzása</button></div>

          <h3 class="um-section-title">Rögzített tételek</h3>
          <div class="card-table" style="margin-bottom:4px;">
            <table>
              <thead><tr><th>Megnevezés</th><th>Összeg</th><th></th></tr></thead>
              <tbody id="um-existing-tbody"></tbody>
            </table>
          </div>
          <div class="um-existing-total" id="um-existing-total"></div>

          <h3 class="um-section-title" id="um-new-title">Új tételek</h3>
          <div id="um-draft-rows"></div>
          <div class="um-total" id="um-draft-total"></div>
          <div class="form-error" id="um-form-error"></div>
          <div class="form-success" id="um-form-success">A tételek sikeresen rögzítve.</div>
          <button class="btn btn-ghost btn-sm" type="button" id="um-add-row-btn">+ Sor hozzáadása</button>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" id="um-cancel" type="button">Bezárás</button>
          <button class="btn btn-primary" id="um-save-btn" type="button">Mentés</button>
        </div>
      </div>
    </div>
  `);

  const LATE_UTILITY_MONTHS = 36;
  let state = { propertyId: null, year: null, month: null, onClose: null };

  // ---------- csatolmányok (pl. a szolgáltatói számla): a bizonylat levelével kiküldhetők a bérlőnek ----------
  const ATTACH_ACCEPT = '.pdf,.jpg,.jpeg,.png,.gif,.webp,.heic,.heif,.doc,.docx,.xls,.xlsx,.odt,.ods,.txt,.csv';
  const ATTACH_MAX_BYTES = 15 * 1024 * 1024;
  const ATTACH_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#paperclip"/></svg>';
  const X_ICON = '<svg viewBox="0 0 24 24" aria-hidden="true"><use href="assets/icons/icons.svg#x"/></svg>';
  const FORWARD_TITLE = 'A bérleti díj már tartalmazza: nem kerül külön a bérlő bizonylatára, csak a tulajdonosi elszámolásban szerepel.';
  const sizeText = (bytes) => (bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`);

  function pickFiles() {
    return new Promise((resolve) => {
      const input = document.createElement('input');
      input.type = 'file';
      input.multiple = true;
      input.accept = ATTACH_ACCEPT;
      input.addEventListener('change', () => resolve([...input.files]));
      input.addEventListener('cancel', () => resolve([]));
      input.click();
    });
  }

  // Feltölt egy fájlt a tételhez. Hibánál az üzenetet adja vissza (különben null).
  async function uploadAttachment(entryId, file) {
    if (file.size > ATTACH_MAX_BYTES) return `A(z) „${file.name}” túl nagy: egy fájl legfeljebb 15 MB lehet.`;
    const form = new FormData();
    form.append('file', file);
    form.append('utilityCostEntryId', entryId);
    try {
      const res = await fetch(`${API_BASE}/api/documents`, { method: 'POST', headers: authHeaders(), body: form });
      if (res.status === 401) { clearAuth(); location.href = 'login.html'; return 'Lejárt a munkamenet.'; }
      if (!res.ok) return (await res.json().catch(() => null))?.message || `A(z) „${file.name}” feltöltése nem sikerült.`;
      return null;
    } catch {
      return `A(z) „${file.name}” feltöltése nem sikerült. Ellenőrizd az internetkapcsolatot.`;
    }
  }

  function attachmentsHtml(entry) {
    return (entry.attachments || []).map((a) => `
      <span class="um-att">${ATTACH_ICON}<a href="document-detail.html?id=${encodeURIComponent(a.id)}" target="_blank" rel="noopener" title="Megnyitás új lapon">${escapeHtml(a.fileName)}</a>
        <span class="dim">${sizeText(a.sizeBytes)}</span>
        <button type="button" title="Csatolmány törlése" aria-label="Csatolmány törlése: ${escapeHtml(a.fileName)}" onclick="window.__umDeleteAttachment('${a.id}')">${X_ICON}</button></span>`).join('');
  }

  window.__umAttach = async (entryId) => {
    const files = await pickFiles();
    if (!files.length) return;
    const errors = [];
    for (const file of files) {
      const error = await uploadAttachment(entryId, file);
      if (error) errors.push(error);
    }
    await refresh({ keepDrafts: true });
    if (typeof state.onSaved === 'function') state.onSaved();
    if (errors.length) await uiAlert(errors.join('\n'), { title: 'Nem minden fájl töltődött fel' });
  };

  window.__umDeleteAttachment = async (documentId) => {
    if (!(await uiConfirm('Biztosan törlöd ezt a csatolmányt? A fájl véglegesen törlődik.', { title: 'Csatolmány törlése', confirmText: 'Törlés', danger: true }))) return;
    const res = await apiFetch(`/api/documents/${documentId}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 204) {
      await uiAlert((await res.json().catch(() => null))?.message || 'Nem sikerült törölni a csatolmányt.', { title: 'Törlés sikertelen' });
      return;
    }
    await refresh({ keepDrafts: true });
    if (typeof state.onSaved === 'function') state.onSaved();
  };

  function addDraftRow() {
    const row = document.createElement('div');
    row.className = 'um-draft-row';
    row.innerHTML = `
      <div class="field"><label>Megnevezés</label><input type="text" class="um-draft-label" placeholder="pl. Áram"></div>
      <div class="field"><label>Összeg (Ft)</label><input type="number" class="um-draft-amount" min="0"></div>
      <button type="button" title="Sor eltávolítása" aria-label="Sor eltávolítása"><svg viewBox="0 0 24 24" aria-hidden="true"><use href="assets/icons/icons.svg#x"/></svg></button>
      <div class="field um-note-field"><label>Megjegyzés <span class="dim">(nem kötelező, a bizonylatra nem kerül rá)</span></label><input type="text" class="um-draft-note" maxlength="500" placeholder="pl. mérőállás, a szolgáltatói számla száma"></div>
      <div class="um-forward-field"><label class="um-check" title="${FORWARD_TITLE}"><input type="checkbox" class="um-draft-forward">Tovább számlázandó <span class="dim">(a bérleti díj tartalmazza, nem kerül a bérlő bizonylatára)</span></label></div>
      <div class="um-file-field"><button type="button" class="um-attach-btn">${ATTACH_ICON}Csatolmány</button><div class="um-atts"></div></div>
    `;
    // A sorhoz kiválasztott fájlok a tétel mentésekor töltődnek fel.
    row.umFiles = [];
    const renderFiles = () => {
      row.querySelector('.um-atts').innerHTML = row.umFiles.map((f, i) => `
        <span class="um-att">${ATTACH_ICON}<span class="um-att-name">${escapeHtml(f.name)}</span><span class="dim">${sizeText(f.size)}</span>
          <button type="button" data-remove-file="${i}" title="Eltávolítás" aria-label="Eltávolítás: ${escapeHtml(f.name)}">${X_ICON}</button></span>`).join('');
      row.querySelectorAll('[data-remove-file]').forEach((b) => b.addEventListener('click', () => { row.umFiles.splice(Number(b.dataset.removeFile), 1); renderFiles(); }));
    };
    row.querySelector('.um-attach-btn').addEventListener('click', async () => { row.umFiles.push(...await pickFiles()); renderFiles(); });
    row.querySelector(':scope > button').addEventListener('click', () => { row.remove(); updateDraftTotal(); });
    row.querySelector('.um-draft-amount').addEventListener('input', updateDraftTotal);
    document.getElementById('um-draft-rows').appendChild(row);
  }

  function updateDraftTotal() {
    const rows = [...document.querySelectorAll('.um-draft-row')];
    const sum = rows.reduce((acc, r) => acc + (Number(r.querySelector('.um-draft-amount').value) || 0), 0);
    document.getElementById('um-draft-total').textContent = rows.length > 0 ? `Új tételek összesen: ${formatCurrency(sum)}` : '';
  }

  function resetDraftRows() {
    document.getElementById('um-draft-rows').innerHTML = '';
    addDraftRow(); // alapból egy sor; többet a „+ Sor hozzáadása” gombbal
    updateDraftTotal();
  }

  // keepDrafts: csatolmány feltöltése / törlése után a még el nem mentett új sorok megmaradnak.
  async function refresh({ keepDrafts = false } = {}) {
    const [entriesRes, invoicesRes, itemsRes] = await Promise.all([
      apiFetch(`/api/properties/${state.propertyId}/utility-costs`),
      apiFetch(`/api/properties/${state.propertyId}/invoices`),
      apiFetch(`/api/properties/${state.propertyId}/utility-items`),
    ]);
    const allEntries = await entriesRes.json();
    const invoices = await invoicesRes.json();
    const items = itemsRes.ok ? await itemsRes.json() : [];
    // Tételenkénti zárolás: a bizonylatra került tétel sztornóig nem módosítható. A már bizonylatolt hónapra utólag
    // felvett tétel nem zárolt, és a következő bizonylatra kerül, a hónapjával jelölve — legfeljebb 36 hónapra visszamenőleg.
    const periodInvoice = invoices.find((i) => (!i.cancelledAt && !i.cancelsInvoiceId) && (i.status === 'Sent' || i.status === 'Generated')
      && i.periodYear === state.year && i.periodMonth === state.month);
    const now = new Date();
    const tooOld = (now.getFullYear() * 12 + now.getMonth()) - (state.year * 12 + state.month - 1) >= LATE_UTILITY_MONTHS;

    const entries = allEntries.filter((e) => e.year === state.year && e.month === state.month);
    state.entries = entries;
    const tbody = document.getElementById('um-existing-tbody');
    tbody.innerHTML = entries.length === 0
      ? `<tr class="empty-row"><td colspan="3">Nincs még rögzített tétel.</td></tr>`
      : entries.map((e) => `
        <tr>
          <td>${escapeHtml(e.label)}<span class="um-flags">${e.isPending ? '<span class="status-tag warn" title="Még nincs összege: amíg nincs kitöltve, nem kerül bizonylatra">Kitöltendő</span>' : ''}${e.forwardBilled ? `<span class="status-tag inactive" title="${FORWARD_TITLE}">Tovább számlázandó</span>` : ''}</span>${e.note ? `<div class="um-entry-note">${escapeHtml(e.note)}</div>` : ''}<div class="um-atts">${attachmentsHtml(e)}</div></td>
          <td>${e.isPending ? '<span class="dim">–</span>' : formatCurrency(e.amount)}</td>
          <td class="row-actions"><button class="um-attach-btn" type="button" title="Csatolmány hozzáadása (pl. a szolgáltatói számla)" onclick="window.__umAttach('${e.id}')" aria-label="Csatolmány hozzáadása">${ATTACH_ICON}<span>Csatolmány</span></button>
            ${e.isLocked
            ? (e.invoiceId
              ? `<a class="link mono" href="invoice-detail.html?id=${encodeURIComponent(e.invoiceId)}" title="Ezen a bizonylaton szerepel; sztornóig zárolt">${escapeHtml(e.invoiceNumber || 'Bizonylaton')}</a>`
              : '<span class="status-tag inactive">Lezárva</span>')
            : `<button class="btn ${e.isPending ? 'btn-primary' : 'btn-ghost'} btn-sm" type="button" onclick="window.__umEditEntry('${e.id}', this)">${e.isPending ? 'Kitöltés' : 'Szerkesztés'}</button>
               <button class="btn btn-danger btn-sm" type="button" onclick="window.__umDeleteEntry('${e.id}')">Törlés</button>`}</td>
        </tr>
      `).join('');

    // A bérlő bizonylatára csak a nem „tovább számlázandó” tételek kerülnek.
    const billed = entries.filter((e) => !e.forwardBilled).reduce((acc, e) => acc + e.amount, 0);
    const forwarded = entries.filter((e) => e.forwardBilled).reduce((acc, e) => acc + e.amount, 0);
    document.getElementById('um-existing-total').textContent = entries.length === 0 ? ''
      : forwarded > 0 ? `Bizonylatra kerül: ${formatCurrency(billed)} · Tovább számlázandó: ${formatCurrency(forwarded)}`
        : `Összesen: ${formatCurrency(billed)}`;

    // Ha az ingatlannak vannak rezsi tételei, a hónap rezsije gombbal is elkészíthető belőlük.
    document.getElementById('um-prepare').classList.toggle('show', items.length > 0 && !tooOld);

    const lateNote = document.getElementById('um-late-note');
    lateNote.textContent = periodInvoice && !tooOld
      ? `Erre a hónapra már kiállították a(z) ${periodInvoice.number} bizonylatot; a rajta szereplő tételek sztornóig zároltak. Az itt most felvett tételek a következő bizonylatra kerülnek, ezzel a hónappal jelölve.`
      : '';
    lateNote.classList.toggle('show', !!lateNote.textContent);
    const lockedNote = document.getElementById('um-locked-note');
    lockedNote.textContent = tooOld ? `Legfeljebb ${LATE_UTILITY_MONTHS} hónapra visszamenőleg lehet rezsitételt felvenni.` : '';
    lockedNote.classList.toggle('show', tooOld);
    for (const id of ['um-new-title', 'um-draft-rows', 'um-draft-total', 'um-add-row-btn', 'um-save-btn']) {
      document.getElementById(id).style.display = tooOld ? 'none' : '';
    }

    if (!keepDrafts) resetDraftRows();
  }

  // Meglévő tétel helyben szerkesztése: a sor mezőkké alakul, Mentés / Mégse.
  window.__umEditEntry = (entryId, btn) => {
    const entry = state.entries.find((e) => e.id === entryId);
    const row = btn.closest('tr');
    if (!entry || !row) return;
    row.innerHTML = `
      <td><input type="text" class="um-edit-label" aria-label="Megnevezés">
        <input type="text" class="um-edit-note" maxlength="500" aria-label="Megjegyzés" placeholder="Megjegyzés (nem kötelező)">
        <label class="um-check" title="${FORWARD_TITLE}"><input type="checkbox" class="um-edit-forward">Tovább számlázandó</label></td>
      <td><input type="number" class="um-edit-amount" min="0" aria-label="Összeg (Ft)"></td>
      <td class="row-actions"><button class="btn btn-primary btn-sm" type="button" data-save>Mentés</button>
        <button class="btn btn-ghost btn-sm" type="button" data-cancel>Mégse</button></td>`;
    row.querySelector('.um-edit-label').value = entry.label;
    row.querySelector('.um-edit-amount').value = entry.isPending ? '' : entry.amount;
    row.querySelector('.um-edit-note').value = entry.note || '';
    row.querySelector('.um-edit-forward').checked = !!entry.forwardBilled;
    // Kitöltendő tételnél rögtön az összeg következik.
    row.querySelector(entry.isPending ? '.um-edit-amount' : '.um-edit-label').focus();
    row.querySelector('[data-cancel]').addEventListener('click', () => refresh({ keepDrafts: true }));
    row.querySelector('[data-save]').addEventListener('click', async () => {
      const label = row.querySelector('.um-edit-label').value.trim();
      const amount = Number(row.querySelector('.um-edit-amount').value);
      const note = row.querySelector('.um-edit-note').value.trim() || null;
      const forwardBilled = row.querySelector('.um-edit-forward').checked;
      const res = await apiFetch(`/api/properties/${state.propertyId}/utility-costs/${entryId}`, { method: 'PUT', body: JSON.stringify({ label, amount, note, forwardBilled }) });
      if (!res.ok) {
        await uiAlert(errorMessage(res, await readJson(res), 'Nem sikerült menteni a tételt.'), { title: 'Mentés sikertelen' });
        return;
      }
      await refresh();
      if (typeof state.onSaved === 'function') state.onSaved();
    });
  };

  window.__umDeleteEntry = async (entryId) => {
    if (!(await uiConfirm('Biztosan törlöd ezt a tételt?', { title: 'Törlés', confirmText: 'Törlés', danger: true }))) return;
    const res = await apiFetch(`/api/properties/${state.propertyId}/utility-costs/${entryId}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 204) {
      const data = await res.json().catch(() => ({}));
      await uiAlert(data.message || 'Nem sikerült törölni a tételt.', { title: 'Törlés sikertelen' });
      return;
    }
    await refresh();
  };

  document.getElementById('um-add-row-btn').addEventListener('click', addDraftRow);

  // A hónap rezsijének elkészítése az ingatlan rezsi tételeiből: a hiányzókat pótolja (a fix tételek összeggel, a
  // nem fixek kitöltendőként jönnek létre).
  document.getElementById('um-prepare-btn').addEventListener('click', async () => {
    const btn = document.getElementById('um-prepare-btn');
    btn.disabled = true;
    try {
      const res = await apiFetch(`/api/properties/${state.propertyId}/utility-costs/prepare`, { method: 'POST', body: JSON.stringify({ year: state.year, month: state.month }) });
      const data = await res.json().catch(() => null);
      if (!res.ok) { await uiAlert(data?.message || 'Nem sikerült behúzni a rezsi tételeket.', { title: 'Nem sikerült' }); return; }
      await refresh({ keepDrafts: true });
      document.getElementById('um-prepare-text').textContent = data.created > 0
        ? `${data.created} tétel került a hónapba a rezsi tételekből.`
        : 'Minden rezsi tétel szerepel már a hónapban.';
      if (typeof state.onSaved === 'function') state.onSaved();
    } finally {
      btn.disabled = false;
    }
  });

  document.getElementById('um-save-btn').addEventListener('click', async () => {
    const errorEl = document.getElementById('um-form-error');
    const successEl = document.getElementById('um-form-success');
    errorEl.classList.remove('show');
    successEl.classList.remove('show');

    const rows = [...document.querySelectorAll('.um-draft-row')].map((r) => ({
      label: r.querySelector('.um-draft-label').value.trim(),
      amount: Number(r.querySelector('.um-draft-amount').value),
      note: r.querySelector('.um-draft-note').value.trim() || null,
      forwardBilled: r.querySelector('.um-draft-forward').checked,
      files: r.umFiles || [],
    })).filter((r) => r.label && r.amount);

    if (rows.length === 0) {
      errorEl.textContent = 'Adj meg legalább egy tételt (megnevezéssel és összeggel).';
      errorEl.classList.add('show');
      return;
    }

    const saveBtn = document.getElementById('um-save-btn');
    saveBtn.disabled = true;
    const uploadErrors = [];
    try {
      for (const { files, ...row } of rows) {
        const res = await apiFetch(`/api/properties/${state.propertyId}/utility-costs`, {
          method: 'POST',
          body: JSON.stringify({ year: state.year, month: state.month, ...row }),
        });
        if (!res.ok) {
          const data = await res.json().catch(() => ({}));
          errorEl.textContent = data.message || 'Nem sikerült minden tételt rögzíteni.';
          errorEl.classList.add('show');
          await refresh();
          return;
        }
        const entry = await res.json();
        for (const file of files) {
          const error = await uploadAttachment(entry.id, file);
          if (error) uploadErrors.push(error);
        }
      }
    } finally {
      saveBtn.disabled = false;
    }

    if (uploadErrors.length) {
      errorEl.textContent = `A tételek rögzítve, de nem minden csatolmány töltődött fel. ${uploadErrors.join(' ')}`;
      errorEl.classList.add('show');
    } else {
      successEl.classList.add('show');
    }
    await refresh();
    if (typeof state.onSaved === 'function') state.onSaved();
  });

  function closeModal() {
    document.getElementById('um-overlay').classList.remove('open');
    if (typeof state.onClose === 'function') state.onClose();
  }
  document.getElementById('um-close').addEventListener('click', closeModal);
  document.getElementById('um-cancel').addEventListener('click', closeModal);
  document.getElementById('um-overlay').addEventListener('click', (e) => { if (e.target.id === 'um-overlay') closeModal(); });

  // opts: { onClose, onSaved } — onClose fut a popup bezárásakor (X/Bezárás/háttérre kattintás),
  // onSaved minden sikeres mentés után (hogy a hívó oldal listája/összesítője frissülhessen anélkül,
  // hogy be kellene zárni a popupot).
  window.openUtilityModal = async function (propertyId, propertyName, year, month, opts = {}) {
    state = { propertyId, year, month, onClose: opts.onClose, onSaved: opts.onSaved };
    document.getElementById('um-title').textContent = `Rezsi: ${propertyName}`;
    document.getElementById('um-subtitle').textContent = `${year}. ${MONTH_NAMES[month - 1]}`;
    document.getElementById('um-form-error').classList.remove('show');
    document.getElementById('um-form-success').classList.remove('show');
    document.getElementById('um-prepare-text').textContent = 'Az ingatlan rezsi tételeiből elkészíthető a hónap rezsije.';
    document.getElementById('um-overlay').classList.add('open');
    await refresh();
  };
})();
