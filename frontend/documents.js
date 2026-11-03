// Dokumentumok fül az ingatlan, bérlő és tulajdonos adatlapján: lista kategória-szűrővel, feltöltés
// (gombbal vagy behúzással, több fájl egyszerre, folyamatjelzővel), szerkesztés, törlés és előnézet.
// Az előnézet a saját ablakunkban jelenik meg: PDF-nél a PDF.js rajzolja ki az oldalakat (saját szerverről,
// assets/pdfjs), képnél a kép, szövegnél a szöveg; a többi típus letölthető.
//
// Használat: mountDocuments({ scope: { propertyId } | { tenantId } | { ownerId },
//   container, count, links: async () => [{ key: 'tenantId', label: 'Bérlő', options: [{ value, label }], value }] })
(function () {
  const ICO = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const MAX_BYTES = 15 * 1024 * 1024;
  const CATEGORIES = [
    { value: 'Contract', label: 'Szerződés' },
    { value: 'UtilityBill', label: 'Rezsi számla' },
    { value: 'Handover', label: 'Jegyzőkönyv' },
    { value: 'Photo', label: 'Fotó' },
    { value: 'Other', label: 'Egyéb' },
  ];
  const CATEGORY_LABEL = Object.fromEntries(CATEGORIES.map((c) => [c.value, c.label]));
  const EXTENSIONS = ['pdf', 'jpg', 'jpeg', 'png', 'gif', 'webp', 'heic', 'heif', 'doc', 'docx', 'xls', 'xlsx', 'odt', 'ods', 'txt', 'csv'];
  const ACCEPT = EXTENSIONS.map((e) => `.${e}`).join(',');

  const ext = (name) => (String(name).match(/\.([^.]+)$/)?.[1] || '').toLowerCase();
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
  // Kiválasztott (még fel nem töltött) fájl típusa a kiterjesztésből — a böngésző által adott MIME-típus gyakran üres.
  const typeFromName = (name) => ({ pdf: 'application/pdf', jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
    heic: 'image/heic', heif: 'image/heif', doc: 'application/msword', docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    odt: 'application/vnd.oasis.opendocument.text', xls: 'application/vnd.ms-excel', xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    ods: 'application/vnd.oasis.opendocument.spreadsheet', txt: 'text/plain', csv: 'text/csv' }[ext(name)] || '');
  const iconOf = (doc) => (doc.contentType === 'text/csv' ? 'file-csv'
    : { pdf: 'file-pdf', image: 'file-image', 'image-other': 'file-image', text: 'file-text', sheet: 'file-excel', word: 'file-word' }[kindOf(doc)] || 'file');
  const typeLabel = (doc) => (ext(doc.fileName) || 'fájl').toUpperCase();

  function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB`;
  }

  // Kezdő kategória a fájlnévből — csak javaslat, a feltöltés előtt átállítható.
  function guessCategory(file) {
    const n = file.name.toLowerCase();
    if (/szerz|contract|berleti|bérleti/.test(n)) return 'Contract';
    if (/jegyz|átad|atad|leltár|leltar/.test(n)) return 'Handover';
    if (/sz[aá]ml|rezsi|villany|áram|aram|g[aá]z|v[ií]z|k[oö]z[oö]s|mvm|e\.?on|fogaz|f[uű]t[eé]s|internet|telekom|vodafone|digi/.test(n)) return 'UtilityBill';
    if (/^image\//.test(file.type) || ['jpg', 'jpeg', 'png', 'gif', 'webp', 'heic', 'heif'].includes(ext(file.name))) return 'Photo';
    return 'Other';
  }

  const categorySelect = (value, attrs = '') => `<select ${attrs}>${CATEGORIES
    .map((c) => `<option value="${c.value}"${c.value === value ? ' selected' : ''}>${c.label}</option>`).join('')}</select>`;

  // ---------- egyszerű saját ablak (feltöltés, szerkesztés) ----------

  function openModal({ title, body, footer, wide = false, onClose }) {
    const prevFocus = document.activeElement;
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay doc-modal-overlay';
    overlay.innerHTML = `<div class="modal${wide ? ' doc-modal-wide' : ''}" role="dialog" aria-modal="true" aria-labelledby="doc-modal-title">
      <div class="modal-head"><div><h2 id="doc-modal-title"></h2></div>
        <button class="modal-close" type="button" data-doc-close aria-label="Bezárás">${ICO('x')}</button></div>
      <div class="modal-body">${body}</div>
      <div class="modal-footer">${footer}</div></div>`;
    overlay.querySelector('#doc-modal-title').textContent = title;
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
      if (e.key === 'Escape' && !document.querySelector('.ui-menu')) { e.preventDefault(); e.stopPropagation(); close(); }
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

  const linkFieldsHtml = (links, values) => links.map((l) => `
    <div class="field"><label for="doc-link-${l.key}">${escapeHtml(l.label)}</label>
      <select id="doc-link-${l.key}" data-link="${l.key}"><option value="">Nincs</option>${l.options
        .map((o) => `<option value="${escapeHtml(o.value)}"${String(values[l.key] ?? '') === String(o.value) ? ' selected' : ''}>${escapeHtml(o.label)}</option>`).join('')}</select>
      ${l.hint ? `<span class="hint">${escapeHtml(l.hint)}</span>` : ''}</div>`).join('');

  const readLinks = (root) => Object.fromEntries([...root.querySelectorAll('[data-link]')].map((s) => [s.dataset.link, s.value || null]));

  // ---------- feltöltés (XHR, hogy legyen folyamatjelző) ----------

  function uploadFile(form, onProgress) {
    return new Promise((resolve) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', `${API_BASE}/api/documents`);
      Object.entries(authHeaders()).forEach(([k, v]) => xhr.setRequestHeader(k, v));
      xhr.upload.addEventListener('progress', (e) => { if (e.lengthComputable) onProgress(e.loaded / e.total); });
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

  // ---------- fájl betöltése az előnézethez (munkameneten belül gyorsítótárazva) ----------

  const blobCache = new Map();
  async function fetchBlob(doc) {
    if (blobCache.has(doc.id)) return blobCache.get(doc.id);
    const res = await fetch(`${API_BASE}/api/documents/${doc.id}/content`, { headers: authHeaders() });
    if (res.status === 401) { clearAuth(); location.href = 'login.html'; throw new Error('Nincs érvényes munkamenet.'); }
    if (!res.ok) throw new Error((await readJson(res)).message || 'Nem sikerült betölteni a dokumentumot.');
    const blob = await res.blob();
    if (blobCache.size > 12) blobCache.delete(blobCache.keys().next().value);
    blobCache.set(doc.id, blob);
    return blob;
  }

  async function downloadDoc(doc) {
    try {
      const blob = await fetchBlob(doc);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = doc.fileName;
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

  function openViewer(list, startIndex) {
    let index = startIndex;
    let zoom = 1;
    let cleanup = () => {};
    let pageCount = 0;
    const prevFocus = document.activeElement;

    const root = document.createElement('div');
    root.className = 'doc-viewer';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-label', 'Dokumentum előnézete');
    root.innerHTML = `
      <div class="dv-bar">
        <div class="dv-title"><span class="dv-ico"></span><div class="dv-title-text"><h2></h2><p></p></div></div>
        <div class="dv-tools">
          <span class="dv-pages" hidden></span>
          <div class="dv-zoom" hidden>
            <button class="icon-btn" type="button" data-zoom="-1" title="Kicsinyítés" aria-label="Kicsinyítés">${ICO('zoom-out')}</button>
            <button class="dv-zoom-val" type="button" data-zoom="0" title="Igazítás az ablakhoz">100%</button>
            <button class="icon-btn" type="button" data-zoom="1" title="Nagyítás" aria-label="Nagyítás">${ICO('zoom-in')}</button>
          </div>
          <span class="dv-sep"></span>
          <span class="dv-pos"></span>
          <button class="icon-btn" type="button" data-nav="-1" title="Előző dokumentum (←)" aria-label="Előző dokumentum">${ICO('chevron-left')}</button>
          <button class="icon-btn" type="button" data-nav="1" title="Következő dokumentum (→)" aria-label="Következő dokumentum">${ICO('chevron-right')}</button>
          <button class="btn btn-secondary btn-sm" type="button" data-download>${ICO('download')}Letöltés</button>
          <button class="icon-btn dv-close" type="button" data-close title="Bezárás (Esc)" aria-label="Bezárás">${ICO('x')}</button>
        </div>
      </div>
      <div class="dv-stage" tabindex="-1"></div>`;
    document.body.appendChild(root);
    document.documentElement.classList.add('doc-viewer-open');
    requestAnimationFrame(() => root.classList.add('open'));

    const stage = root.querySelector('.dv-stage');
    const pagesEl = root.querySelector('.dv-pages');
    const zoomBox = root.querySelector('.dv-zoom');
    const zoomVal = root.querySelector('.dv-zoom-val');
    let applyZoom = () => {};

    function close() {
      cleanup();
      document.removeEventListener('keydown', onKey, true);
      root.classList.remove('open');
      document.documentElement.classList.remove('doc-viewer-open');
      setTimeout(() => root.remove(), 180);
      if (prevFocus?.focus) prevFocus.focus();
    }

    function setZoom(z) {
      zoom = Math.min(4, Math.max(0.25, Math.round(z * 100) / 100));
      zoomVal.textContent = `${Math.round(zoom * 100)}%`;
      applyZoom();
    }

    function message(icon, title, text, withDownload) {
      stage.innerHTML = `<div class="dv-message">${ICO(icon)}<h3></h3><p></p>${withDownload ? `<button class="btn btn-primary btn-sm" type="button" data-download>${ICO('download')}Letöltés</button>` : ''}</div>`;
      stage.querySelector('h3').textContent = title;
      stage.querySelector('p').textContent = text;
    }

    async function show() {
      cleanup();
      cleanup = () => {};
      const doc = list[index];
      zoom = 1;
      zoomVal.textContent = '100%';
      pagesEl.hidden = true;
      zoomBox.hidden = true;
      applyZoom = () => {};
      root.querySelector('.dv-ico').innerHTML = ICO(iconOf(doc));
      root.querySelector('.dv-title h2').textContent = doc.name;
      const meta = [CATEGORY_LABEL[doc.category] || doc.category, `${typeLabel(doc)}, ${formatSize(doc.sizeBytes)}`,
        `feltöltve ${formatDate(doc.uploadedAt)}${doc.uploadedByName ? `, ${doc.uploadedByName}` : ''}`];
      root.querySelector('.dv-title p').textContent = meta.join(' · ');
      root.querySelector('.dv-pos').textContent = list.length > 1 ? `${index + 1} / ${list.length}` : '';
      root.querySelectorAll('[data-nav]').forEach((b) => { b.hidden = list.length < 2; });
      stage.innerHTML = '<div class="dv-loading"><span class="dv-spinner"></span>Betöltés…</div>';
      stage.scrollTop = 0;

      const kind = kindOf(doc);
      if (kind === 'sheet' || kind === 'word' || kind === 'file') {
        message(iconOf(doc), 'Ehhez a fájltípushoz nincs előnézet', `${typeLabel(doc)} fájl. Töltsd le, és nyisd meg a gépeden.`, true);
        return;
      }
      let blob;
      try {
        blob = await fetchBlob(doc);
      } catch (e) {
        if (list[index] !== doc) return;
        message('triangle-alert', 'Nem sikerült betölteni', e.message, false);
        return;
      }
      if (list[index] !== doc) return; // közben továbblapoztak

      if (kind === 'image' || kind === 'image-other') showImage(doc, blob);
      else if (kind === 'text') showText(blob);
      else if (kind === 'pdf') await showPdf(doc, blob);
    }

    function showImage(doc, blob) {
      const url = URL.createObjectURL(blob);
      stage.innerHTML = '<div class="dv-image"><img alt=""></div>';
      const img = stage.querySelector('img');
      img.alt = doc.name;
      zoomBox.hidden = false;
      applyZoom = () => {
        const box = stage.getBoundingClientRect();
        const fit = Math.min(1, (box.width - 48) / img.naturalWidth, (box.height - 48) / img.naturalHeight);
        img.style.width = `${Math.max(1, Math.round(img.naturalWidth * fit * zoom))}px`;
      };
      img.addEventListener('load', applyZoom);
      img.addEventListener('error', () => {
        message('image', 'A böngésző nem tudja megjeleníteni ezt a képet', `${typeLabel(doc)} formátum (pl. iPhone-fotó). Töltsd le, és nyisd meg a gépeden.`, true);
        zoomBox.hidden = true;
      });
      img.src = url;
      cleanup = () => URL.revokeObjectURL(url);
    }

    async function showText(blob) {
      const text = await blob.text();
      stage.innerHTML = '<pre class="dv-text"></pre>';
      stage.querySelector('pre').textContent = text;
    }

    async function showPdf(doc, blob) {
      let lib;
      let pdf;
      try {
        lib = await loadPdfjs();
        pdf = await lib.getDocument({
          data: new Uint8Array(await blob.arrayBuffer()),
          isEvalSupported: false,
          standardFontDataUrl: new URL('assets/pdfjs/standard_fonts/', location.href).href,
        }).promise;
      } catch (e) {
        if (list[index] !== doc) return;
        message('triangle-alert', 'A PDF nem jeleníthető meg', e?.name === 'PasswordException'
          ? 'A fájl jelszóval védett. Töltsd le, és nyisd meg a gépeden.'
          : 'Lehet, hogy sérült a fájl. Letöltve próbáld megnyitni.', true);
        return;
      }
      if (list[index] !== doc) { pdf.destroy(); return; }

      pageCount = pdf.numPages;
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
      pagesEl.hidden = false;
      zoomBox.hidden = false;
      pagesEl.textContent = `1 / ${pageCount} oldal`;

      // Alapméret: az ablak szélességéhez igazítva (legfeljebb ~1000 px széles oldal), erre jön a nagyítás.
      const fitScale = () => Math.min(1000, stage.clientWidth - 48) / base.width;
      const scaleNow = () => fitScale() * zoom;

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
        } catch { /* megszakítva (nagyítás, lapozás) */ }
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
        pagesEl.textContent = `${current.n} / ${pageCount} oldal`;
      };
      stage.addEventListener('scroll', onScroll, { passive: true });

      applyZoom = () => {
        const anchor = stage.scrollHeight ? stage.scrollTop / stage.scrollHeight : 0;
        sizeAll();
        stage.scrollTop = anchor * stage.scrollHeight;
        visible.forEach(render);
      };
      sizeAll();

      let resizeTimer;
      const onResize = () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(applyZoom, 150); };
      window.addEventListener('resize', onResize);
      cleanup = () => {
        observer.disconnect();
        stage.removeEventListener('scroll', onScroll);
        window.removeEventListener('resize', onResize);
        pages.forEach((p) => p.task?.cancel());
        pdf.destroy();
      };
    }

    function onKey(e) {
      if (document.querySelector('.ui-dialog-overlay')) return;
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
      else if (e.key === 'ArrowLeft' && list.length > 1) { e.preventDefault(); index = (index - 1 + list.length) % list.length; show(); }
      else if (e.key === 'ArrowRight' && list.length > 1) { e.preventDefault(); index = (index + 1) % list.length; show(); }
      else if ((e.key === '+' || e.key === '=') && !zoomBox.hidden) { e.preventDefault(); setZoom(zoom * 1.25); }
      else if (e.key === '-' && !zoomBox.hidden) { e.preventDefault(); setZoom(zoom / 1.25); }
    }
    document.addEventListener('keydown', onKey, true);

    root.addEventListener('click', (e) => {
      const t = e.target.closest('button');
      if (!t) return;
      if (t.hasAttribute('data-close')) close();
      else if (t.hasAttribute('data-download')) downloadDoc(list[index]);
      else if (t.dataset.nav) { index = (index + Number(t.dataset.nav) + list.length) % list.length; show(); }
      else if (t.dataset.zoom) {
        const d = Number(t.dataset.zoom);
        setZoom(d === 0 ? 1 : d > 0 ? zoom * 1.25 : zoom / 1.25);
      }
    });
    root.querySelector('[data-close]').focus();
    show();
  }

  // ---------- az adatlap Dokumentumok füle ----------

  window.mountDocuments = function mountDocuments({ scope, container, count, links = async () => [] }) {
    const host = typeof container === 'string' ? document.querySelector(container) : container;
    const countEl = typeof count === 'string' ? document.querySelector(count) : count;
    const scopeKey = Object.keys(scope)[0];
    const query = `${scopeKey}=${encodeURIComponent(scope[scopeKey])}`;
    let docs = [];
    let filter = 'all';
    let loaded = false;

    host.classList.add('doc-pane');
    host.innerHTML = `
      <div class="doc-toolbar">
        <div class="doc-filters" role="group" aria-label="Kategória"></div>
        <button class="btn btn-primary btn-sm" type="button" data-doc-upload>${ICO('upload')}Feltöltés</button>
        <input type="file" multiple hidden accept="${ACCEPT}" data-doc-input>
      </div>
      <div class="doc-list"><div class="panel-empty">Betöltés…</div></div>
      <div class="doc-dropzone" aria-hidden="true"><div>${ICO('upload')}<b>Engedd el a fájlokat a feltöltéshez</b></div></div>`;
    const input = host.querySelector('[data-doc-input]');
    const listEl = host.querySelector('.doc-list');

    const visibleDocs = () => (filter === 'all' ? docs : docs.filter((d) => d.category === filter));

    function linkChips(d) {
      const chips = [];
      if (d.propertyId && scopeKey !== 'propertyId') chips.push(`<a class="doc-chip" href="property-detail.html?id=${encodeURIComponent(d.propertyId)}">${ICO('house')}${escapeHtml(d.propertyName)}</a>`);
      if (d.tenantId && scopeKey !== 'tenantId') chips.push(`<a class="doc-chip" href="tenant-detail.html?id=${encodeURIComponent(d.tenantId)}">${ICO('user')}${escapeHtml(d.tenantName)}</a>`);
      if (d.ownerId && scopeKey !== 'ownerId') chips.push(`<a class="doc-chip" href="owner-detail.html?id=${encodeURIComponent(d.ownerId)}">${ICO('key-round')}${escapeHtml(d.ownerName)}</a>`);
      return chips.join('') || '<span class="dim">–</span>';
    }

    function render() {
      if (countEl) countEl.textContent = docs.length || '';
      const present = CATEGORIES.filter((c) => docs.some((d) => d.category === c.value));
      if (filter !== 'all' && !present.some((c) => c.value === filter)) filter = 'all';
      const filters = host.querySelector('.doc-filters');
      filters.innerHTML = docs.length && present.length > 1
        ? [{ value: 'all', label: 'Mind', n: docs.length }, ...present.map((c) => ({ ...c, n: docs.filter((d) => d.category === c.value).length }))]
          .map((c) => `<button type="button" class="doc-filter${c.value === filter ? ' active' : ''}" data-filter="${c.value}" aria-pressed="${c.value === filter}">${c.label} <span>${c.n}</span></button>`).join('')
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
        <thead><tr><th>Név</th><th>Kategória</th><th>Kapcsolódik</th><th>Feltöltve</th><th class="num">Méret</th><th></th></tr></thead>
        <tbody>${list.map((d, i) => `
          <tr data-i="${i}">
            <td class="doc-name-cell">
              <button type="button" class="doc-open" data-open="${i}" title="Előnézet">
                <span class="doc-ico k-${kindOf(d)}">${ICO(iconOf(d))}<i>${escapeHtml(typeLabel(d))}</i></span>
                <span class="doc-name"><b>${escapeHtml(d.name)}</b><small>${escapeHtml(d.note || d.fileName)}</small></span>
              </button>
            </td>
            <td><span class="doc-cat c-${escapeHtml(d.category)}">${escapeHtml(CATEGORY_LABEL[d.category] || d.category)}</span></td>
            <td class="doc-links">${linkChips(d)}</td>
            <td class="doc-when">${formatDate(d.uploadedAt)}${d.uploadedByName ? `<small>${escapeHtml(d.uploadedByName)}</small>` : ''}</td>
            <td class="num">${formatSize(d.sizeBytes)}</td>
            <td class="num doc-actions">
              <button class="icon-btn" type="button" data-download="${i}" title="Letöltés" aria-label="Letöltés">${ICO('download')}</button>
              <button class="icon-btn" type="button" data-edit="${i}" title="Szerkesztés" aria-label="Szerkesztés">${ICO('pen-line')}</button>
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
      loaded = true;
      render();
    }

    // ----- feltöltés -----

    async function startUpload(fileList) {
      const files = [...fileList];
      if (!files.length) return;
      const linkDefs = await links();
      const rejected = [];
      const items = files.filter((f) => {
        if (!EXTENSIONS.includes(ext(f.name))) { rejected.push(`${f.name}: ez a fájltípus nem tölthető fel.`); return false; }
        if (f.size > MAX_BYTES) { rejected.push(`${f.name}: nagyobb, mint 15 MB.`); return false; }
        if (!f.size) { rejected.push(`${f.name}: üres fájl.`); return false; }
        return true;
      }).map((file) => ({ file, name: file.name.replace(/\.[^.]+$/, ''), category: guessCategory(file), done: false }));

      if (!items.length) {
        uiAlert(rejected.join('\n'), { title: 'Nem tölthető fel' });
        return;
      }

      const rowsHtml = () => items.map((it, i) => `
        <div class="doc-up-row${it.done ? ' done' : ''}" data-row="${i}">
          <span class="doc-ico k-${kindOf({ contentType: typeFromName(it.file.name) })}">${ICO(iconOf({ contentType: typeFromName(it.file.name) }))}<i>${escapeHtml(ext(it.file.name).toUpperCase())}</i></span>
          <div class="doc-up-main">
            <input type="text" value="${escapeHtml(it.name)}" maxlength="200" aria-label="Név" data-name="${i}"${it.done ? ' disabled' : ''}>
            <div class="doc-up-meta"><span>${escapeHtml(it.file.name)}, ${formatSize(it.file.size)}</span><span class="doc-up-state" data-state="${i}">${it.done ? 'Feltöltve' : ''}</span></div>
            <div class="doc-up-bar"><span data-bar="${i}"></span></div>
          </div>
          ${categorySelect(it.category, `aria-label="Kategória" data-cat="${i}"${it.done ? ' disabled' : ''}`)}
          <button class="icon-btn" type="button" data-remove="${i}" title="Kihagyás" aria-label="Kihagyás"${it.done ? ' hidden' : ''}>${ICO('x')}</button>
        </div>`).join('');

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
      overlay.addEventListener('change', (e) => { if (e.target.dataset.cat) items[e.target.dataset.cat].category = e.target.value; });
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
        const empty = todo.find((it) => !it.name.trim());
        if (empty) {
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
          row.classList.remove('failed');
          row.classList.add('busy');
          state.textContent = 'Feltöltés…';
          const form = new FormData();
          form.append('file', it.file);
          form.append('name', it.name.trim());
          form.append('category', it.category);
          if (note) form.append('note', note);
          form.append(scopeKey, scope[scopeKey]);
          Object.entries(linkValues).forEach(([k, v]) => { if (v && k !== scopeKey) form.append(k, v); });
          const result = await uploadFile(form, (p) => { bar.style.width = `${Math.round(p * 100)}%`; });
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

    // ----- szerkesztés, törlés -----

    async function edit(doc) {
      const linkDefs = await links(doc);
      const { overlay, close } = openModal({
        title: 'Dokumentum adatai',
        body: `<div class="form-error" data-ed-error></div>
          <div class="field"><label for="doc-ed-name">Név</label><input id="doc-ed-name" type="text" maxlength="200"></div>
          <div class="field"><label for="doc-ed-cat">Kategória</label>${categorySelect(doc.category, 'id="doc-ed-cat"')}</div>
          ${linkFieldsHtml(linkDefs, doc)}
          <div class="field"><label for="doc-ed-note">Megjegyzés</label><textarea id="doc-ed-note" rows="3" maxlength="1000"></textarea></div>
          <p class="hint">Fájl: ${escapeHtml(doc.fileName)}, ${formatSize(doc.sizeBytes)}</p>`,
        footer: '<button class="btn btn-secondary" type="button" data-doc-close>Mégse</button><button class="btn btn-primary" type="button" data-ed-save>Mentés</button>',
      });
      overlay.querySelector('#doc-ed-name').value = doc.name;
      overlay.querySelector('#doc-ed-note').value = doc.note || '';
      overlay.querySelector('#doc-ed-name').focus();
      const save = async () => {
        const body = {
          name: overlay.querySelector('#doc-ed-name').value.trim(),
          category: overlay.querySelector('#doc-ed-cat').value,
          note: overlay.querySelector('#doc-ed-note').value.trim() || null,
          propertyId: doc.propertyId, tenantId: doc.tenantId, ownerId: doc.ownerId,
          ...readLinks(overlay),
        };
        const res = await apiFetch(`/api/documents/${doc.id}`, { method: 'PUT', body: JSON.stringify(body) });
        if (!res.ok) {
          const el = overlay.querySelector('[data-ed-error]');
          el.textContent = errorMessage(res, await readJson(res), 'Nem sikerült menteni.');
          el.classList.add('show');
          return;
        }
        close();
        load();
      };
      overlay.querySelector('[data-ed-save]').addEventListener('click', save);
      overlay.querySelector('#doc-ed-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); save(); } });
    }

    async function remove(doc) {
      const others = [doc.propertyId && scopeKey !== 'propertyId' && 'az ingatlannál', doc.tenantId && scopeKey !== 'tenantId' && 'a bérlőnél',
        doc.ownerId && scopeKey !== 'ownerId' && 'a tulajdonosnál'].filter(Boolean);
      const ok = await uiConfirm(`A(z) „${doc.name}” dokumentum véglegesen törlődik${others.length ? `, ${others.join(' és ')} is` : ''}. Ez nem vonható vissza.`,
        { title: 'Dokumentum törlése', confirmText: 'Törlés', danger: true });
      if (!ok) return;
      const res = await apiFetch(`/api/documents/${doc.id}`, { method: 'DELETE' });
      if (!res.ok && res.status !== 204) {
        uiAlert(errorMessage(res, await readJson(res), 'Nem sikerült törölni.'), { title: 'Törlés sikertelen' });
        return;
      }
      blobCache.delete(doc.id);
      load();
    }

    // ----- események -----

    host.addEventListener('click', (e) => {
      const b = e.target.closest('button');
      if (!b || !host.contains(b)) return;
      const list = visibleDocs();
      if (b.hasAttribute('data-doc-upload')) input.click();
      else if (b.dataset.filter) { filter = b.dataset.filter; render(); }
      else if (b.dataset.open) openViewer(list, Number(b.dataset.open));
      else if (b.dataset.download) downloadDoc(list[Number(b.dataset.download)]);
      else if (b.dataset.edit) edit(list[Number(b.dataset.edit)]);
      else if (b.dataset.delete) remove(list[Number(b.dataset.delete)]);
    });
    input.addEventListener('change', () => { startUpload(input.files); input.value = ''; });

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
    return { reload: load, isLoaded: () => loaded };
  };
})();
