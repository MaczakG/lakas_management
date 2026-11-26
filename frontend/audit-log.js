// Beállítások → Napló fül: a felhasználói napló (ki, mikor, mit csinált), a legfrissebb elöl, 100-asával lapozva.
// Szűrhető felhasználóra, típusra és ingatlanra. A „Beállítások” / „Napló” fül a címben (#naplo) is megadható.
(function () {
  const $ = (id) => document.getElementById(id);
  const tbody = $('log-tbody');
  let loaded = 0;
  let loading = false;
  let started = false;

  // ---------- fülek ----------
  function showTab(name) {
    document.querySelectorAll('[data-stab]').forEach((b) => {
      const on = b.dataset.stab === name;
      b.classList.toggle('active', on);
      b.setAttribute('aria-selected', String(on));
    });
    document.querySelectorAll('[data-spane]').forEach((p) => { p.hidden = p.dataset.spane !== name; });
    if (name === 'log' && !started) { started = true; init(); }
  }
  document.querySelectorAll('[data-stab]').forEach((b) => b.addEventListener('click', () => {
    showTab(b.dataset.stab);
    history.replaceState(null, '', b.dataset.stab === 'log' ? '#naplo' : location.pathname + location.search);
  }));
  if (location.hash === '#naplo') showTab('log');

  // ---------- megjelenítés ----------
  const ACTION_CLASS = { Created: 'ok', Updated: '', Deleted: 'missing', Login: 'inactive' };
  const LINKS = {
    Property: (id) => `property-detail.html?id=${id}`,
    Tenant: (id) => `tenant-detail.html?id=${id}`,
    Owner: (id) => `owner-detail.html?id=${id}`,
    Invoice: (id) => `invoice-detail.html?id=${id}`,
    Document: (id) => `document-detail.html?id=${id}`,
  };
  const VISIBLE_CHANGES = 4;

  function when(iso) {
    const d = new Date(iso);
    return `${d.toLocaleDateString('hu-HU')} ${d.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' })}`;
  }

  function changeHtml(c, action) {
    const v = (x) => escapeHtml(x ?? '—');
    if (action === 'Created') return `<li><b>${escapeHtml(c.field)}:</b> ${v(c.new)}</li>`;
    if (action === 'Deleted') return `<li><b>${escapeHtml(c.field)}:</b> <span class="old">${v(c.old)}</span></li>`;
    return `<li><b>${escapeHtml(c.field)}:</b> <span class="old">${v(c.old)}</span> → ${v(c.new)}</li>`;
  }

  function changesHtml(e) {
    if (!e.changes.length) return '<span class="dim">–</span>';
    const items = e.changes.map((c) => changeHtml(c, e.action));
    if (items.length <= VISIBLE_CHANGES + 1) return `<ul class="log-changes">${items.join('')}</ul>`;
    return `<ul class="log-changes">${items.slice(0, VISIBLE_CHANGES).join('')}<li><details><summary>további ${items.length - VISIBLE_CHANGES} mező</summary><ul class="log-changes">${items.slice(VISIBLE_CHANGES).join('')}</ul></details></li></ul>`;
  }

  function itemHtml(e) {
    const label = escapeHtml(e.entityLabel || e.entityTypeLabel);
    const link = e.action !== 'Deleted' && e.entityId && LINKS[e.entityType] ? LINKS[e.entityType](encodeURIComponent(e.entityId)) : null;
    const main = link ? `<a href="${link}">${label}</a>` : label;
    const parts = [e.entityTypeLabel];
    if (e.propertyName && e.entityType !== 'Property') parts.push(e.propertyName);
    return `${main}<span class="log-sub">${escapeHtml(parts.join(' · '))}</span>`;
  }

  function rowHtml(e) {
    return `<tr>
      <td class="nowrap">${when(e.at)}</td>
      <td>${escapeHtml(e.user)}</td>
      <td><span class="status-tag ${ACTION_CLASS[e.action] ?? ''}">${escapeHtml(e.actionLabel)}</span></td>
      <td>${e.action === 'Login' ? '<span class="dim">–</span>' : itemHtml(e)}</td>
      <td>${changesHtml(e)}</td>
    </tr>`;
  }

  // ---------- betöltés ----------
  function query() {
    const p = new URLSearchParams({ skip: String(loaded) });
    if ($('log-user').value) p.set('userId', $('log-user').value);
    if ($('log-type').value) p.set('type', $('log-type').value);
    if ($('log-property').value) p.set('propertyId', $('log-property').value);
    return p.toString();
  }

  async function load(reset) {
    if (loading) return;
    loading = true;
    if (reset) {
      loaded = 0;
      tbody.innerHTML = '<tr class="empty-row"><td colspan="5">Betöltés...</td></tr>';
    }
    $('log-more').disabled = true;
    try {
      const res = await apiFetch(`/api/audit-log?${query()}`);
      if (!res.ok) throw new Error();
      const page = await res.json();
      fillTypes(page.types);
      if (reset) tbody.innerHTML = '';
      tbody.insertAdjacentHTML('beforeend', page.entries.map(rowHtml).join(''));
      loaded += page.entries.length;
      if (!loaded) tbody.innerHTML = '<tr class="empty-row"><td colspan="5">Nincs a szűrésnek megfelelő bejegyzés.</td></tr>';
      $('log-more').hidden = !page.hasMore;
    } catch {
      if (reset) tbody.innerHTML = '<tr class="empty-row"><td colspan="5">A naplót nem sikerült betölteni.</td></tr>';
    } finally {
      $('log-more').disabled = false;
      loading = false;
    }
  }

  function fillTypes(types) {
    const select = $('log-type');
    if (select.options.length > 1) return;
    select.insertAdjacentHTML('beforeend', Object.entries(types)
      .sort((a, b) => a[1].localeCompare(b[1], 'hu'))
      .map(([key, label]) => `<option value="${escapeHtml(key)}">${escapeHtml(label)}</option>`).join(''));
  }

  async function init() {
    const [users, properties] = await Promise.all([
      apiFetch('/api/users').then((r) => (r.ok ? r.json() : [])).catch(() => []),
      apiFetch('/api/properties').then((r) => (r.ok ? r.json() : [])).catch(() => []),
    ]);
    $('log-user').insertAdjacentHTML('beforeend',
      users.map((u) => `<option value="${escapeHtml(u.id)}">${escapeHtml(u.fullName)}</option>`).join('')
      + '<option value="00000000-0000-0000-0000-000000000000">Rendszer (automatikus)</option>');
    $('log-property').insertAdjacentHTML('beforeend',
      properties.map((p) => `<option value="${escapeHtml(p.id)}">${escapeHtml(p.name)}</option>`).join(''));
    ['log-user', 'log-type', 'log-property'].forEach((id) => $(id).addEventListener('change', () => load(true)));
    $('log-more').addEventListener('click', () => load(false));
    await load(true);
  }
})();
