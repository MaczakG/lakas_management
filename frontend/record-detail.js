// Közös építőkövek az adatlapokhoz (bérlő, tulajdonos) — az ingatlan-adatlap (property-detail.js) mintájára:
// helyben szerkeszthető mezők (elhagyáskor / Enterre mentenek), panelek „masonry” elrendezése, fülek és a
// számlák táblája.
const DETAIL_ICON = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
const DETAIL_INVOICE_STATUS = { Sent: 'Kiküldve', Generated: 'Nem küldött', Failed: 'Sikertelen' };

// Helyben szerkeszthető mezők egy rekordon. getRecord(): az aktuális rekord; save(draft): elmenti a módosított
// másolatot, és hibánál az üzenetet adja vissza (különben null).
// Mező: { path, label, type?: 'text'|'email'|'textarea'|'select'|'date'|'number', options?: [{value,label}],
//         required?, placeholder?, wide? }
function createInlineEditor({ getRecord, save }) {
  const idOf = (field) => `f-${field.path.replace(/\./g, '-')}`;
  const get = (path) => path.split('.').reduce((o, k) => o?.[k], getRecord());
  const set = (target, path, value) => {
    const keys = path.split('.');
    const last = keys.pop();
    keys.reduce((o, k) => o[k], target)[last] = value;
  };
  const shown = (field, value) => {
    if (value == null) return '';
    if (field.type === 'date') { const [y, m, d] = String(value).split('-'); return `${y}. ${m}. ${d}.`; }
    if (field.type === 'number') return String(value).replace('.', ',');
    return String(value);
  };

  function renderField(field) {
    const value = get(field.path);
    const id = idOf(field);
    let control;
    if (field.type === 'select') {
      control = `<select class="iinput" id="${id}">${field.required ? '' : '<option value="">Nincs megadva</option>'}${field.options
        .map((o) => `<option value="${escapeHtml(o.value)}"${String(o.value) === String(value ?? '') ? ' selected' : ''}>${escapeHtml(o.label)}</option>`).join('')}</select>`;
    } else if (field.type === 'textarea') {
      control = `<textarea class="iinput" id="${id}" placeholder="${escapeHtml(field.placeholder || '')}">${escapeHtml(value ?? '')}</textarea>`;
    } else {
      const mode = field.type === 'email' ? ' inputmode="email"' : field.type === 'number' ? ' inputmode="decimal"' : field.type === 'date' ? ' inputmode="numeric"' : '';
      control = `<input class="iinput" id="${id}" type="text"${mode} value="${escapeHtml(shown(field, value))}" placeholder="${escapeHtml(field.placeholder || (field.required ? '' : '–'))}" autocomplete="off">`;
    }
    return `<div class="prop${field.wide ? ' wide' : ''}">
      <dt><label for="${id}">${escapeHtml(field.label)}</label></dt>
      <dd><div class="ifield"><div class="ictl">${control}</div><span class="istate" data-state="${field.path}"></span></div></dd>
    </div>`;
  }

  function parse(field, raw) {
    const text = raw.trim();
    if (!text) return field.required ? { error: 'Kötelező mező.' } : { value: null };
    if (field.type === 'email' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(text)) return { error: 'Érvénytelen e-mail cím.' };
    if (field.type === 'date') {
      const m = /^(\d{4})[.\-/ ]\s*(\d{1,2})[.\-/ ]\s*(\d{1,2})\.?$/.exec(text);
      if (!m) return { error: 'Dátum: ÉÉÉÉ.HH.NN.' };
      const iso = `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}`;
      if (Number.isNaN(Date.parse(iso))) return { error: 'Érvénytelen dátum.' };
      return { value: iso };
    }
    if (field.type === 'number') {
      const n = Number(text.replace(/[\s ]/g, '').replace(',', '.'));
      return Number.isFinite(n) ? { value: n } : { error: 'Számot adj meg.' };
    }
    return { value: text };
  }

  function bind(fields) {
    fields.forEach((field) => {
      const input = document.getElementById(idOf(field));
      if (!input) return;
      let lastSaved = input.value;
      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && field.type !== 'textarea') { e.preventDefault(); input.blur(); }
        if (e.key === 'Escape') { input.value = lastSaved; input.blur(); }
      });
      input.addEventListener('change', async () => {
        const state = document.querySelector(`[data-state="${field.path}"]`);
        const parsed = parse(field, input.value);
        if (parsed.error) {
          input.setAttribute('aria-invalid', 'true');
          state.className = 'istate err';
          state.textContent = parsed.error;
          return;
        }
        input.removeAttribute('aria-invalid');
        state.className = 'istate';
        state.textContent = 'Mentés…';
        const draft = structuredClone(getRecord());
        set(draft, field.path, parsed.value);
        const error = await save(draft);
        if (error) {
          input.setAttribute('aria-invalid', 'true');
          state.className = 'istate err';
          state.textContent = 'Hiba';
          state.title = error;
          return;
        }
        if (field.type !== 'select' && field.type !== 'textarea') input.value = shown(field, parsed.value);
        lastSaved = input.value;
        state.className = 'istate ok';
        state.textContent = 'Mentve';
        setTimeout(() => { if (state.textContent === 'Mentve') state.textContent = ''; }, 2000);
      });
    });
  }

  return { renderField, bind };
}

