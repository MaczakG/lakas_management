// Megosztott auth/API segédfüggvények — minden oldal betölti <script src="app.js"> a saját
// inline szkriptje előtt. localStorage-ban tárolja a munkamenetet (nincs "emlékezz rám" opció,
// nincs 2FA — csak egyszerű JWT bejelentkezés).
// A lakaskezelo-api Render URL-je véletlen utótagot kapott (a sima név ütközés miatt foglalt lett
// egy korábbi, sikertelen duplikált Blueprint-próbálkozás során) — ha a szolgáltatást újra
// létrehoznák és megint más utótagot kapna, ezt kell frissíteni.
const API_BASE = (location.hostname === 'localhost' || location.hostname === '127.0.0.1')
  ? 'http://localhost:5080'
  : 'https://lakaskezelo-api-yn90.onrender.com';

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

function formatCurrency(amount) {
  return `${Number(amount).toLocaleString('hu-HU')} Ft`;
}

// Devizanem-érzékeny formázás — HUF-nál a megszokott "N Ft" (tizedesjegy nélkül), EUR/USD-nál
// "N,NN EUR/USD" (2 tizedesjeggyel, mivel ott gyakori a törtösszeg).
function formatMoney(amount, currency) {
  if (!currency || currency === 'HUF') return formatCurrency(amount);
  return `${Number(amount).toLocaleString('hu-HU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;
}

function formatDate(value) {
  if (!value) return '—';
  const d = new Date(value);
  return d.toLocaleDateString('hu-HU');
}

const MONTH_NAMES = ['Január', 'Február', 'Március', 'Április', 'Május', 'Június', 'Július', 'Augusztus', 'Szeptember', 'Október', 'November', 'December'];
