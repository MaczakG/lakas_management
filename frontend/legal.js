// Az ÁSZF és az adatkezelési tájékoztató (aszf.html, adatkezeles.html) kitöltése a szolgáltató aktuális
// adataival és díjaival (/api/public/legal — a Platform oldalon szerkeszthető). data-legal="kulcs"
// elemek kapják az értéket; hiányzó adatnál jól látható jelölés marad, hogy feltűnjön.
(async function () {
  let info;
  try {
    const res = await fetch(`${API_BASE}/api/public/legal`);
    info = await res.json();
  } catch {
    return;
  }
  const op = info.operator || {};
  const huf = (n) => `${Math.round(n).toLocaleString('hu-HU')} Ft`;
  const priceText = `${huf(info.monthlyPricePerProperty)} / aktív ingatlan / hónap`
    + (info.vatExemption ? ' (alanyi adómentes, ÁFA nélkül)' : info.pricesIncludeVat ? ` (bruttó, ${info.vatPercent}% ÁFA-val)` : ` + ${info.vatPercent}% ÁFA`);
  const values = {
    product: info.productName,
    website: info.websiteUrl || location.origin,
    version: info.termsVersion ? info.termsVersion.replaceAll('-', '. ') + '.' : '',
    'operator.name': op.name,
    'operator.address': op.address,
    'operator.taxNumber': op.taxNumber,
    'operator.registrationNumber': op.registrationNumber,
    'operator.email': op.email,
    'operator.phone': op.phone,
    price: priceText,
    yearlyDiscount: `${Number(info.yearlyDiscountPercent).toLocaleString('hu-HU')}%`,
    trialDays: String(info.trialDays),
    graceDays: String(info.graceDays),
  };
  document.querySelectorAll('[data-legal]').forEach((el) => {
    const v = values[el.dataset.legal];
    if (v) {
      el.textContent = v;
      el.classList.remove('legal-missing');
    } else {
      el.textContent = el.dataset.fallback || '[nincs megadva]';
      if (!el.dataset.fallback) el.classList.add('legal-missing');
    }
  });
  document.querySelectorAll('[data-legal-if]').forEach((el) => { el.hidden = !values[el.dataset.legalIf]; });
  document.title = `${info.productName} — ${document.querySelector('h1')?.textContent || ''}`;
})();
