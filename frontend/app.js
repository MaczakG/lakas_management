// Megosztott auth/API segédfüggvények — minden oldal betölti <script src="app.js"> a saját
// inline szkriptje előtt. localStorage-ban tárolja a munkamenetet (nincs "emlékezz rám" opció).
// A bejelentkezés kétlépcsős: jelszó + e-mailben kapott 6 jegyű kód (ld. login.html), a kapott JWT
// a szerveren visszavonható (jelszócsere/inaktiválás után 401 → automatikus kijelentkeztetés).
// Élesben (AWS EC2) a frontend és az API ugyanazon origin alól szolgál ki — az nginx
// reverse-proxyzza az /api/-t a backend felé, ezért üres (relatív) API_BASE kell. Helyi
// fejlesztésnél az API külön porton fut.
const API_BASE = (location.hostname === 'localhost' || location.hostname === '127.0.0.1')
  ? 'http://localhost:5080'
  : '';

function getAuth() {
  const raw = localStorage.getItem('lakaskezelo_auth');
  if (!raw) return null;
  try { return JSON.parse(raw); } catch { return null; }
}

function storeAuth(authResponse) {
  localStorage.setItem('lakaskezelo_auth', JSON.stringify(authResponse));
}

function clearAuth() {
  localStorage.removeItem('lakaskezelo_auth');
}

function authHeaders() {
  const auth = getAuth();
  return auth ? { Authorization: `Bearer ${auth.accessToken}` } : {};
}

// Minden oldal (a login/forgot/reset kivételével) ezt hívja a szkriptje elején — ha nincs
// munkamenet, azonnal a bejelentkezési oldalra irányít, mielőtt bármi más lefutna.
function requireAuth() {
  const auth = getAuth();
  if (!auth) {
    location.href = 'login.html';
    return null;
  }
  return auth;
}

// Cégen belüli adminisztrátor-e (a szerver úgyis ellenőrzi — ez csak a felület elrejtéséhez kell).
function isAdmin() {
  return getAuth()?.user?.role === 'Admin';
}

function logout() {
  clearAuth();
  location.href = 'login.html';
}

// Közös fetch-wrapper: JSON body/response, Authorization fejléc, és 401 esetén automatikus
// kijelentkeztetés (lejárt/érvénytelen token) — így minden oldalnak csak a saját üzleti logikáját
// kell írnia, nem kell mindenhol külön kezelnie a lejárt munkamenetet.
async function apiFetch(path, options = {}) {
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders(),
      ...(options.headers || {}),
    },
  });

  if (res.status === 401) {
    clearAuth();
    location.href = 'login.html';
    throw new Error('Nincs érvényes munkamenet.');
  }

  return res;
}

// Fájl letöltése az API-ról (PDF, ZIP) — sima <a href> nem küldené el a Bearer tokent, ezért
// fetch-csel töltjük le, és egy ideiglenes blob-linkkel mentetjük el a böngészővel.
async function downloadFile(path, fallbackName) {
  const res = await fetch(`${API_BASE}${path}`, { headers: authHeaders() });
  if (res.status === 401) {
    clearAuth();
    location.href = 'login.html';
    return;
  }
  if (!res.ok) {
    const data = await res.json().catch(() => ({}));
    await uiAlert(data.message || 'Nem sikerült letölteni a fájlt.', { title: 'Letöltés sikertelen' });
    return;
  }

  const disposition = res.headers.get('Content-Disposition') || '';
  const match = disposition.match(/filename\*=UTF-8''([^;]+)/i) || disposition.match(/filename="?([^";]+)"?/i);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;

  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// Magyar számformátum explicit szeparátorokkal ("100 000", "1 234,56") — nem a böngésző
// locale-adataira bízzuk, mert egyes környezetekben vesszőt tett ezres elválasztónak.
// Nem törő szóközt használunk, hogy az összeg ne törjön két sorba.
// Hibaválasz szövege: a backend { message } JSON-t ad, de a túl sok kérésre (429) az nginx is
// válaszolhat saját, nem JSON törzzsel — ezért a JSON-olvasás itt nem dobhat hibát.
async function readJson(res) {
  return res.json().catch(() => ({}));
}

function errorMessage(res, data, fallback) {
  if (data && data.message) return data.message;
  if (res.status === 429) return 'Túl sok próbálkozás. Várj egy kicsit, és próbáld újra.';
  return fallback;
}

