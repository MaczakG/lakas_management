// Megosztott oldalsáv — minden oldal <div class="app"> után rögtön behúzza <script src="nav.js">.
// document.write() a script-tag helyére teszi a markupot (klasszikus, szinkron script). A csak
// adminisztrátornak szóló menüpontok (data-admin-only) munkatársnál rejtve vannak — a szerver úgyis
// ellenőrzi a jogosultságot, ez csak a felületet tisztítja.
// Az ikonok a Lucide-sprite-ból jönnek (assets/icons/icons.svg).
(function () {
  const icon = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const item = (href, ico, label, adminOnly) => `<a class="nav-item" href="${href}"${adminOnly ? ' data-admin-only' : ''}><span class="ico">${icon(ico)}</span>${label}</a>`;

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
      ${item('settings.html', 'lock', 'Beállítások', true)}
    </nav>

    <div class="sidebar-footer">
      <div class="avatar" id="avatar">··</div>
      <div class="who"><span id="who-name">—</span><small id="who-company"></small></div>
    </div>
    <button class="btn btn-secondary btn-sm logout-btn" type="button" id="logout-link">${icon('log-out')}Kilépés</button>
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

  const renderWho = () => {
    const auth = getAuth();
    if (!auth?.user) return;
    const name = auth.user.fullName || auth.user.email;
    document.getElementById('who-name').textContent = name;
    document.getElementById('who-name').title = auth.user.email;
    document.getElementById('who-company').textContent = auth.companyName || auth.user.email;
    document.getElementById('avatar').textContent = name.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase();
    document.querySelectorAll('.sidebar [data-admin-only]').forEach((el) => { el.hidden = !isAdmin(); });
  };
  renderWho();

  // A szerepkör és a cég neve közben változhat (pl. az admin átállította) — a tárolt munkamenetet
  // oldalbetöltéskor frissítjük a szerverről.
  if (getAuth()) {
    apiFetch('/api/auth/me').then((r) => (r.ok ? r.json() : null)).then((me) => {
      const auth = getAuth();
      if (!me || !auth) return;
      storeAuth({ ...auth, user: me.user, companyName: me.companyName });
      renderWho();
    }).catch(() => {});
  }

  // Melyik verzió fut éppen — nem hitelesített végpont, támogatáshoz/hibakereséshez hasznos.
  APP_INFO.then((v) => {
    if (v.version) document.getElementById('version-tag').textContent = `v${v.version}`;
  });
})();
