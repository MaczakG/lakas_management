// Megosztott oldalsáv — minden oldal <div class="app"> után rögtön behúzza <script src="nav.js">.
// document.write() a script-tag helyére teszi a markupot (klasszikus, szinkron script). A csak
// adminisztrátornak szóló menüpontok (data-admin-only) munkatársnál rejtve vannak — a szerver úgyis
// ellenőrzi a jogosultságot, ez csak a felületet tisztítja.
// Az ikonok a Lucide-sprite-ból jönnek (assets/icons/icons.svg).
(function () {
  const icon = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  // only: 'admin' (adminisztrátor), 'billing' (fizető cég adminisztrátora — az üzemeltető saját cége nem fizet)
  const item = (href, ico, label, only) => `<a class="nav-item" href="${href}"${only ? ` data-only="${only}" hidden` : ''}><span class="ico">${icon(ico)}</span>${label}</a>`;

  document.write(`
  <button class="mobile-nav-toggle" id="mobile-nav-toggle" aria-label="Menü">${icon('menu')}</button>
  <div class="sidebar-backdrop" id="sidebar-backdrop"></div>
  <aside class="sidebar">
    <a class="brand" href="dashboard.html">
      <span class="brand-mark">${icon('house')}</span>
      <span class="brand-name">Lakáskezelő<small>Bérbeadás és számlázás</small></span>
    </a>

    <nav class="nav-scroll">
      ${item('dashboard.html', 'layout-dashboard', 'Vezérlőpult')}

      <div class="nav-label">Ingatlanok</div>
      ${item('properties.html', 'house', 'Ingatlanok')}
      ${item('utility-entry.html', 'zap', 'Rezsi felvétel')}

      <div class="nav-label">Partnerek</div>
      ${item('owners.html', 'key-round', 'Tulajdonosok')}
      ${item('tenants.html', 'users', 'Bérlők')}

      <div class="nav-label">Pénzügy</div>
      ${item('invoice-bundles.html', 'file-invoice', 'Számlák')}
      ${item('exchange-rates.html', 'arrow-left-right', 'Árfolyamok')}

      <div class="nav-label">Fiók</div>
      ${item('users.html', 'user-check', 'Felhasználók')}
      ${item('subscription.html', 'circle-dollar-sign', 'Előfizetés', 'billing')}
      ${item('settings.html', 'lock', 'Beállítások', 'admin')}
    </nav>

    <div class="sidebar-footer">
      <div class="avatar" id="avatar"></div>
      <div class="who"><span id="who-name">–</span><small id="who-company"></small></div>
    </div>
    <button class="btn btn-secondary btn-sm logout-btn" type="button" id="logout-link">${icon('log-out')}Kilépés</button>
    <span class="sidebar-version"><span id="version-tag"></span><br><a href="aszf.html" target="_blank">ÁSZF</a> · <a href="adatkezeles.html" target="_blank">Adatkezelés</a><br><span class="credit">Uicons by <a href="https://www.flaticon.com/uicons" target="_blank" rel="noopener">Flaticon</a></span></span>
  </aside>`);

  // Az adatlap (property-detail.html) az Ingatlanok menüpont alá tartozik.
  const page = location.pathname.split('/').pop() || 'dashboard.html';
  // A dokumentum adatlapja ahhoz a menüponthoz tartozik, ahonnan megnyitották (?from=property:…).
  // A számla adatlapja is (?from=…; alapból a Számlák menüpont).
  const from = (new URLSearchParams(location.search).get('from') || '').split(':')[0];
  const fromPage = { property: 'properties.html', tenant: 'tenants.html', owner: 'owners.html', bundles: 'invoice-bundles.html' }[from];
  const activePage = { 'property-detail.html': 'properties.html', 'tenant-detail.html': 'tenants.html', 'owner-detail.html': 'owners.html',
    'document-detail.html': fromPage || 'properties.html', 'invoice-detail.html': fromPage || 'invoice-bundles.html' }[page] || page;
  window.__currentPage = page;
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
    const visible = {
      admin: isAdmin(),
      billing: isAdmin() && auth.billingEnabled && !auth.billingExempt,
    };
    document.querySelectorAll('.sidebar [data-only]').forEach((el) => { el.hidden = !visible[el.dataset.only]; });
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

    // Előfizetés: figyelmeztető sáv (próbaidő vége közel, sikertelen fizetés, lemondva, csak olvasható).
    apiFetch('/api/subscription/status').then((r) => (r.ok ? r.json() : null)).then((st) => {
      const auth = getAuth();
      if (!st || !auth) return;
      storeAuth({ ...auth, billingEnabled: st.billingEnabled, billingExempt: st.isOperator });
      renderWho();
      if (!st.billingEnabled || st.isOperator) return;
      showSubscriptionBanner(st);
    }).catch(() => {});
  }

  function showSubscriptionBanner(st) {
    const fmt = (d) => new Date(d).toLocaleDateString('hu-HU');
    const daysLeft = (d) => Math.max(0, Math.ceil((new Date(d) - Date.now()) / 86400000));
    let tone = 'info';
    let text = null;
    if (st.locked) {
      tone = 'danger';
      text = 'A fiók csak olvasható, mert nincs érvényes előfizetés. Az adataid megvannak, előfizetés után újra szerkesztheted őket.';
    } else if (st.status === 'PastDue') {
      tone = 'warn';
      text = `Nem sikerült megújítani az előfizetést. ${st.graceEndsAt ? fmt(st.graceEndsAt) + '-ig' : 'Néhány napig'} még használható. Fizess új kártyával.`;
    } else if (st.status === 'Trial') {
      const n = daysLeft(st.trialEndsAt);
      tone = n <= 3 ? 'warn' : 'info';
      text = `Ingyenes próbaidő: még ${n} nap (${fmt(st.trialEndsAt)}-ig).`;
    } else if (st.status === 'Active' && st.cancelAtPeriodEnd && st.paidUntil) {
      tone = 'warn';
      text = `Lemondtad az előfizetést: ${fmt(st.paidUntil)}-ig használható.`;
    }
    if (!text) return;
    const main = document.querySelector('.main');
    if (!main || document.getElementById('sub-banner')) return;
    const banner = document.createElement('div');
    banner.id = 'sub-banner';
    banner.className = `sub-banner ${tone}`;
    banner.innerHTML = `${icon(tone === 'info' ? 'hourglass' : 'triangle-alert')}<span></span>`;
    banner.querySelector('span').textContent = text;
    if (isAdmin() && window.__currentPage !== 'subscription.html') {
      const link = document.createElement('a');
      link.href = 'subscription.html';
      link.innerHTML = 'Előfizetés<svg class="inline-ico" viewBox="0 0 24 24" aria-hidden="true"><use href="assets/icons/icons.svg#arrow-right"/></svg>';
      banner.appendChild(link);
    }
    main.prepend(banner);
  }

  // Melyik verzió fut éppen — nem hitelesített végpont, támogatáshoz/hibakereséshez hasznos.
  APP_INFO.then((v) => {
    if (v.version) document.getElementById('version-tag').textContent = `v${v.version}`;
  });
})();
