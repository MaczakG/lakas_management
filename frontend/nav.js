// Megosztott sidebar — minden oldal <div class="app"> után rögtön behúzza <script src="nav.js">.
// document.write() a script-tag helyére teszi a markupot (klasszikus, szinkron script). Nincs
// jogosultság-alapú elrejtés — minden bejelentkezett felhasználó ugyanazt a menüt látja.
(function () {
  document.write(`
  <button class="mobile-nav-toggle" id="mobile-nav-toggle" aria-label="Menü">
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18M3 12h18M3 18h18"/></svg>
  </button>
  <div class="sidebar-backdrop" id="sidebar-backdrop"></div>
  <aside class="sidebar">
    <div class="brand"><span class="brand-name">Lakáskezelő</span></div>

    <div class="nav-scroll">
    <a class="nav-item" href="properties.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V21h14V9.5"/></svg></span> Ingatlanok</a>
    <a class="nav-item" href="owners.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg></span> Tulajdonosok</a>
    <a class="nav-item" href="tenants.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg></span> Bérlők</a>
    <a class="nav-item" href="billing.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6M9 13h6M9 17h6"/></svg></span> Számlázás</a>
    <a class="nav-item" href="exchange-rates.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M7 3v18M7 3 3 7M7 3l4 4"/><path d="M17 21V3M17 21l4-4M17 21l-4-4"/></svg></span> Árfolyamok</a>

    <div class="nav-label">Fiók</div>
    <a class="nav-item" href="users.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="8" r="4"/><path d="M4 21c0-4 4-6 8-6s8 2 8 6"/></svg></span> Felhasználók</a>
    <a class="nav-item" href="settings.html"><span class="ico"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="10" rx="0"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg></span> Beállítások</a>
    </div>

    <div class="sidebar-footer">
      <div class="avatar" id="avatar">··</div>
      <div class="who">
        <span id="who-name">—</span>
      </div>
      <a class="logout-link" href="#" id="logout-link" title="Kijelentkezés">
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/></svg>
      </a>
    </div>
  </aside>`);

  const page = location.pathname.split('/').pop() || 'properties.html';
  document.querySelectorAll('.sidebar .nav-item[href]').forEach((a) => {
    if (a.getAttribute('href') === page) a.classList.add('active');
  });

  document.getElementById('mobile-nav-toggle')?.addEventListener('click', () => document.querySelector('.app').classList.toggle('sidebar-open'));
  document.getElementById('sidebar-backdrop')?.addEventListener('click', () => document.querySelector('.app').classList.remove('sidebar-open'));

  document.getElementById('logout-link')?.addEventListener('click', (e) => {
    e.preventDefault();
    logout();
  });

  const auth = getAuth();
  if (auth?.user) {
    const name = auth.user.fullName || auth.user.email;
    document.getElementById('who-name').textContent = name;
    document.getElementById('avatar').textContent = name.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase();
  }
})();