// Teszt-környezetben (a szerver /api/version-je "staging"-et mond) minden oldal tetején egy jól látható
// sáv jelzi, hogy ez nem az éles rendszer — a böngészőfül címe is "[TESZT]"-tel kezdődik.
// A nyilvános /api/version (verzió, környezet, termék neve, nyitva van-e a regisztráció) — egyszer
// kérjük le, minden oldal ezt a promise-t használja.
const APP_INFO = fetch(`${API_BASE}/api/version`).then((r) => r.json()).catch(() => ({}));

APP_INFO.then((v) => {
  if (!v.environment || v.environment === 'production') return;
  const show = () => {
    if (document.getElementById('env-ribbon')) return;
    const ribbon = document.createElement('div');
    ribbon.id = 'env-ribbon';
    ribbon.className = 'env-ribbon';
    ribbon.textContent = `Tesztkörnyezet (v${v.version}): kitalált adatok, e-mail csak a felhasználóknak megy`;
    document.body.prepend(ribbon);
    document.documentElement.classList.add('has-env-ribbon');
    if (!document.title.startsWith('[TESZT]')) document.title = `[TESZT] ${document.title}`;
  };
  if (document.body) show(); else document.addEventListener('DOMContentLoaded', show);
}).catch(() => {});

// Az oldalfejléc élő számlálója (Yardly-minta): [{ n: 8, label: 'aktív', tone: 'ok' }, ...]
// → "● 8 aktív  ● 2 hiányzó rezsi". A 0 értékű, tone-nal jelölt (figyelmeztető) tételek kimaradnak.
function setPageState(parts) {
  const el = document.getElementById('page-state');
  if (!el) return;
  el.innerHTML = parts
    .filter((p) => !(p.tone && p.tone !== 'ok' && !p.n))
    .map((p) => `<span class="s ${p.tone || ''}"><b>${Number(p.n) || 0}</b> ${escapeHtml(p.label)}</span>`)
    .join('');
}

