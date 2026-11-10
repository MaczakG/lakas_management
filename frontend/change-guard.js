// Figyelmeztetés módosított adatlap elhagyásakor (ingatlan, bérlő, tulajdonos, dokumentum).
//
// Az adatlap mezői továbbra is azonnal mentődnek. Ez a modul megjegyzi, mi változott az oldal megnyitása óta, és
// amikor a felhasználó a programon belül továbblépne (hivatkozás, menü, Kilépés, a böngésző Vissza gombja),
// felsorolja a módosításokat, és megkérdezi: maradjon így, vagy álljon vissza az eredeti.
//
// A böngészőfül bezárásakor és frissítéskor nem tud kérdezni (ott csak a böngésző saját, „nem mentett
// módosítások” szövegű ablaka jöhetne, ami itt félrevezető lenne: a módosítás már el van mentve).
//
// Használat:
//   ChangeGuard.group('record', async (items) => hibaüzenet | null);   // items: [{ key, before }]
//   ChangeGuard.track(mentésPromise);                                  // folyamatban lévő mentés
//   ChangeGuard.record({ group, key, label, before, after, text });    // sikeres mentés után
//   ChangeGuard.go(url) / ChangeGuard.leave(() => …)                   // kódból indított továbblépés
window.ChangeGuard = (function () {
  const entries = new Map();   // key → { group, key, label, before, after, text }
  const groups = new Map();    // név → visszaállító függvény
  const pending = new Set();   // folyamatban lévő mentések
  let asking = false;
  // A Vissza gombhoz: az első módosításkor egy „őr” előzménybejegyzés kerül az oldal fölé. A Vissza így előbb
  // erről lép le (az oldal marad), és van alkalom kérdezni.
  let onSentinel = !!history.state?.changeGuard;

  const same = (a, b) => String(a ?? '') === String(b ?? '');

  function group(name, revert) { groups.set(name, revert); }

  function track(promise) {
    pending.add(promise);
    const done = () => pending.delete(promise);
    promise.then(done, done);
    return promise;
  }

  function record({ group: groupName = 'record', key, label, before, after, text }) {
    const id = `${groupName}:${key}`;
    const existing = entries.get(id);
    const original = existing ? existing.before : before;
    if (same(original, after)) { entries.delete(id); return; }   // visszaírta az eredetit: nincs mit kérdezni
    entries.set(id, { group: groupName, key, label, before: original, after, text: text || ((v) => String(v ?? '')) });
    if (!onSentinel) {
      try { history.pushState({ changeGuard: true }, ''); onSentinel = true; } catch { /* nem baj */ }
    }
  }

  function shown(entry, value) {
    let text = entry.text(value);
    text = text == null ? '' : String(text).replace(/\s+/g, ' ').trim();
    if (!text) return '–';
    return text.length > 90 ? `${text.slice(0, 90)}…` : text;
  }

  // Az eredeti értékek visszamentése csoportonként. Hibánál az üzenetet adja vissza.
  async function revertAll() {
    const byGroup = new Map();
    entries.forEach((e) => { if (!byGroup.has(e.group)) byGroup.set(e.group, []); byGroup.get(e.group).push(e); });
    for (const [name, items] of byGroup) {
      const revert = groups.get(name);
      if (!revert) continue;
      let error;
      try { error = await revert(items.map((e) => ({ key: e.key, before: e.before }))); }
      catch { error = 'Nem sikerült visszaállítani. Ellenőrizd az internetkapcsolatot.'; }
      if (error) return error;
      items.forEach((e) => entries.delete(`${e.group}:${e.key}`));
    }
    return null;
  }

  // A kérdés. Eredmény: 'keep' (maradjon így), 'revert' (sikeresen visszaállítva) vagy 'stay' (marad az adatlapon).
  function ask() {
    return new Promise((resolve) => {
      const prevFocus = document.activeElement;
      const overlay = document.createElement('div');
      overlay.className = 'modal-overlay ui-dialog-overlay';
      overlay.innerHTML = `
        <div class="modal ui-dialog change-guard" role="dialog" aria-modal="true" aria-labelledby="change-guard-title">
          <div class="modal-head"><div><h2 id="change-guard-title">Módosítottad az adatlapot</h2>
            <p>Biztosan módosítani szeretted volna? Ez változott, amióta megnyitottad:</p></div></div>
          <div class="modal-body">
            <ul class="change-guard-list"></ul>
            <div class="form-error" data-cg-error></div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" data-cg="stay">Vissza az adatlapra</button>
            <button class="btn btn-secondary" type="button" data-cg="revert">Visszaállítás</button>
            <button class="btn btn-primary" type="button" data-cg="keep">Igen, maradjon így</button>
          </div>
        </div>`;
      const list = overlay.querySelector('.change-guard-list');
      entries.forEach((e) => {
        const li = document.createElement('li');
        const label = document.createElement('span');
        label.className = 'cg-label';
        label.textContent = e.label;
        const before = document.createElement('span');
        before.className = 'cg-before';
        before.textContent = shown(e, e.before);
        const arrow = document.createElement('span');
        arrow.className = 'cg-arrow';
        arrow.textContent = '→';
        arrow.setAttribute('aria-label', 'új érték:');
        const after = document.createElement('span');
        after.className = 'cg-after';
        after.textContent = shown(e, e.after);
        const values = document.createElement('span');
        values.className = 'cg-values';
        values.append(before, arrow, after);
        li.append(label, values);
        list.appendChild(li);
      });
      document.body.appendChild(overlay);
      requestAnimationFrame(() => overlay.classList.add('open'));

      const buttons = [...overlay.querySelectorAll('[data-cg]')];
      const errorEl = overlay.querySelector('[data-cg-error]');
      let busy = false;
      const close = (result) => {
        overlay.classList.remove('open');
        document.removeEventListener('keydown', onKey, true);
        setTimeout(() => overlay.remove(), 200);
        if (result === 'stay' && prevFocus && typeof prevFocus.focus === 'function') prevFocus.focus();
        resolve(result);
      };
      const onKey = (e) => {
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); if (!busy) close('stay'); }
        else if (e.key === 'Tab') {
          const i = buttons.indexOf(document.activeElement);
          if (e.shiftKey && i <= 0) { e.preventDefault(); buttons[buttons.length - 1].focus(); }
          else if (!e.shiftKey && i === buttons.length - 1) { e.preventDefault(); buttons[0].focus(); }
        }
      };
      document.addEventListener('keydown', onKey, true);
      overlay.addEventListener('mousedown', (e) => { if (e.target === overlay && !busy) close('stay'); });
      overlay.querySelector('[data-cg="stay"]').addEventListener('click', () => close('stay'));
      overlay.querySelector('[data-cg="keep"]').addEventListener('click', () => close('keep'));
      overlay.querySelector('[data-cg="revert"]').addEventListener('click', async (e) => {
        busy = true;
        buttons.forEach((b) => { b.disabled = true; });
        e.currentTarget.textContent = 'Visszaállítás…';
        const error = await revertAll();
        if (!error) { close('revert'); return; }
        busy = false;
        buttons.forEach((b) => { b.disabled = false; });
        e.currentTarget.textContent = 'Visszaállítás';
        errorEl.textContent = error;
        errorEl.classList.add('show');
      });
      setTimeout(() => overlay.querySelector('[data-cg="keep"]').focus(), 30);
    });
  }

  // Továbblépés: megvárja a folyamatban lévő mentést, és ha volt módosítás, kérdez. Az action csak akkor fut le,
  // ha a felhasználó nem az adatlapon maradást választotta. Igazat ad vissza, ha továbblépett.
  async function leave(action) {
    if (asking) return false;
    asking = true;
    try {
      if (pending.size) await Promise.allSettled([...pending]);
      if (entries.size && (await ask()) === 'stay') return false;
      entries.clear();
    } finally {
      asking = false;
    }
    action();
    return true;
  }

  const go = (url) => leave(() => { location.href = url; });
  const hasWork = () => entries.size > 0 || pending.size > 0;

  // Hivatkozásra kattintás (menü, morzsák, táblázatok linkjei). Új lapon nyitásnál az adatlap megmarad: nem kérdez.
  document.addEventListener('click', (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest?.('a[href]');
    if (!a || a.target === '_blank' || a.hasAttribute('download')) return;
    const href = a.getAttribute('href');
    if (!href || href.startsWith('#') || /^(javascript|mailto|tel):/i.test(href)) return;
    if (!hasWork()) return;
    e.preventDefault();
    go(a.href);
  }, true);

  // A böngésző Vissza gombja: az őr-bejegyzésről lelépve az oldal még megvan, itt lehet kérdezni.
  window.addEventListener('popstate', (e) => {
    if (e.state?.changeGuard) { onSentinel = true; return; }
    if (!onSentinel) return;
    onSentinel = false;
    const rearm = () => { try { history.pushState({ changeGuard: true }, ''); onSentinel = true; } catch { /* nem baj */ } };
    if (asking) { rearm(); return; }   // már nyitva a kérdés: maradunk
    if (!hasWork()) { history.back(); return; }
    leave(() => history.back()).then((left) => { if (!left) rearm(); });
  });

  return { group, track, record, leave, go, hasChanges: () => entries.size > 0 };
})();
