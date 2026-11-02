// Megosztott oldalsáv — minden oldal <div class="app"> után rögtön behúzza <script src="nav.js">.
// document.write() a script-tag helyére teszi a markupot (klasszikus, szinkron script). Nincs
// jogosultság-alapú elrejtés — minden bejelentkezett felhasználó ugyanazt a menüt látja.
// Az ikonok a Lucide-sprite-ból jönnek (assets/icons/icons.svg).
(function () {
  const icon = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const item = (href, ico, label) => `<a class="nav-item" href="${href}"><span class="ico">${icon(ico)}</span>${label}</a>`;

  document.write(`
  <button class="mobile-nav-toggle" id="mobile-nav-toggle" aria-label="Menü">${icon('menu')}</button>
  <div class="sidebar-backdrop" id="sidebar-backdrop"></div>
  <aside class="sidebar">
    <a class="brand" href="properties.html">
      <span class="brand-mark">${icon('house')}</span>
      <span class="brand-name">Lakáskezelő<small>Bérbeadás és számlázás</small></span>
    </a>

    <nav class="nav-scroll">
      <div class="nav-label">Ingatlanok</div>
      ${item('properties.html', 'house', 'Ingatlanok')}
      ${item('utility-entry.html', 'zap', 'Rezsi felvétel')}

      <div class="nav-label">Partnerek</div>
      ${item('owners.html', 'key-round', 'Tulajdonosok')}
      ${item('tenants.html', 'users', 'Bérlők')}

      <div class="nav-label">Pénzügy</div>
      ${item('billing.html', 'receipt', 'Számlázás')}
      ${item('invoice-bundles.html', 'package', 'Számla csomagok')}
      ${item('exchange-rates.html', 'arrow-left-right', 'Árfolyamok')}

      <div class="nav-label">Fiók</div>
      ${item('users.html', 'user-check', 'Felhasználók')}
      ${item('settings.html', 'lock', 'Beállítások')}
    </nav>

    <div class="sidebar-footer">
      <div class="avatar" id="avatar">··</div>
      <div class="who"><span id="who-name">—</span><small id="who-email"></small></div>
      <a class="logout-link" href="#" id="logout-link" title="Kijelentkezés" aria-label="Kijelentkezés">${icon('log-out')}</a>
    </div>
    <span class="sidebar-version" id="version-tag"></span>
  </aside>`);

  // Az adatlap (property-detail.html) az Ingatlanok menüpont alá tartozik.
  const page = location.pathname.split('/').pop() || 'properties.html';
  const activePage = { 'property-detail.html': 'properties.html' }[page] || page;
  document.querySelectorAll('.sidebar .nav-item[href]').forEach((a) => {
    if (a.getAttribute('href') === activePage) a.classList.add('active');
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
    document.getElementById('who-email').textContent = auth.user.fullName ? auth.user.email : '';
    document.getElementById('avatar').textContent = name.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase();
  }

  // Melyik verzió fut éppen — nem hitelesített végpont, támogatáshoz/hibakereséshez hasznos.
  fetch(`${API_BASE}/api/version`).then((r) => r.json()).then((v) => {
    document.getElementById('version-tag').textContent = `v${v.version}`;
  }).catch(() => {});
})();