// HTML-escape szöveghez ÉS attribútumértékhez is (az idézőjeleket is kódolja) — minden oldal ezt
// használja, ha API-ból jövő szöveget tesz innerHTML-be vagy egy attribútumba.
function escapeHtml(s) {
  return String(s ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// Inline eseménykezelő (onclick="f(${jsArg(x)})") szöveges argumentuma: JS-string literál, ami
// HTML-attribútumban is biztonságos. Idézőjelek NÉLKÜL kell beilleszteni — a literál hozza a sajátját.
function jsArg(s) {
  return escapeHtml(JSON.stringify(String(s ?? '')));
}

function formatNumber(amount, decimals = 0) {
  const [intPart, fracPart] = Math.abs(Number(amount) || 0).toFixed(decimals).split('.');
  const grouped = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, '\u00A0');
  return `${Number(amount) < 0 ? '-' : ''}${grouped}${fracPart ? ',' + fracPart : ''}`;
}

function formatCurrency(amount) {
  return `${formatNumber(amount)} Ft`;
}

// Devizanem-érzékeny formázás — HUF-nál a megszokott "N Ft" (tizedesjegy nélkül), EUR/USD-nál
// "N,NN EUR/USD" (2 tizedesjeggyel, mivel ott gyakori a törtösszeg).
function formatMoney(amount, currency) {
  if (!currency || currency === 'HUF') return formatCurrency(amount);
  return `${formatNumber(amount, 2)} ${currency}`;
}

function formatDate(value) {
  if (!value) return '–';
  const d = new Date(value);
  return d.toLocaleDateString('hu-HU');
}

const MONTH_NAMES = ['Január', 'Február', 'Március', 'Április', 'Május', 'Június', 'Július', 'Augusztus', 'Szeptember', 'Október', 'November', 'December'];

// ======================================================================================================
// Saját felugró ablakok és legördülő listák — a böngésző alert/confirm/prompt ablakai és a natív select-lista
// helyett, hogy minden a Lakáskezelő dizájnjában jelenjen meg. A stílusok a styles.css „ui-” részében.
// ======================================================================================================

// Párbeszédablak. Visszaad: alertnél undefined, confirmnál true/false, promptnál a beírt szöveg vagy null.
// opts: { title, confirmText, cancelText, danger, input: { value, placeholder, type, label } }
function uiDialog(kind, message, opts = {}) {
  return new Promise((resolve) => {
    const prevFocus = document.activeElement;
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay ui-dialog-overlay';
    const title = opts.title || (kind === 'alert' ? 'Értesítés' : kind === 'prompt' ? 'Megadás' : 'Megerősítés');
    const confirmText = opts.confirmText || (kind === 'alert' ? 'Rendben' : kind === 'prompt' ? 'Mentés' : 'Igen');
    const cancelText = opts.cancelText || 'Mégse';
    const input = opts.input || {};
    overlay.innerHTML = `
      <div class="modal ui-dialog" role="${kind === 'alert' ? 'alertdialog' : 'dialog'}" aria-modal="true" aria-labelledby="ui-dialog-title">
        <div class="modal-head"><div><h2 id="ui-dialog-title"></h2></div></div>
        <div class="modal-body">
          <p class="ui-dialog-message"></p>
          ${kind === 'prompt' ? `<div class="field ui-dialog-field">${input.label ? '<label for="ui-dialog-input"></label>' : ''}<input id="ui-dialog-input" type="${input.type === 'number' ? 'number' : 'text'}" autocomplete="off"></div>` : ''}
        </div>
        <div class="modal-footer">
          ${kind === 'alert' ? '' : '<button class="btn btn-secondary" type="button" data-ui-cancel></button>'}
          <button class="btn ${opts.danger ? 'btn-danger-solid' : 'btn-primary'}" type="button" data-ui-ok></button>
        </div>
      </div>`;
    // A szövegek textContenttel kerülnek be (adatból is jöhetnek).
    overlay.querySelector('#ui-dialog-title').textContent = title;
    overlay.querySelector('.ui-dialog-message').textContent = message || '';
    overlay.querySelector('[data-ui-ok]').textContent = confirmText;
    const cancelBtn = overlay.querySelector('[data-ui-cancel]');
    if (cancelBtn) cancelBtn.textContent = cancelText;
    const field = overlay.querySelector('#ui-dialog-input');
    if (field) {
      field.value = input.value ?? '';
      if (input.placeholder) field.placeholder = input.placeholder;
      const label = overlay.querySelector('.ui-dialog-field label');
      if (label) label.textContent = input.label;
    }
    document.body.appendChild(overlay);
    requestAnimationFrame(() => overlay.classList.add('open'));

    const close = (result) => {
      overlay.classList.remove('open');
      document.removeEventListener('keydown', onKey, true);
      setTimeout(() => overlay.remove(), 200);
      if (prevFocus && typeof prevFocus.focus === 'function') prevFocus.focus();
      resolve(result);
    };
    const ok = () => close(kind === 'prompt' ? field.value : kind === 'confirm' ? true : undefined);
    const cancel = () => close(kind === 'prompt' ? null : kind === 'confirm' ? false : undefined);
    const onKey = (e) => {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); cancel(); }
      else if (e.key === 'Enter' && (kind === 'alert' || document.activeElement === field)) { e.preventDefault(); ok(); }
      else if (e.key === 'Tab') {
        // a fókusz az ablakon belül marad
        const items = [...overlay.querySelectorAll('input,button')];
        const i = items.indexOf(document.activeElement);
        if (e.shiftKey && i <= 0) { e.preventDefault(); items[items.length - 1].focus(); }
        else if (!e.shiftKey && i === items.length - 1) { e.preventDefault(); items[0].focus(); }
      }
    };
    document.addEventListener('keydown', onKey, true);
    overlay.querySelector('[data-ui-ok]').addEventListener('click', ok);
    if (cancelBtn) cancelBtn.addEventListener('click', cancel);
    overlay.addEventListener('mousedown', (e) => { if (e.target === overlay && kind !== 'alert') cancel(); });
    setTimeout(() => (field || (opts.danger && cancelBtn) || overlay.querySelector('[data-ui-ok]')).focus(), 30);
    if (field) setTimeout(() => field.select(), 40);
  });
}
const uiAlert = (message, opts) => uiDialog('alert', message, opts);
const uiConfirm = (message, opts) => uiDialog('confirm', message, opts);
const uiPrompt = (message, opts) => uiDialog('prompt', message, opts);

