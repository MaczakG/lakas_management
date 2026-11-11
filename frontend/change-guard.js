// Mentés gombbal szerkeszthető adatlapok (ingatlan, bérlő, tulajdonos, dokumentum) és figyelmeztetés mentetlen
// adatokkal való kilépéskor.
//
// A mezők nem mentődnek maguktól. Amint egy tábla (panel) valamelyik mezője módosul, a tábla jobb felső sarkában
// megjelenik a „Mégsem” és a „Mentés” gomb: a Mentés a tábla összes módosított mezőjét egyszerre menti, a Mégsem
// visszaírja az utoljára mentett értékeket.
//
// Ha mentetlen adat van, és a felhasználó továbblépne:
//   - a programon belül (hivatkozás, menü, Kilépés gomb): saját ablak kérdez rá;
//   - a böngészőfül bezárásakor, frissítéskor, a böngésző Vissza gombjánál: a böngésző saját figyelmeztetése jön
//     (ennek a szövegét a böngésző adja, nem írható át).
//
// Használat:
//   const sheet = ChangeGuard.sheet({ getValue, parse, format, save });   sheet.bind(fields);
//   ChangeGuard.watch(() => vanMentetlen);     // más mentetlen állapot (pl. a Beköltözés fül szövege)
//   ChangeGuard.go(url) / ChangeGuard.leave(() => …) / ChangeGuard.allowLeave()
window.ChangeGuard = (function () {
  const watchers = [];   // () => bool: van-e mentetlen adat
  let asking = false;
  let leaving = false;   // a felhasználó már jóváhagyta a kilépést (a böngésző ne kérdezzen újra)

  const isDirty = () => watchers.some((w) => { try { return w(); } catch { return false; } });
  const watch = (fn) => { watchers.push(fn); };
  const allowLeave = () => { leaving = true; };

  // Továbblépés a programon belül. Az action csak akkor fut le, ha nincs mentetlen adat, vagy a felhasználó a
  // kilépést választotta. Igazat ad vissza, ha továbblépett.
  async function leave(action) {
    if (asking) return false;
    if (isDirty()) {
      asking = true;
      let ok = false;
      try {
        ok = await uiConfirm('Vannak mentetlen adatok. Biztosan ki akarsz lépni?',
          { title: 'Mentetlen adatok', confirmText: 'Kilépés mentés nélkül', cancelText: 'Maradok', danger: true });
      } finally {
        asking = false;
      }
      if (!ok) return false;
    }
    leaving = true;
    action();
    return true;
  }
  const go = (url) => leave(() => { location.href = url; });

  // Hivatkozásra kattintás (menü, morzsák, táblázatok linkjei). Új lapon nyitásnál az adatlap megmarad: nem kérdez.
  document.addEventListener('click', (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest?.('a[href]');
    if (!a || a.target === '_blank' || a.hasAttribute('download')) return;
    const href = a.getAttribute('href');
    if (!href || href.startsWith('#') || /^(javascript|mailto|tel):/i.test(href)) return;
    if (!isDirty()) return;
    e.preventDefault();
    go(a.href);
  }, true);

  // Fül bezárása, frissítés, Vissza gomb: a böngésző saját „biztosan elhagyod az oldalt?” ablaka.
  window.addEventListener('beforeunload', (e) => {
    if (leaving || !isDirty()) return;
    e.preventDefault();
    e.returnValue = '';
  });

  // ---------- Mentés / Mégsem a táblák jobb felső sarkában ----------

  // getValue(field): a mező utoljára mentett értéke; parse(field, szöveg): { value } vagy { error };
  // format(field, value): ahogy mentés után a mezőben áll (lista és többsoros mező kivételével);
  // save(changes): [{ field, value }] mentése egy kéréssel, hibánál az üzenetet adja vissza.
  function sheet({ getValue, parse, format, save }) {
    const items = new Map();   // path → { field, input, state, panel, saved }
    const same = (a, b) => String(a ?? '') === String(b ?? '');

    function dirty(item) {
      if (!item.input.isConnected || item.input.value === item.saved) return false;
      if (item.input.tagName === 'SELECT') return true;
      const parsed = parse(item.field, item.input.value);
      return !!parsed.error || !same(parsed.value, getValue(item.field));
    }
    const panelItems = (panel) => [...items.values()].filter((i) => i.panel === panel && i.input.isConnected);
    watch(() => [...items.values()].some(dirty));

    function setState(item, kind, text, title) {
      item.state.className = `istate${kind ? ` ${kind}` : ''}`;
      item.state.textContent = text || '';
      item.state.title = title || '';
      if (kind === 'err') item.input.setAttribute('aria-invalid', 'true'); else item.input.removeAttribute('aria-invalid');
    }

    function actions(panel) {
      let box = panel.querySelector(':scope > .panel-head > .panel-save');
      if (box) return box;
      box = document.createElement('div');
      box.className = 'panel-save';
      box.hidden = true;
      box.innerHTML = '<button class="btn btn-secondary" type="button" data-panel-cancel>Mégsem</button>'
        + '<button class="btn btn-primary" type="button" data-panel-save>Mentés</button>';
      box.querySelector('[data-panel-cancel]').addEventListener('click', () => cancelPanel(panel));
      box.querySelector('[data-panel-save]').addEventListener('click', () => savePanel(panel));
      panel.querySelector(':scope > .panel-head').appendChild(box);
      return box;
    }

    function refresh(panel) {
      if (!panel) return;
      const isDirtyPanel = panelItems(panel).some(dirty);
      actions(panel).hidden = !isDirtyPanel;
      panel.classList.toggle('is-dirty', isDirtyPanel);
    }

    function cancelPanel(panel) {
      panelItems(panel).forEach((item) => {
        item.input.value = item.saved;
        setState(item, '', '');
      });
      refresh(panel);
    }

    async function savePanel(panel) {
      const box = actions(panel);
      if (box.dataset.busy) return false;
      const changed = panelItems(panel).filter(dirty);
      if (!changed.length) { refresh(panel); return true; }
      const changes = [];
      let firstInvalid = null;
      changed.forEach((item) => {
        const parsed = parse(item.field, item.input.value);
        if (parsed.error) { setState(item, 'err', parsed.error); firstInvalid = firstInvalid || item; return; }
        changes.push({ field: item.field, value: parsed.value, item });
      });
      if (firstInvalid) { firstInvalid.input.focus(); return false; }

      box.dataset.busy = '1';
      box.querySelectorAll('button').forEach((b) => { b.disabled = true; });
      changed.forEach((item) => setState(item, '', 'Mentés…'));
      let error;
      try { error = await save(changes.map(({ field, value }) => ({ field, value }))); }
      catch { error = 'Nem sikerült menteni. Ellenőrizd az internetkapcsolatot.'; }
      delete box.dataset.busy;
      box.querySelectorAll('button').forEach((b) => { b.disabled = false; });
      if (error) {
        changed.forEach((item) => setState(item, 'err', 'Hiba', error));
        return false;
      }
      changes.forEach(({ field, value, item }) => {
        if (item.input.tagName === 'INPUT' || field.multiple) item.input.value = format(field, value);
        item.saved = item.input.value;
        setState(item, 'ok', 'Mentve');
        setTimeout(() => { if (item.state.textContent === 'Mentve') setState(item, '', ''); }, 2000);
      });
      refresh(panel);
      return true;
    }

    // A kirajzolt mezők bekötése (újrarajzolás után újra hívható).
    function bind(fields) {
      const panels = new Set();
      fields.forEach((field) => {
        const input = document.getElementById(`f-${field.path.replace(/\./g, '-')}`);
        if (!input) return;
        const panel = input.closest('.panel');
        const state = document.querySelector(`[data-state="${field.path}"]`);
        const item = { field, input, state, panel, saved: input.value };
        items.set(field.path, item);
        if (panel) panels.add(panel);
        const changed = () => {
          if (item.state.classList.contains('err')) setState(item, '', '');
          if (dirty(item)) setState(item, 'dirty', 'Nincs mentve');
          else if (item.state.classList.contains('dirty')) setState(item, '', '');
          refresh(panel);
        };
        input.addEventListener('input', changed);
        input.addEventListener('change', () => {
          changed();
          // A mező elhagyásakor a hibás érték rögtön látszik (mentés nélkül is).
          if (input.tagName !== 'SELECT' && dirty(item)) {
            const parsed = parse(field, input.value);
            if (parsed.error) setState(item, 'err', parsed.error);
          }
        });
        input.addEventListener('keydown', (e) => {
          if (e.key === 'Enter' && input.tagName === 'INPUT') { e.preventDefault(); if (panel) savePanel(panel); }
          if (e.key === 'Escape' && input.value !== item.saved) { e.stopPropagation(); input.value = item.saved; changed(); }
        });
      });
      panels.forEach(refresh);
    }

    return { bind };
  }

  return { sheet, watch, leave, go, allowLeave, isDirty };
})();