// A panelek a mindenkori legrövidebb oszlopba kerülnek (mint az ingatlan-adatlapon).
function layoutMasonry() {
  const grid = document.getElementById('masonry');
  if (!grid) return;
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
let detailResizeTimer;
window.addEventListener('resize', () => { clearTimeout(detailResizeTimer); detailResizeTimer = setTimeout(layoutMasonry, 120); });

// Fülek: a választott fül oldalanként megmarad (storageKey).
function setupTabs(storageKey, fallback) {
  const activate = (name) => {
    document.querySelectorAll('.record-tab').forEach((b) => b.classList.toggle('active', b.dataset.tab === name));
    document.querySelectorAll('.record-tab-pane').forEach((p) => { p.hidden = p.dataset.pane !== name; });
    try { localStorage.setItem(storageKey, name); } catch { /* privát mód */ }
  };
  document.querySelectorAll('.record-tab').forEach((b) => b.addEventListener('click', () => activate(b.dataset.tab)));
  let saved = null;
  try { saved = localStorage.getItem(storageKey); } catch { /* privát mód */ }
  activate(document.querySelector(`.record-tab[data-tab="${saved}"]`) ? saved : fallback);
}

function renderInvoiceTable(invoices, emptyText, { showProperty = true, showTenant = true, showPeriod = true } = {}) {
  if (!invoices.length) return `<div class="panel-empty">${escapeHtml(emptyText)}</div>`;
  return `<table>
    <thead><tr><th>Sorszám</th>${showPeriod ? '<th>Időszak</th>' : ''}${showProperty ? '<th>Ingatlan</th>' : ''}${showTenant ? '<th>Bérlő</th>' : ''}<th>Kiállítva</th><th class="num">Összeg</th><th>Állapot</th><th></th></tr></thead>
    <tbody>${invoices.map((i) => `
      <tr>
        <td><span class="mono">${escapeHtml(i.number)}</span></td>
        ${showPeriod ? `<td>${i.periodYear}. ${MONTH_NAMES[i.periodMonth - 1].toLowerCase()}</td>` : ''}
        ${showProperty ? `<td><a class="link" href="property-detail.html?id=${encodeURIComponent(i.propertyId)}">${escapeHtml(i.propertyName)}</a></td>` : ''}
        ${showTenant ? `<td>${escapeHtml(i.tenantName || '–')}</td>` : ''}
        <td>${formatDate(i.issuedAt)}</td>
        <td class="num">${formatCurrency(i.amountTotal)}</td>
        <td><span class="status-tag ${escapeHtml(i.status)}">${DETAIL_INVOICE_STATUS[i.status] || escapeHtml(i.status)}</span></td>
        <td class="num">${window.invoiceActionButtons ? invoiceActionButtons(i)
          : (i.status !== 'Failed' ? `<button class="icon-btn" type="button" title="PDF letöltése" aria-label="PDF letöltése" onclick="downloadFile('/api/invoices/${i.id}/pdf', ${jsArg(i.number + '.pdf')})">${DETAIL_ICON('download')}</button>` : '')}</td>
      </tr>`).join('')}</tbody></table>`;
}

function detailRow(label, html) {
  return `<div class="prop"><dt>${escapeHtml(label)}</dt><dd>${html}</dd></div>`;
}