// Legördülő lista: minden egyszeres <select> natív lenyíló listája helyett saját lista nyílik (egérrel, érintéssel,
// billentyűzettel: Szóköz / Enter / Alt+↓). A <select> maga megmarad — az értéke, az eseményei (change) és a
// stílusa változatlan, így az oldalak kódja nem változik. Eseménydelegálással működik a később létrejövőkre is.
(function () {
  let menu = null;
  let current = null; // a nyitott <select>
  let active = -1;
  let typed = '';
  let typedTimer = null;

  const usable = (el) => el instanceof HTMLSelectElement && !el.multiple && !el.disabled && el.size <= 1;
  const options = () => [...current.options].filter((o) => !o.hidden);

  function close(refocus = true) {
    if (!menu) return;
    menu.remove();
    menu = null;
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('pointerdown', onOutside, true);
    window.removeEventListener('resize', onReflow);
    window.removeEventListener('scroll', onScroll, true);
    const sel = current;
    current = null;
    if (refocus && sel) sel.focus();
  }

  function setActive(i) {
    const items = menu.querySelectorAll('.ui-option');
    if (!items.length) return;
    active = Math.max(0, Math.min(items.length - 1, i));
    items.forEach((el, k) => el.classList.toggle('active', k === active));
    items[active].scrollIntoView({ block: 'nearest' });
  }

  function choose(index) {
    const opt = options()[index];
    if (!opt || opt.disabled) return;
    const sel = current;
    const changed = sel.value !== opt.value || sel.selectedIndex !== opt.index;
    sel.selectedIndex = opt.index;
    close();
    if (changed) {
      sel.dispatchEvent(new Event('input', { bubbles: true }));
      sel.dispatchEvent(new Event('change', { bubbles: true }));
    }
  }

  function position() {
    const r = current.getBoundingClientRect();
    menu.style.minWidth = `${Math.max(r.width, 160)}px`;
    menu.style.left = `${Math.max(8, Math.min(r.left, window.innerWidth - menu.offsetWidth - 8))}px`;
    const below = window.innerHeight - r.bottom - 8;
    const above = r.top - 8;
    const h = Math.min(menu.scrollHeight, 300);
    if (below >= Math.min(h, 200) || below >= above) {
      menu.style.top = `${r.bottom + 4}px`;
      menu.style.maxHeight = `${Math.min(300, below - 4)}px`;
    } else {
      menu.style.top = `${Math.max(8, r.top - 4 - Math.min(h, above - 4))}px`;
      menu.style.maxHeight = `${Math.min(300, above - 4)}px`;
    }
  }

  function open(sel) {
    if (menu) close(false);
    current = sel;
    menu = document.createElement('div');
    menu.className = 'ui-menu';
    menu.setAttribute('role', 'listbox');
    const label = sel.labels && sel.labels[0] ? sel.labels[0].textContent.trim() : sel.getAttribute('aria-label');
    if (label) menu.setAttribute('aria-label', label);
    options().forEach((o, i) => {
      const item = document.createElement('div');
      item.className = `ui-option${o.selected ? ' selected' : ''}${o.disabled ? ' disabled' : ''}`;
      item.setAttribute('role', 'option');
      item.setAttribute('aria-selected', String(o.selected));
      const text = document.createElement('span');
      text.textContent = o.textContent;
      item.appendChild(text);
      item.addEventListener('pointermove', () => setActive(i));
      item.addEventListener('click', () => choose(i));
      menu.appendChild(item);
    });
    if (!menu.children.length) {
      const empty = document.createElement('div');
      empty.className = 'ui-option disabled';
      empty.textContent = 'Nincs választható elem';
      menu.appendChild(empty);
    }
    document.body.appendChild(menu);
    position();
    const selIndex = options().findIndex((o) => o.selected);
    setActive(selIndex >= 0 ? selIndex : 0);
    document.addEventListener('keydown', onKey, true);
    document.addEventListener('pointerdown', onOutside, true);
    window.addEventListener('resize', onReflow);
    window.addEventListener('scroll', onScroll, true);
  }

  function onKey(e) {
    if (!menu) return;
    const count = menu.querySelectorAll('.ui-option').length;
    // A lista által kezelt billentyű ne jusson tovább (pl. a választó Enter ne nyissa újra a listát).
    if (['ArrowDown', 'ArrowUp', 'Home', 'End', 'PageDown', 'PageUp', 'Enter', ' ', 'Escape'].includes(e.key)) e.stopPropagation();
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); setActive(active + 1); break;
      case 'ArrowUp': e.preventDefault(); setActive(active - 1); break;
      case 'Home': e.preventDefault(); setActive(0); break;
      case 'End': e.preventDefault(); setActive(count - 1); break;
      case 'PageDown': e.preventDefault(); setActive(active + 8); break;
      case 'PageUp': e.preventDefault(); setActive(active - 8); break;
      case 'Enter': case ' ': e.preventDefault(); choose(active); break;
      case 'Escape': e.preventDefault(); e.stopPropagation(); close(); break;
      case 'Tab': close(false); break;
      default:
        if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
          // gépelésre ugrás az első egyező elemre
          clearTimeout(typedTimer);
          typed += e.key.toLowerCase();
          typedTimer = setTimeout(() => { typed = ''; }, 700);
          const i = options().findIndex((o) => o.textContent.trim().toLowerCase().startsWith(typed));
          if (i >= 0) setActive(i);
        }
    }
  }
  function onOutside(e) {
    if (menu && !menu.contains(e.target) && e.target !== current) close(false);
  }
  function onReflow() { if (menu) position(); }
  function onScroll(e) { if (menu && !menu.contains(e.target)) close(false); }

  // Egér és érintés: a natív lista helyett a saját nyílik.
  document.addEventListener('mousedown', (e) => {
    const sel = e.target;
    if (!usable(sel) || e.button !== 0) return;
    e.preventDefault();
    sel.focus();
    if (menu && current === sel) close(); else open(sel);
  });
  document.addEventListener('touchstart', (e) => {
    const sel = e.target;
    if (!usable(sel)) return;
    e.preventDefault();
    sel.focus();
    if (menu && current === sel) close(); else open(sel);
  }, { passive: false });
  document.addEventListener('keydown', (e) => {
    const sel = e.target;
    if (menu || !usable(sel)) return;
    if (e.key === ' ' || e.key === 'Enter' || e.key === 'F4' || (e.altKey && (e.key === 'ArrowDown' || e.key === 'ArrowUp'))) {
      e.preventDefault();
      open(sel);
    }
  });
})();

