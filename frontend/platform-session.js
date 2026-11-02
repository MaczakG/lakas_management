// A Platform admin saját munkamenete — külön kulcs a böngészőben, így a cég-fiókos belépéstől teljesen
// független (egyszerre akár mindkettőben be lehet lépve, egymásra nincsenek hatással). Minden platform-*.html
// oldal az app.js UTÁN tölti be: felülírja az app.js munkamenet-függvényeit a platform kulcsára és oldalaira.
const PLATFORM_AUTH_KEY = 'lakaskezelo_platform_auth';

getAuth = function () {
  try { return JSON.parse(localStorage.getItem(PLATFORM_AUTH_KEY) || 'null'); } catch { return null; }
};
storeAuth = function (authResponse) { localStorage.setItem(PLATFORM_AUTH_KEY, JSON.stringify(authResponse)); };
clearAuth = function () { localStorage.removeItem(PLATFORM_AUTH_KEY); };
requireAuth = function () {
  const auth = getAuth();
  if (!auth) { location.href = 'platform-login.html'; return null; }
  return auth;
};
logout = function () { clearAuth(); location.href = 'platform-login.html'; };
apiFetch = async function (path, options = {}) {
  const auth = getAuth();
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(auth ? { Authorization: `Bearer ${auth.accessToken}` } : {}),
      ...(options.headers || {}),
    },
  });
  if (res.status === 401) {
    clearAuth();
    location.href = 'platform-login.html';
    throw new Error('Nincs érvényes munkamenet.');
  }
  return res;
};
