// Megosztott rezsi-felviteli popup — a properties.html (Rezsi tab) és az utility-entry.html
// egyaránt ezt hívja meg (window.openUtilityModal), hogy egy adott ingatlan egy adott hónapjához
// egy helyen lehessen tételeket megnézni/felvenni/törölni. Ugyanúgy egy önálló, betöltéskor a
// document.body végére beszúrt fragment, mint a nav.js sidebar-ja — nincs build lépés, nincs
// modul-rendszer, ezért egy közös script a duplikáció elkerülésének módja két oldal között.
(function () {
  const styleTag = document.createElement('style');
  styleTag.textContent = `
    #um-overlay{z-index:300;}
    #um-overlay .modal{max-width:640px;}
    .um-draft-row{display:grid;grid-template-columns:1fr 1fr auto;gap:10px;align-items:flex-end;margin-bottom:10px;}
    .um-draft-row .field{margin-bottom:0;}
    .um-draft-row button svg{width:16px;height:16px;display:block;margin:auto;}
    .um-draft-row button{width:42px;height:42px;border:1px solid var(--line-strong);background:var(--surface);cursor:pointer;color:var(--coral);flex-shrink:0;}
    .um-total{font-size:13px;font-weight:600;color:var(--ink-soft);margin:4px 0 16px;}
    .um-existing-total{font-size:13px;font-weight:600;color:var(--ink-soft);margin:10px 0 20px;text-align:right;}
    #um-existing-tbody input{height:36px;font-size:13.5px;}
    #um-existing-tbody .row-actions{white-space:nowrap;}
    .um-note{display:none;font-size:13px;line-height:1.5;color:var(--ink-soft);border-left:2px solid var(--line-strong);padding:2px 0 2px 12px;margin:0 0 18px;}
    .um-note.show{display:block;}
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

  function addDraftRow() {
    const row = document.createElement('div');
    row.className = 'um-draft-row';
    row.innerHTML = `
      <div class="field"><label>Megnevezés</label><input type="text" class="um-draft-label" placeholder="pl. Áram"></div>
      <div class="field"><label>Összeg (Ft)</label><input type="number" class="um-draft-amount" min="0"></div>
      <button type="button" title="Sor eltávolítása" aria-label="Sor eltávolítása"><svg viewBox="0 0 24 24" aria-hidden="true"><use href="assets/icons/icons.svg#x"/></svg></button>
    `;
    row.querySelector('button').addEventListener('click', () => { row.remove(); updateDraftTotal(); });
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

  async function refresh() {
    const [entriesRes, invoicesRes] = await Promise.all([
      apiFetch(`/api/properties/${state.propertyId}/utility-costs`),
      apiFetch(`/api/properties/${state.propertyId}/invoices`),
    ]);
    const allEntries = await entriesRes.json();
    const invoices = await invoicesRes.json();
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
          <td>${escapeHtml(e.label)}</td>
          <td>${formatCurrency(e.amount)}</td>
          <td class="row-actions">${e.isLocked
            ? (e.invoiceId
              ? `<a class="link mono" href="invoice-detail.html?id=${encodeURIComponent(e.invoiceId)}" title="Ezen a bizonylaton szerepel; sztornóig zárolt">${escapeHtml(e.invoiceNumber || 'Bizonylaton')}</a>`
              : '<span class="status-tag inactive">Lezárva</span>')
            : `<button class="btn btn-ghost btn-sm" type="button" onclick="window.__umEditEntry('${e.id}', this)">Szerkesztés</button>
               <button class="btn btn-danger btn-sm" type="button" onclick="window.__umDeleteEntry('${e.id}')">Törlés</button>`}</td>
        </tr>
      `).join('');

    const sum = entries.reduce((acc, e) => acc + e.amount, 0);
    document.getElementById('um-existing-total').textContent = entries.length > 0 ? `Összesen: ${formatCurrency(sum)}` : '';

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

    resetDraftRows();
  }

  // Meglévő tétel helyben szerkesztése: a sor mezőkké alakul, Mentés / Mégse.
  window.__umEditEntry = (entryId, btn) => {
    const entry = state.entries.find((e) => e.id === entryId);
    const row = btn.closest('tr');
    if (!entry || !row) return;
    row.innerHTML = `
      <td><input type="text" class="um-edit-label" aria-label="Megnevezés"></td>
      <td><input type="number" class="um-edit-amount" min="0" aria-label="Összeg (Ft)"></td>
      <td class="row-actions"><button class="btn btn-primary btn-sm" type="button" data-save>Mentés</button>
        <button class="btn btn-ghost btn-sm" type="button" data-cancel>Mégse</button></td>`;
    row.querySelector('.um-edit-label').value = entry.label;
    row.querySelector('.um-edit-amount').value = entry.amount;
    row.querySelector('.um-edit-label').focus();
    row.querySelector('[data-cancel]').addEventListener('click', refresh);
    row.querySelector('[data-save]').addEventListener('click', async () => {
      const label = row.querySelector('.um-edit-label').value.trim();
      const amount = Number(row.querySelector('.um-edit-amount').value);
      const res = await apiFetch(`/api/properties/${state.propertyId}/utility-costs/${entryId}`, { method: 'PUT', body: JSON.stringify({ label, amount }) });
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

  document.getElementById('um-save-btn').addEventListener('click', async () => {
    const errorEl = document.getElementById('um-form-error');
    const successEl = document.getElementById('um-form-success');
    errorEl.classList.remove('show');
    successEl.classList.remove('show');

    const rows = [...document.querySelectorAll('.um-draft-row')].map((r) => ({
      label: r.querySelector('.um-draft-label').value.trim(),
      amount: Number(r.querySelector('.um-draft-amount').value),
    })).filter((r) => r.label && r.amount);

    if (rows.length === 0) {
      errorEl.textContent = 'Adj meg legalább egy tételt (megnevezéssel és összeggel).';
      errorEl.classList.add('show');
      return;
    }

    const payloadBase = { year: state.year, month: state.month };
    for (const row of rows) {
      const res = await apiFetch(`/api/properties/${state.propertyId}/utility-costs`, {
        method: 'POST',
        body: JSON.stringify({ ...payloadBase, ...row }),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        errorEl.textContent = data.message || 'Nem sikerült minden tételt rögzíteni.';
        errorEl.classList.add('show');
        await refresh();
        return;
      }
    }

    successEl.classList.add('show');
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
    document.getElementById('um-overlay').classList.add('open');
    await refresh();
  };
})();