// Egy rezsi-hónap állapota a tételei alapján (tételenkénti zárolás): van kitöltendő (még összeg nélküli) tétel →
// „Kitöltendő”; mind bizonylaton → „Bizonylaton”; van bizonylaton lévő és még nyitott is (utólag felvett) →
// „Utólagos tétel” (a következő bizonylatra kerül); egyik sem → „Rögzítve”. A „tovább számlázandó” tétel soha nem
// kerül bizonylatra, ezért a zárolásnál nem számít.
function utilityPeriodTag(entries) {
  if (!entries.length) return '<span class="status-tag missing">Nincs adat</span>';
  if (entries.some((e) => e.isPending && !e.isLocked)) {
    return '<span class="status-tag warn" title="Van még összeg nélküli tétel: amíg nincs kitöltve, nem kerül bizonylatra">Kitöltendő</span>';
  }
  const billable = entries.filter((e) => !e.forwardBilled);
  const locked = billable.filter((e) => e.isLocked).length;
  if (billable.length && locked === billable.length) {
    return billable.some((e) => e.invoiceId) ? '<span class="status-tag inactive">Bizonylaton</span>' : '<span class="status-tag inactive">Lezárva</span>';
  }
  if (locked > 0) return '<span class="status-tag warn" title="Az utólag felvett tételek a következő bizonylatra kerülnek">Utólagos tétel</span>';
  return '<span class="status-tag ok">Rögzítve</span>';
}

// Több e-mail cím egy mezőben (a bérlő értesítőinek címzettjei): vesszővel, pontosvesszővel, szóközzel vagy új
// sorral elválasztva. { value } a ", "-vel összefűzött címeket adja (üresnél null), { error } a hibaüzenetet.
const EMAIL_LIST_MAX = 5;
function splitEmails(text) {
  const seen = new Set();
  return String(text ?? '').split(/[,;\s]+/).filter((a) => {
    if (!a || seen.has(a.toLowerCase())) return false;
    seen.add(a.toLowerCase());
    return true;
  });
}
function parseEmailList(text) {
  const addresses = splitEmails(text);
  if (!addresses.length) return { value: null };
  const invalid = addresses.find((a) => !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(a));
  if (invalid) return { error: `Érvénytelen e-mail cím: ${invalid}` };
  if (addresses.length > EMAIL_LIST_MAX) return { error: `Legfeljebb ${EMAIL_LIST_MAX} e-mail cím adható meg.` };
  return { value: addresses.join(', ') };
}

// A következő bizonylat rezsijének állapota (PropertyDto.utilityState): Ok / Pending / Missing.
function utilityStateTag(state, okLabel = 'Rendben') {
  if (state === 'Pending') return '<span class="status-tag warn" title="Van kitöltendő (nem fix) rezsi tétel">Kitöltendő</span>';
  if (state === 'Missing') return '<span class="status-tag missing">Hiányzik</span>';
  return `<span class="status-tag ok">${okLabel}</span>`;
}
