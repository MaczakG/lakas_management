# Változásnapló

A verziószám dátum alapú (`ÉÉÉÉ.HH.NN`), ld. `deploy/README.md` „Ágak, verziók, kiadások”.

## Csak a teszten (staging) — még nincs élesben

(nincs)

## Élesben

### v2026.11.02.9 — 2026. 11. 02.

- **2026.11.02.9** — Számla csomagok: a lenyíló számlalista teljes szélességű, külön keret nélkül.

### v2026.11.02.8 — 2026. 11. 02.

- **2026.11.02.8** — Számla csomagok: a hónap sorára kattintva alatta lenyílik a csomag számláinak listája
  (sorszám, ingatlan, bérlő, kiállítás, összeg, állapot, PDF-letöltés); újabb kattintásra visszazáródik.

### v2026.11.02.7 — 2026. 11. 02.

- **2026.11.02.7** — A Platform admin teljesen különálló: saját fiókok (nem a cégek felhasználói), saját belépés
  (platform-login.html: jelszó + e-mailes kód, zárolás, jelszó-beállító link, min. 12 karakteres jelszó), saját
  munkamenet. A platform-token csak a platform-végpontokat éri el, a cégek fiókjai a platformot nem; az
  alkalmazásból eltűnt a „Platform admin” gomb. Az első fiók a `PLATFORM_ADMIN_EMAIL` címmel jön létre.
- **2026.11.02.6** — Bérlő- és tulajdonos-adatlap az ingatlan-adatlap mintájára (helyben szerkeszthető
  panelek, összegzés; bérlőnél az ingatlan és a számlái, tulajdonosnál az ingatlanai hányaddal és a számlák),
  a listákból és az ingatlan-adatlapról elérhetően.
- **Beállítások / Platform admin** — A cégek Beállítások oldalán csak a rezsi-emlékeztető, a számlakibocsátó
  adatai (új: válaszcím) és a számla e-mail szövege maradt. Az e-mail küldés (Mailgun/SMTP) és a számla-tárhely
  (S3) platform-szintű lett: a cégek levelei a platform fiókján mennek, a cég nevével feladóként és a cég
  e-mail címével válaszcímként. A Platform admin külön felületet kapott (saját oldalsáv: Cégek, Fizetések,
  Díjak és fizetés, E-mail és tárhely, Szolgáltató és jogi adatok), az alkalmazásból a „Platform admin” gombbal.

### v2026.11.02.5 — 2026. 11. 02.

- **2026.11.02.5** — SaaS 4. lépés: ÁSZF (adatfeldolgozási feltételekkel) és adatkezelési tájékoztató, a
  szolgáltató adataival és a díjakkal automatikusan kitöltve (Platform oldal). Regisztrációnál kötelező az
  elfogadásuk (a változat és az időpont a cégnél tárolódik), fizetésnél a fogyasztói azonnali-teljesítés kérése.
  Platform oldal: díjmentes napok jóváírása egy cégnek, az összes díjfizetés listája. Linkek a belépési,
  regisztrációs oldalon és az oldalsávban.
- **2026.11.02.4** — SaaS 3. lépés: előfizetés. Ingatlanonkénti havi díj, éves fizetésnél kedvezménnyel,
  próbaidővel (alapár: bruttó 1990 Ft / ingatlan / hó, a Platform oldalon módosítható). Bankkártyás fizetés a Barion oldalán (a kártya a megújításokhoz elmentve), automatikus
  megújítás, minden fizetésről Billingo-számla. Sikertelen megújításnál türelmi idő, utána (és lejárt
  próbaidőnél) a fiók csak olvasható, és nem megy ki bérlői számla/emlékeztető. Előfizetés oldal (díjcsomag,
  számlázási adatok, fizetések és számlák, lemondás) és Platform oldal az üzemeltetőnek (díjszedés
  főkapcsolója, árak, ÁFA, Barion- és Billingo-kulcsok, cégek áttekintése). A díjszedés alapból kikapcsolt.
- **2026.11.02.3** — SaaS 2. lépés: önálló regisztráció (új cég, e-mail-megerősítéssel), kollégák meghívása
  e-mailben (a jelszót a meghívott maga állítja be), szerepkörök (adminisztrátor / munkatárs — a Beállításokat
  és a felhasználókat csak az adminisztrátor kezeli, a cégnek mindig marad aktív adminisztrátora). A
  rendszerlevelek (belépési kód, meghívó, megerősítés) az üzemeltető postafiókjából, a termék nevével mennek.
  Jól látható „Kilépés” gomb és a cég neve az oldalsávban. A 7 napig meg nem erősített regisztrációk törlődnek.
- **2026.11.02.2** — SaaS 1. lépés: több cég (előfizető) egy rendszerben, szigorúan elkülönített adatokkal.
  Minden adat egy céghez tartozik; a meglévő adatok az első cégbe kerülnek (neve a számlakibocsátó neve).
  A beállítások cégenként külön vannak. Automata tesztek a cégek elkülönítésére, és ezek minden PR-nál
  lefutnak (GitHub Actions).
- **2026.11.02** — Új dizájn a Yardly mintájára, a logó zöldjével; ingatlan-adatlap (helyben szerkeszthető
  panelek, számlák/rezsi/bérlők fülek), új adatlap-mezők (cím, típus, hrsz, alapterület, mérőórák, kaució…).
  Saját betűkészlet (Google Fonts nélkül). Teszt-környezet: „TESZT” sáv, e-mail csak a felhasználóknak.

### Korábban

- **2026.10.30** — A számla csak munkanapon megy ki (hétvégére/ünnepre eső nap → következő munkanap).
- **2026.10.02** — .NET 10; kötelező e-mailes kétlépcsős belépés, fiókzárolás, visszavonható munkamenetek,
  biztonsági fejlécek; árfolyam devizánként egy, óránként frissülő sor; titkosított szerverlemez.
