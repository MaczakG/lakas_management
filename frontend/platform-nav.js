// A Platform admin külön felülete (az üzemeltető adminisztrátorainak) — saját oldalsávval, a cégek
// alkalmazásától elkülönítve. Minden platform-*.html oldal az app.js után ezt húzza be (document.write).
// Ha a bejelentkezett felhasználó nem üzemeltető, visszairányít az alkalmazásba (a szerver amúgy is 403-at ad).
(function () {
  const icon = (id) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><use href="assets/icons/icons.svg#${id}"/></svg>`;
  const item = (href, ico, label) => `<a class="nav-item" href="${href}"><span class="ico">${icon(ico)}</span>${label}</a>`;

  document.write(`
  <button class="mobile-nav-toggle" id="mobile-nav-toggle" aria-label="Menü">${icon('menu')}</button>
  <div class="sidebar-backdrop" id="sidebar-backdrop"></div>
  <aside class="sidebar platform-sidebar">
    <a class="brand" href="platform.html">
      <span class="brand-mark">${icon('server')}</span>
      <span class="brand-name">Platform admin<small>Üzemeltetés</small></span>
    </a>

    <nav class="nav-scroll">
      <div class="nav-label">Ügyfelek</div>
      ${item('platform.html', 'building-2', 'Cégek')}
      ${item('platform-payments.html', 'receipt', 'Fizetések')}

      <div class="nav-label">Beállítások</div>
      ${item('platform-billing.html', 'circle-dollar-sign', 'Díjak és fizetés')}
      ${item('platform-delivery.html', 'mail', 'E-mail és tárhely')}
      ${item('platform-legal.html', 'scroll-text', 'Szolgáltató és jogi adatok')}
    </nav>

    <div class="sidebar-footer">
      <div class="avatar" id="avatar">··</div>
      <div class="who"><span id="who-name">—</span><small>Platform üzemeltető</small></div>
    </div>
    <a class="btn btn-secondary btn-sm logout-btn" href="properties.html">${icon('arrow-left')}Vissza az alkalmazásba</a>
    <button class="btn btn-ghost btn-sm logout-btn" type="button" id="logout-link" style="margin-top:6px;">${icon('log-out')}Kilépés</button>
    <span class="sidebar-version" id="version-tag"></span>
  </aside>`);

  const page = location.pathname.split('/').pop() || 'platform.html';
  document.querySelectorAll('.sidebar .nav-item[href]').forEach((a) => {
    if (a.getAttribute('href') === page) a.classList.add('active');
  });
  document.getElementById('mobile-nav-toggle')?.addEventListener('click', () => document.querySelector('.app').classList.toggle('sidebar-open'));
  document.getElementById('sidebar-backdrop')?.addEventListener('click', () => document.querySelector('.app').classList.remove('sidebar-open'));
  document.getElementById('logout-link')?.addEventListener('click', (e) => { e.preventDefault(); logout(); });

  const auth = getAuth();
  if (auth?.user) {
    const name = auth.user.fullName || auth.user.email;
    document.getElementById('who-name').textContent = name;
    document.getElementById('avatar').textContent = name.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase();
  }
  if (auth) {
    apiFetch('/api/auth/me').then((r) => (r.ok ? r.json() : null)).then((me) => {
      if (!me?.isPlatformOperator) location.href = 'properties.html';
    }).catch(() => {});
  }
  APP_INFO.then((v) => { if (v.version) document.getElementById('version-tag').textContent = `v${v.version}`; });
})();
