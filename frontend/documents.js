// Dokumentumok: a Dokumentumok fül az ingatlan, bérlő és tulajdonos adatlapján (lista típus-szűrővel, feltöltés
// gombbal vagy behúzással, több fájl egyszerre, folyamatjelzővel), a szerkeszthető dokumentumtípusok ablaka, és
// az előnézet, amit a dokumentum adatlapja (document-detail.html) használ. PDF-nél a PDF.js rajzolja ki az
// oldalakat (saját szerverről, assets/pdfjs), képnél a kép, szövegnél a szöveg jelenik meg; a többi letölthető.
//
// Használat a fülön: mountDocuments({ scope: { propertyId } | { tenantId } | { ownerId }, container, count,
//   uploadButton, links: async (doc?) => [{ key: 'tenantId', label, options: [{ value, label }], value, hint }] })
(function () {
  const ICO = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const MAX_BYTES = 15 * 1024 * 1024;
  const EXTENSIONS = ['pdf', 'jpg', 'jpeg', 'png', 'gif', 'webp', 'heic', 'heif', 'doc', 'docx', 'xls', 'xlsx', 'odt', 'ods', 'txt', 'csv'];
  const ACCEPT = EXTENSIONS.map((e) => `.${e}`).join(',');
  const MANAGE = '__manage';
  // 100% = valódi méret (PDF: 1 pont = 1/72 hüvelyk, a képernyőn 96 px/hüvelyk); alapból 60%.
  const DEFAULT_ZOOM = 0.6;
  const PDF_CSS_SCALE = 96 / 72;

  const ext = (name) => (String(name).match(/\.([^.]+)$/)?.[1] || '').toLowerCase();
  // Kiválasztott (még fel nem töltött) fájl típusa a kiterjesztésből — a böngésző által adott MIME-típus gyakran üres.
  const typeFromName = (name) => ({ pdf: 'application/pdf', jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
    heic: 'image/heic', heif: 'image/heif', doc: 'application/msword', docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    odt: 'application/vnd.oasis.opendocument.text', xls: 'application/vnd.ms-excel', xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    ods: 'application/vnd.oasis.opendocument.spreadsheet', txt: 'text/plain', csv: 'text/csv' }[ext(name)] || '');
  const kindOf = (doc) => {
    const ct = doc.contentType || '';
    if (ct === 'application/pdf') return 'pdf';
    if (/^image\/(jpeg|png|gif|webp)$/.test(ct)) return 'image';
    if (/^image\//.test(ct)) return 'image-other';
    if (/^text\//.test(ct)) return 'text';
    if (/sheet|excel/.test(ct)) return 'sheet';
    if (/word|opendocument\.text/.test(ct)) return 'word';
    return 'file';
  };
  const iconOf = (doc) => (doc.contentType === 'text/csv' ? 'file-csv'
    : { pdf: 'file-pdf', image: 'file-image', 'image-other': 'file-image', text: 'file-text', sheet: 'file-excel', word: 'file-word' }[kindOf(doc)] || 'file');
  const typeLabel = (doc) => (ext(doc.fileName) || 'fájl').toUpperCase();

  function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`;
  }
  const formatDateTime = (value) => {
    const d = new Date(value);
    return `${d.toLocaleDateString('hu-HU')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  };

  // ---------- dokumentumtípusok ----------

  let typesCache = null;
  async function loadTypes(force = false) {
    if (!typesCache || force) {
      typesCache = apiFetch('/api/document-types').then(async (res) => (res.ok ? res.json() : []));
    }
    return typesCache;
  }

  // Kezdő típus a fájlnévből — csak javaslat, a feltöltés előtt átállítható.
  function guessType(file, types) {
    const n = file.name.toLowerCase();
    const find = (re) => types.find((t) => re.test(t.name.toLowerCase()));
    const direct = types.find((t) => n.includes(t.name.toLowerCase()));
    if (direct) return direct.id;
    let hit = null;
    if (/szerz|contract|berleti|bérleti/.test(n)) hit = find(/szerz/);
    else if (/jegyz|átad|atad|leltár|leltar/.test(n)) hit = find(/jegyz|átad/);
    else if (/sz[aá]ml|rezsi|villany|áram|aram|g[aá]z|v[ií]z|k[oö]z[oö]s|mvm|e\.?on|fogaz|f[uű]t[eé]s|internet|telekom|vodafone|digi/.test(n)) hit = find(/rezsi|sz[aá]mla/);
    else if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'heic', 'heif'].includes(ext(file.name))) hit = find(/fot[oó]|k[eé]p/);
    return (hit || find(/^egy[eé]b/))?.id || '';
  }

  const typeOptions = (types, value) => `<option value="">Nincs típus</option>${types
    .map((t) => `<option value="${t.id}"${t.id === value ? ' selected' : ''}>${escapeHtml(t.name)}</option>`).join('')}
    <option value="${MANAGE}">Típusok szerkesztése…</option>`;

  // A „Típusok szerkesztése…” elem kiválasztásakor az előző érték marad, és megnyílik a típusok ablaka; bezárás
  // után az összes típus-választó frissül (az értékük megmarad).
  function bindTypeSelects(root, onChange) {
    root.querySelectorAll('select[data-type]').forEach((sel) => {
      sel.dataset.prev = sel.value;
      sel.addEventListener('change', () => {
        if (sel.value !== MANAGE) { sel.dataset.prev = sel.value; onChange?.(sel); return; }
        sel.value = sel.dataset.prev || '';
        openTypeManager(async () => {
          const types = await loadTypes(true);
          document.querySelectorAll('select[data-type]').forEach((s) => {
            const keep = s.value;
            s.innerHTML = typeOptions(types, keep);
            s.value = types.some((t) => t.id === keep) ? keep : '';
            s.dataset.prev = s.value;
          });
        });
      });
    });
  }

  // ---------- egyszerű saját ablak ----------

  function openModal({ title, sub, body, footer, wide = false, onClose }) {
    const prevFocus = document.activeElement;
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay doc-modal-overlay';
    overlay.innerHTML = `<div class="modal${wide ? ' doc-modal-wide' : ''}" role="dialog" aria-modal="true" aria-labelledby="doc-modal-title">
      <div class="modal-head"><div><h2 id="doc-modal-title"></h2>${sub ? '<p class="doc-modal-sub"></p>' : ''}</div>
        <button class="modal-close" type="button" data-doc-close aria-label="Bezárás">${ICO('x')}</button></div>
      <div class="modal-body">${body}</div>
      <div class="modal-footer">${footer}</div></div>`;
    overlay.querySelector('#doc-modal-title').textContent = title;
    if (sub) overlay.querySelector('.doc-modal-sub').textContent = sub;
    document.body.appendChild(overlay);
    requestAnimationFrame(() => overlay.classList.add('open'));
    let closed = false;
    const close = () => {
      if (closed) return;
      closed = true;
      overlay.classList.remove('open');
      document.removeEventListener('keydown', onKey, true);
      setTimeout(() => overlay.remove(), 200);
      if (prevFocus?.focus) prevFocus.focus();
      onClose?.();
    };
    const onKey = (e) => {
      if (document.querySelector('.ui-menu') || document.querySelector('.ui-dialog-overlay')) return;
      if (overlay !== [...document.querySelectorAll('.doc-modal-overlay')].pop()) return;
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
      if (e.key === 'Tab') {
        const items = [...overlay.querySelectorAll('input,select,textarea,button')].filter((x) => !x.disabled && x.offsetParent);
        const i = items.indexOf(document.activeElement);
        if (e.shiftKey && i <= 0) { e.preventDefault(); items[items.length - 1]?.focus(); }
        else if (!e.shiftKey && i === items.length - 1) { e.preventDefault(); items[0]?.focus(); }
      }
    };
    document.addEventListener('keydown', onKey, true);
    overlay.addEventListener('mousedown', (e) => { if (e.target === overlay) close(); });
    overlay.querySelectorAll('[data-doc-close]').forEach((b) => b.addEventListener('click', close));
    return { overlay, close };
  }

  // Dokumentumtípusok ablaka: átnevezés (mezőből kilépve / Enterre ment), sorrend, törlés, új típus.
  async function openTypeManager(onClose) {
    let types = await loadTypes(true);
    const { overlay } = openModal({
      title: 'Dokumentumtípusok',
      sub: 'A típusok az egész cégnél közösek. Törölt típusú dokumentum típus nélküli lesz.',
      body: `<div class="form-error" data-tm-error></div><div class="dt-list" data-tm-list></div>
        <form class="dt-add" data-tm-add><input type="text" maxlength="60" placeholder="Új típus, pl. Közüzemi átírás" aria-label="Új típus neve">
        <button class="btn btn-secondary" type="submit">${ICO('plus')}Hozzáadás</button></form>`,
      footer: '<button class="btn btn-primary" type="button" data-doc-close>Kész</button>',
      onClose: () => onClose?.(),
    });
    const listEl = overlay.querySelector('[data-tm-list]');
    const errorEl = overlay.querySelector('[data-tm-error]');
    const showError = (msg) => { errorEl.textContent = msg; errorEl.classList.toggle('show', !!msg); };

    const render = () => {
      listEl.innerHTML = types.length ? types.map((t, i) => `
        <div class="dt-row" data-id="${t.id}">
          <div class="dt-move">
            <button class="icon-btn" type="button" data-up="${i}" title="Feljebb" aria-label="Feljebb"${i === 0 ? ' disabled' : ''}>${ICO('chevron-up')}</button>
            <button class="icon-btn" type="button" data-down="${i}" title="Lejjebb" aria-label="Lejjebb"${i === types.length - 1 ? ' disabled' : ''}>${ICO('chevron-down')}</button>
          </div>
          <input type="text" maxlength="60" value="${escapeHtml(t.name)}" aria-label="Típus neve" data-name="${i}">
          <span class="dt-count">${t.documentCount ? `${t.documentCount} dokumentum` : 'nincs használva'}</span>
          <button class="icon-btn danger" type="button" data-del="${i}" title="Törlés" aria-label="Törlés">${ICO('trash')}</button>
        </div>`).join('') : '<div class="panel-empty">Nincs dokumentumtípus. Adj hozzá egyet lent.</div>';
    };
    render();

    listEl.addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.dataset.name) { e.preventDefault(); e.target.blur(); } });
    listEl.addEventListener('change', async (e) => {
      const i = e.target.dataset.name;
      if (i === undefined) return;
      const t = types[i];
      const name = e.target.value.trim();
      if (name === t.name) return;
      const res = await apiFetch(`/api/document-types/${t.id}`, { method: 'PUT', body: JSON.stringify({ name }) });
      if (!res.ok) { showError(errorMessage(res, await readJson(res), 'Nem sikerült átnevezni.')); e.target.value = t.name; return; }
      showError('');
      t.name = name;
    });
    listEl.addEventListener('click', async (e) => {
      const b = e.target.closest('button');
      if (!b) return;
      if (b.dataset.up || b.dataset.down) {
        const i = Number(b.dataset.up ?? b.dataset.down);
        const j = b.dataset.up ? i - 1 : i + 1;
        [types[i], types[j]] = [types[j], types[i]];
        render();
        await apiFetch('/api/document-types/order', { method: 'PUT', body: JSON.stringify(types.map((t) => t.id)) });
      } else if (b.dataset.del) {
        const t = types[Number(b.dataset.del)];
        if (t.documentCount && !(await uiConfirm(`A(z) „${t.name}” típust ${t.documentCount} dokumentum használja; ezek típus nélküliek lesznek.`,
          { title: 'Típus törlése', confirmText: 'Törlés', danger: true }))) return;
        const res = await apiFetch(`/api/document-types/${t.id}`, { method: 'DELETE' });
        if (!res.ok && res.status !== 204) { showError(errorMessage(res, await readJson(res), 'Nem sikerült törölni.')); return; }
        types = types.filter((x) => x !== t);
        render();
      }
    });
    overlay.querySelector('[data-tm-add]').addEventListener('submit', async (e) => {
      e.preventDefault();
      const input = e.target.querySelector('input');
      const name = input.value.trim();
      if (!name) return;
      const res = await apiFetch('/api/document-types', { method: 'POST', body: JSON.stringify({ name }) });
      if (!res.ok) { showError(errorMessage(res, await readJson(res), 'Nem sikerült hozzáadni.')); return; }
      showError('');
      types.push(await res.json());
      input.value = '';
      render();
      input.focus();
    });
  }

  const linkFieldsHtml = (links, values) => links.map((l) => `
    <div class="field"><label for="doc-link-${l.key}">${escapeHtml(l.label)}</label>
      <select id="doc-link-${l.key}" data-link="${l.key}"><option value="">Nincs</option>${l.options
        .map((o) => `<option value="${escapeHtml(o.value)}"${String(values[l.key] ?? '') === String(o.value) ? ' selected' : ''}>${escapeHtml(o.label)}</option>`).join('')}</select>
      ${l.hint ? `<span class="hint">${escapeHtml(l.hint)}</span>` : ''}</div>`).join('');

  const readLinks = (root) => Object.fromEntries([...root.querySelectorAll('[data-link]')].map((s) => [s.dataset.link, s.value || null]));

  // ---------- feltöltés (XHR, hogy legyen folyamatjelző) ----------

  function uploadForm(path, form, onProgress) {
    return new Promise((resolve) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', `${API_BASE}${path}`);
      Object.entries(authHeaders()).forEach(([k, v]) => xhr.setRequestHeader(k, v));
      xhr.upload.addEventListener('progress', (e) => { if (e.lengthComputable) onProgress?.(e.loaded / e.total); });
      xhr.addEventListener('load', () => {
        if (xhr.status === 401) { clearAuth(); location.href = 'login.html'; return; }
        let data = {};
        try { data = JSON.parse(xhr.responseText || '{}'); } catch { /* nem JSON (pl. nginx 413) */ }
        if (xhr.status >= 200 && xhr.status < 300) resolve({ ok: true, data });
        else if (xhr.status === 413) resolve({ ok: false, message: 'A fájl túl nagy (legfeljebb 15 MB).' });
        else resolve({ ok: false, message: data.message || 'Nem sikerült feltölteni.' });
      });
      xhr.addEventListener('error', () => resolve({ ok: false, message: 'Hálózati hiba a feltöltés közben.' }));
      xhr.send(form);
    });
  }

  function checkFile(f) {
    if (!EXTENSIONS.includes(ext(f.name))) return `${f.name}: ez a fájltípus nem tölthető fel.`;
    if (f.size > MAX_BYTES) return `${f.name}: nagyobb, mint 15 MB.`;
    if (!f.size) return `${f.name}: üres fájl.`;
    return null;
  }

  function pickFile(multiple) {
    return new Promise((resolve) => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = ACCEPT;
      input.multiple = multiple;
      input.addEventListener('change', () => resolve([...input.files]));
      input.click();
    });
  }

  // ---------- fájl letöltése / betöltése ----------

  async function fetchContent(doc, version, purpose) {
    const v = version || doc.currentVersion || 1;
    const res = await fetch(`${API_BASE}/api/documents/${doc.id}/content?version=${v}&purpose=${purpose}`, { headers: authHeaders() });
    if (res.status === 401) { clearAuth(); location.href = 'login.html'; throw new Error('Nincs érvényes munkamenet.'); }
    if (!res.ok) throw new Error((await readJson(res)).message || 'Nem sikerült betölteni a dokumentumot.');
    return res.blob();
  }

  async function downloadDocument(doc, version, fileName) {
    try {
      const blob = await fetchContent(doc, version, 'download');
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName || doc.fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      uiAlert(e.message, { title: 'Letöltés sikertelen' });
    }
  }

  let pdfjs = null;
  function loadPdfjs() {
    if (!pdfjs) {
      pdfjs = import(new URL('assets/pdfjs/pdf.min.js', location.href).href).then((m) => {
        m.GlobalWorkerOptions.workerSrc = new URL('assets/pdfjs/pdf.worker.min.js', location.href).href;
        return m;
      });
      pdfjs.catch(() => { pdfjs = null; });
    }
    return pdfjs;
  }

  // ---------- előnézet ----------
  // createPreview(stage, { onInfo }) — a stage görgethető elem; onInfo({ pages, page, zoom, zoomable }) a fejléchez.

  function createPreview(stage, { onInfo } = {}) {
    let zoom = DEFAULT_ZOOM;
    let cleanup = () => {};
    let applyZoom = () => {};
    let info = { pages: 0, page: 0, zoomable: false };
    let token = 0;
    const emit = () => onInfo?.({ ...info, zoom });

    function message(icon, title, text, onDownload) {
      stage.innerHTML = `<div class="dv-message">${ICO(icon)}<h3></h3><p></p>${onDownload ? `<button class="btn btn-primary btn-sm" type="button">${ICO('download')}Letöltés</button>` : ''}</div>`;
      stage.querySelector('h3').textContent = title;
      stage.querySelector('p').textContent = text;
      if (onDownload) stage.querySelector('button').addEventListener('click', onDownload);
    }

    // Dokumentum (adott verziójának) megjelenítése.
    function show(doc, version) {
      const v = version || { number: doc.currentVersion, fileName: doc.fileName, contentType: doc.contentType };
      return showFile({ ...doc, fileName: v.fileName, contentType: v.contentType },
        () => fetchContent(doc, v.number, 'view'), () => downloadDocument(doc, v.number, v.fileName));
    }

    // Tetszőleges fájl megjelenítése: file = { name, fileName, contentType }, load() → Promise<Blob>.
    async function showFile(file, load, download) {
      cleanup();
      cleanup = () => {};
      applyZoom = () => {};
      const my = ++token;
      zoom = DEFAULT_ZOOM;
      info = { pages: 0, page: 0, zoomable: false };
      emit();
      stage.innerHTML = '<div class="dv-loading"><span class="dv-spinner"></span>Betöltés…</div>';
      stage.scrollTop = 0;

      const kind = kindOf(file);
      if (kind === 'sheet' || kind === 'word' || kind === 'file') {
        message(iconOf(file), 'Ehhez a fájltípushoz nincs előnézet', `${typeLabel(file)} fájl. Töltsd le, és nyisd meg a gépeden.`, download);
        return;
      }
      let blob;
      try {
        blob = await load();
      } catch (e) {
        if (my === token) message('triangle-alert', 'Nem sikerült betölteni', e.message);
        return;
      }
      if (my !== token) return;
      if (kind === 'image' || kind === 'image-other') showImage(file, blob, download);
      else if (kind === 'text') showText(blob);
      else await showPdf(blob, download, my);
    }

    function showImage(file, blob, download) {
      const url = URL.createObjectURL(blob);
      stage.innerHTML = '<div class="dv-image"><img alt=""></div>';
      const img = stage.querySelector('img');
      img.alt = file.name;
      applyZoom = () => { img.style.width = `${Math.max(1, Math.round(img.naturalWidth * zoom))}px`; };
      img.addEventListener('load', () => {
        // Nagy (pl. telefonos) fotónál a 60% is túl nagy lenne: akkor az ablakhoz igazítjuk.
        const room = stage.clientWidth - 48;
        if (img.naturalWidth * zoom > room) zoom = Math.max(0.05, Math.floor((room / img.naturalWidth) * 100) / 100);
        info = { pages: 1, page: 1, zoomable: true };
        applyZoom();
        emit();
      });
      img.addEventListener('error', () => {
        message('file-image', 'A böngésző nem tudja megjeleníteni ezt a képet', `${typeLabel(file)} formátum (pl. iPhone-fotó). Töltsd le, és nyisd meg a gépeden.`, download);
        info = { pages: 0, page: 0, zoomable: false };
        emit();
      });
      img.src = url;
      cleanup = () => URL.revokeObjectURL(url);
    }

    async function showText(blob) {
      const text = await blob.text();
      stage.innerHTML = '<pre class="dv-text"></pre>';
      stage.querySelector('pre').textContent = text;
    }

    async function showPdf(blob, download, my) {
      let pdf;
      try {
        const lib = await loadPdfjs();
        pdf = await lib.getDocument({
          data: new Uint8Array(await blob.arrayBuffer()),
          isEvalSupported: false,
          standardFontDataUrl: new URL('assets/pdfjs/standard_fonts/', location.href).href,
        }).promise;
      } catch (e) {
        if (my === token) {
          message('triangle-alert', 'A PDF nem jeleníthető meg', e?.name === 'PasswordException'
            ? 'A fájl jelszóval védett. Töltsd le, és nyisd meg a gépeden.'
            : 'Lehet, hogy sérült a fájl. Letöltve próbáld megnyitni.', download);
        }
        return;
      }
      if (my !== token) { pdf.destroy(); return; }

      const pageCount = pdf.numPages;
      const first = await pdf.getPage(1);
      const base = first.getViewport({ scale: 1 });
      stage.innerHTML = '<div class="dv-pdf"></div>';
      const wrap = stage.querySelector('.dv-pdf');
      const pages = [];
      for (let n = 1; n <= pageCount; n++) {
        const el = document.createElement('div');
        el.className = 'dv-page';
        el.dataset.page = n;
        el.innerHTML = '<canvas></canvas>';
        wrap.appendChild(el);
        pages.push({ n, el, size: n === 1 ? base : null, rendered: 0, task: null });
      }
      info = { pages: pageCount, page: 1, zoomable: true };
      emit();

      const scaleNow = () => zoom * PDF_CSS_SCALE;
      const sizeAll = () => {
        const s = scaleNow();
        pages.forEach((p) => {
          const vp = p.size || base;
          p.el.style.width = `${Math.floor(vp.width * s)}px`;
          p.el.style.height = `${Math.floor(vp.height * s)}px`;
        });
      };

      async function render(p) {
        const s = scaleNow();
        if (p.rendered === s) return;
        p.task?.cancel();
        const page = p.n === 1 ? first : await pdf.getPage(p.n);
        p.size = page.getViewport({ scale: 1 });
        const vp = page.getViewport({ scale: s });
        p.el.style.width = `${Math.floor(vp.width)}px`;
        p.el.style.height = `${Math.floor(vp.height)}px`;
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const canvas = document.createElement('canvas');
        canvas.width = Math.floor(vp.width * dpr);
        canvas.height = Math.floor(vp.height * dpr);
        p.task = page.render({ canvasContext: canvas.getContext('2d'), viewport: vp, transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null });
        try {
          await p.task.promise;
          p.el.replaceChildren(canvas);
          p.rendered = s;
        } catch { /* megszakítva (nagyítás, verzióváltás) */ }
      }

      const visible = new Set();
      const observer = new IntersectionObserver((entries) => {
        entries.forEach((en) => {
          const p = pages[Number(en.target.dataset.page) - 1];
          if (en.isIntersecting) { visible.add(p); render(p); } else visible.delete(p);
        });
      }, { root: stage, rootMargin: '800px 0px' });
      pages.forEach((p) => observer.observe(p.el));

      const onScroll = () => {
        const mid = stage.scrollTop + stage.clientHeight / 3;
        const current = pages.find((p) => p.el.offsetTop + p.el.offsetHeight > mid) || pages[pages.length - 1];
        if (current.n !== info.page) { info.page = current.n; emit(); }
      };
      stage.addEventListener('scroll', onScroll, { passive: true });

      applyZoom = () => {
        const anchor = stage.scrollHeight ? stage.scrollTop / stage.scrollHeight : 0;
        sizeAll();
        stage.scrollTop = anchor * stage.scrollHeight;
        visible.forEach(render);
      };
      sizeAll();
      cleanup = () => {
        observer.disconnect();
        stage.removeEventListener('scroll', onScroll);
        pages.forEach((p) => p.task?.cancel());
        pdf.destroy();
      };
    }

    function setZoom(z) {
      zoom = Math.min(4, Math.max(0.1, Math.round(z * 100) / 100));
      applyZoom();
      emit();
    }

    return {
      show,
      showFile,
      setZoom,
      zoomIn: () => setZoom(zoom >= 1 ? zoom + 0.25 : zoom + 0.1),
      zoomOut: () => setZoom(zoom > 1 ? zoom - 0.25 : zoom - 0.1),
      reset: () => setZoom(DEFAULT_ZOOM),
      destroy: () => { token++; cleanup(); },
    };
  }

  // ---------- új verzió (a dokumentum adatlapjáról) ----------

  async function uploadNewVersion(doc, onProgress) {
    const [file] = await pickFile(false);
    if (!file) return null;
    const problem = checkFile(file);
    if (problem) { uiAlert(problem, { title: 'Nem tölthető fel' }); return null; }
    const form = new FormData();
    form.append('file', file);
    const result = await uploadForm(`/api/documents/${doc.id}/versions`, form, onProgress);
    if (!result.ok) { uiAlert(result.message, { title: 'Feltöltés sikertelen' }); return null; }
    return result.data;
  }

  window.Docs = {
    ICO, kindOf, iconOf, typeLabel, formatSize, formatDateTime, loadTypes, typeOptions, bindTypeSelects, openTypeManager,
    createPreview, downloadDocument, uploadNewVersion, DEFAULT_ZOOM, MANAGE,
  };

  // ---------- az adatlap Dokumentumok füle ----------

  window.mountDocuments = function mountDocuments({ scope, container, count, uploadButton, links = async () => [] }) {
    const host = typeof container === 'string' ? document.querySelector(container) : container;
    const countEl = typeof count === 'string' ? document.querySelector(count) : count;
    const uploadBtn = typeof uploadButton === 'string' ? document.querySelector(uploadButton) : uploadButton;
    const scopeKey = Object.keys(scope)[0];
    const scopeName = { propertyId: 'property', tenantId: 'tenant', ownerId: 'owner' }[scopeKey];
    const query = `${scopeKey}=${encodeURIComponent(scope[scopeKey])}`;
    let docs = [];
    let filter = 'all';

    host.classList.add('doc-pane');
    host.innerHTML = `
      <div class="doc-toolbar" hidden><div class="doc-filters" role="group" aria-label="Dokumentumtípus"></div></div>
      <div class="doc-list"><div class="panel-empty">Betöltés…</div></div>
      <div class="doc-dropzone" aria-hidden="true"><div>${ICO('upload')}<b>Engedd el a fájlokat a feltöltéshez</b></div></div>`;
    const listEl = host.querySelector('.doc-list');
    const toolbar = host.querySelector('.doc-toolbar');

    const keyOf = (d) => d.documentTypeId || 'none';
    const visibleDocs = () => (filter === 'all' ? docs : docs.filter((d) => keyOf(d) === filter));
    const detailUrl = (d) => `document-detail.html?id=${encodeURIComponent(d.id)}&from=${scopeName}:${encodeURIComponent(scope[scopeKey])}`;

    function linkChips(d) {
      const chips = [];
      if (d.propertyId && scopeKey !== 'propertyId') chips.push(`<a class="doc-chip" href="property-detail.html?id=${encodeURIComponent(d.propertyId)}">${ICO('house')}${escapeHtml(d.propertyName)}</a>`);
      if (d.tenantId && scopeKey !== 'tenantId') chips.push(`<a class="doc-chip" href="tenant-detail.html?id=${encodeURIComponent(d.tenantId)}">${ICO('user')}${escapeHtml(d.tenantName)}</a>`);
      if (d.ownerId && scopeKey !== 'ownerId') chips.push(`<a class="doc-chip" href="owner-detail.html?id=${encodeURIComponent(d.ownerId)}">${ICO('key-round')}${escapeHtml(d.ownerName)}</a>`);
      return chips.join('') || '<span class="dim">–</span>';
    }

    function render() {
      if (countEl) countEl.textContent = docs.length || '';
      const groups = [];
      docs.forEach((d) => {
        const g = groups.find((x) => x.key === keyOf(d));
        if (g) g.n++; else groups.push({ key: keyOf(d), label: d.documentTypeName || 'Típus nélkül', n: 1 });
      });
      groups.sort((a, b) => (a.key === 'none') - (b.key === 'none') || a.label.localeCompare(b.label, 'hu'));
      if (filter !== 'all' && !groups.some((g) => g.key === filter)) filter = 'all';
      toolbar.hidden = groups.length < 2;
      host.querySelector('.doc-filters').innerHTML = groups.length > 1
        ? [{ key: 'all', label: 'Mind', n: docs.length }, ...groups]
          .map((g) => `<button type="button" class="doc-filter${g.key === filter ? ' active' : ''}" data-filter="${g.key}" aria-pressed="${g.key === filter}">${escapeHtml(g.label)} <span>${g.n}</span></button>`).join('')
        : '';

      if (!docs.length) {
        listEl.innerHTML = `<div class="doc-empty">
          <div class="doc-empty-ico">${ICO('folder-open')}</div>
          <p><b>Még nincs feltöltött dokumentum.</b><br>Szerződés, szkennelt rezsi számla, átadás-átvételi jegyzőkönyv, fotó… Húzd ide a fájlokat, vagy válaszd ki őket.</p>
          <button class="btn btn-secondary btn-sm" type="button" data-doc-upload>${ICO('upload')}Fájlok kiválasztása</button>
          <span class="hint">PDF, kép, Word, Excel vagy szöveg, fájlonként legfeljebb 15 MB.</span></div>`;
        return;
      }
      const list = visibleDocs();
      listEl.innerHTML = `<table class="doc-table">
        <thead><tr><th>Név</th><th>Típus</th><th>Kapcsolódik</th><th>Feltöltve</th><th class="num">Méret</th><th></th></tr></thead>
        <tbody>${list.map((d, i) => `
          <tr>
            <td class="doc-name-cell">
              <a class="doc-open" href="${detailUrl(d)}">
                <span class="doc-ico k-${kindOf(d)}">${ICO(iconOf(d))}<i>${escapeHtml(typeLabel(d))}</i></span>
                <span class="doc-name"><b>${escapeHtml(d.name)}</b><small>${escapeHtml(d.note || d.fileName)}${d.currentVersion > 1 ? ` · ${d.currentVersion}. verzió` : ''}</small></span>
              </a>
            </td>
            <td>${d.documentTypeName ? `<span class="doc-cat">${escapeHtml(d.documentTypeName)}</span>` : '<span class="dim">–</span>'}</td>
            <td class="doc-links">${linkChips(d)}</td>
            <td class="doc-when">${formatDate(d.uploadedAt)}${d.uploadedByName ? `<small>${escapeHtml(d.uploadedByName)}</small>` : ''}</td>
            <td class="num">${formatSize(d.sizeBytes)}</td>
            <td class="num doc-actions">
              <button class="icon-btn" type="button" data-download="${i}" title="Letöltés" aria-label="Letöltés">${ICO('download')}</button>
              <button class="icon-btn danger" type="button" data-delete="${i}" title="Törlés" aria-label="Törlés">${ICO('trash')}</button>
            </td>
          </tr>`).join('')}</tbody></table>`;
    }

    async function load() {
      const res = await apiFetch(`/api/documents?${query}`);
      if (!res.ok) {
        listEl.innerHTML = `<div class="panel-empty">${escapeHtml(errorMessage(res, await readJson(res), 'Nem sikerült betölteni a dokumentumokat.'))}</div>`;
        return;
      }
      docs = await res.json();
      render();
    }

    // ----- feltöltés -----

    async function startUpload(fileList) {
      const files = [...fileList];
      if (!files.length) return;
      const [linkDefs, types] = await Promise.all([links(), loadTypes()]);
      const rejected = [];
      const items = files.filter((f) => {
        const problem = checkFile(f);
        if (problem) rejected.push(problem);
        return !problem;
      }).map((file) => ({ file, name: file.name.replace(/\.[^.]+$/, ''), typeId: guessType(file, types), done: false }));

      if (!items.length) {
        uiAlert(rejected.join('\n'), { title: 'Nem tölthető fel' });
        return;
      }

      const rowsHtml = () => items.map((it, i) => {
        const pseudo = { contentType: typeFromName(it.file.name) };
        return `
        <div class="doc-up-row" data-row="${i}">
          <span class="doc-ico k-${kindOf(pseudo)}">${ICO(iconOf(pseudo))}<i>${escapeHtml(ext(it.file.name).toUpperCase())}</i></span>
          <div class="doc-up-main">
            <input type="text" value="${escapeHtml(it.name)}" maxlength="200" aria-label="Név" data-name="${i}">
            <div class="doc-up-meta"><span>${escapeHtml(it.file.name)}, ${formatSize(it.file.size)}</span><span class="doc-up-state" data-state="${i}"></span></div>
            <div class="doc-up-bar"><span data-bar="${i}"></span></div>
          </div>
          <select aria-label="Dokumentumtípus" data-type data-cat="${i}">${typeOptions(types, it.typeId)}</select>
          <button class="icon-btn" type="button" data-remove="${i}" title="Kihagyás" aria-label="Kihagyás">${ICO('x')}</button>
        </div>`;
      }).join('');

      const values = Object.fromEntries(linkDefs.map((l) => [l.key, l.value ?? '']));
      const { overlay, close } = openModal({
        title: items.length > 1 ? `${items.length} dokumentum feltöltése` : 'Dokumentum feltöltése',
        wide: true,
        body: `<div class="form-error" data-up-error></div>
          ${rejected.length ? `<div class="doc-up-skipped">Kimarad: ${rejected.map(escapeHtml).join(' ')}</div>` : ''}
          <div class="doc-up-rows">${rowsHtml()}</div>
          ${linkDefs.length ? `<div class="field-row doc-up-links">${linkFieldsHtml(linkDefs, values)}</div>` : ''}
          <div class="field"><label for="doc-up-note">Megjegyzés <span class="dim">(nem kötelező)</span></label>
            <textarea id="doc-up-note" rows="2" maxlength="1000" placeholder="pl. 2026. évi bérleti szerződés, 2. oldalon az aláírás"></textarea></div>`,
        footer: '<button class="btn btn-secondary" type="button" data-doc-close>Mégse</button><button class="btn btn-primary" type="button" data-up-go>Feltöltés</button>',
      });
      bindTypeSelects(overlay);
      const goBtn = overlay.querySelector('[data-up-go]');
      const errorEl = overlay.querySelector('[data-up-error]');
      const pending = () => items.filter((it) => !it.done && !it.removed);
      const refreshGo = () => {
        const n = pending().length;
        goBtn.textContent = n > 1 ? `Feltöltés (${n})` : 'Feltöltés';
        goBtn.disabled = n === 0;
      };
      refreshGo();
      overlay.querySelector('[data-name="0"]')?.focus();

      overlay.addEventListener('input', (e) => { if (e.target.dataset.name) items[e.target.dataset.name].name = e.target.value; });
      overlay.addEventListener('click', (e) => {
        const rm = e.target.closest('[data-remove]');
        if (!rm) return;
        const i = Number(rm.dataset.remove);
        items[i].removed = true;
        overlay.querySelector(`[data-row="${i}"]`).remove();
        if (!pending().length && !items.some((it) => it.done)) close();
        refreshGo();
      });

      let busy = false;
      goBtn.addEventListener('click', async () => {
        if (busy) return;
        const todo = pending();
        if (todo.some((it) => !it.name.trim())) {
          errorEl.textContent = 'Minden dokumentumnak adj nevet.';
          errorEl.classList.add('show');
          return;
        }
        errorEl.classList.remove('show');
        busy = true;
        goBtn.disabled = true;
        goBtn.textContent = 'Feltöltés…';
        const linkValues = readLinks(overlay);
        const note = overlay.querySelector('#doc-up-note').value.trim();
        let failed = 0;
        for (const it of todo) {
          const i = items.indexOf(it);
          const row = overlay.querySelector(`[data-row="${i}"]`);
          const bar = overlay.querySelector(`[data-bar="${i}"]`);
          const state = overlay.querySelector(`[data-state="${i}"]`);
          const typeId = overlay.querySelector(`[data-cat="${i}"]`).value;
          row.classList.remove('failed');
          row.classList.add('busy');
          state.textContent = 'Feltöltés…';
          const form = new FormData();
          form.append('file', it.file);
          form.append('name', it.name.trim());
          if (typeId && typeId !== MANAGE) form.append('documentTypeId', typeId);
          if (note) form.append('note', note);
          form.append(scopeKey, scope[scopeKey]);
          Object.entries(linkValues).forEach(([k, v]) => { if (v && k !== scopeKey) form.append(k, v); });
          const result = await uploadForm('/api/documents', form, (p) => { bar.style.width = `${Math.round(p * 100)}%`; });
          row.classList.remove('busy');
          if (result.ok) {
            it.done = true;
            row.classList.add('done');
            bar.style.width = '100%';
            state.textContent = 'Feltöltve';
            row.querySelectorAll('input,select').forEach((x) => { x.disabled = true; });
            row.querySelector('[data-remove]').hidden = true;
          } else {
            failed++;
            row.classList.add('failed');
            bar.style.width = '0';
            state.textContent = result.message;
          }
        }
        busy = false;
        await load();
        if (!failed) { close(); return; }
        errorEl.textContent = failed === todo.length ? 'A feltöltés nem sikerült.' : 'Néhány fájlt nem sikerült feltölteni. A sikereseket elmentettük.';
        errorEl.classList.add('show');
        refreshGo();
        goBtn.textContent = 'Újrapróbálás';
      });
    }

    async function remove(doc) {
      const others = [doc.propertyId && scopeKey !== 'propertyId' && 'az ingatlannál', doc.tenantId && scopeKey !== 'tenantId' && 'a bérlőnél',
        doc.ownerId && scopeKey !== 'ownerId' && 'a tulajdonosnál'].filter(Boolean);
      const ok = await uiConfirm(`A(z) „${doc.name}” dokumentum minden verziójával véglegesen törlődik${others.length ? `, ${others.join(' és ')} is` : ''}. Ez nem vonható vissza.`,
        { title: 'Dokumentum törlése', confirmText: 'Törlés', danger: true });
      if (!ok) return;
      const res = await apiFetch(`/api/documents/${doc.id}`, { method: 'DELETE' });
      if (!res.ok && res.status !== 204) {
        uiAlert(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'), { title: 'Törlés sikertelen' });
        return;
      }
      load();
    }

    // ----- események -----

    const choose = async () => startUpload(await pickFile(true));
    uploadBtn?.addEventListener('click', choose);
    host.addEventListener('click', (e) => {
      const b = e.target.closest('button');
      if (!b || !host.contains(b)) return;
      const list = visibleDocs();
      if (b.hasAttribute('data-doc-upload')) choose();
      else if (b.dataset.filter) { filter = b.dataset.filter; render(); }
      else if (b.dataset.download) downloadDocument(list[Number(b.dataset.download)]);
      else if (b.dataset.delete) remove(list[Number(b.dataset.delete)]);
    });

    // Behúzás: a fájl a fül bármely részére ejthető.
    let dragDepth = 0;
    const hasFiles = (e) => [...(e.dataTransfer?.types || [])].includes('Files');
    host.addEventListener('dragenter', (e) => { if (!hasFiles(e)) return; e.preventDefault(); dragDepth++; host.classList.add('dragging'); });
    host.addEventListener('dragover', (e) => { if (!hasFiles(e)) return; e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; });
    host.addEventListener('dragleave', () => { dragDepth = Math.max(0, dragDepth - 1); if (!dragDepth) host.classList.remove('dragging'); });
    host.addEventListener('drop', (e) => {
      if (!hasFiles(e)) return;
      e.preventDefault();
      dragDepth = 0;
      host.classList.remove('dragging');
      startUpload(e.dataTransfer.files);
    });

    load();
    return { reload: load };
  };
})();
