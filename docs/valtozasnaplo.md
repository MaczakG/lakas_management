# Változásnapló

A verziószám dátum alapú (`ÉÉÉÉ.HH.NN`), ld. `deploy/README.md` „Ágak, verziók, kiadások”.

## Csak a teszten (staging) — még nincs élesben

- **2026.11.03.13** — Archiválás helyett sztornózás: a kiállított számla nem törölhető és nem archiválható, csak
  külön sztornó bizonylat készíthető hozzá (a számla sorában vagy adatlapján a Sztornózás gombbal). A sztornó
  bizonylat az eredeti sorait negatív összeggel tartalmazza, saját sorszámmal (pl. FUT-2026-011-S), az eredetire
  hivatkozva; ha az eredeti ki volt küldve, a bérlő e-mailben megkapja. Jelölés: „Sztornózott” (eredeti) és
  „Sztornó”. Mindkettő bekerül a havi csomagba (ZIP és összesítő; a sztornó negatív, így az összesen helyes).
  Sztornózás után az időszak rezsije szerkeszthető, és új számla készíthető rá („/2” sorszámmal). A korábban
  archivált számlák sztornózottak lettek, és elkészült hozzájuk a sztornó bizonylat (migráció: InvoiceStorno).
  Új: saját adatlap minden számlának. Bal oldalon:
  - az adatok;
  - a tételek;
  - a kapcsolódó bizonylatok (eredeti, sztornó, új számla).

  Jobb oldalon a PDF előnézete 60%-on. Megnyitás: a számla sorszámára kattintva.
  A „Számla csomagok” menüpont neve „Számlák”, számla ikonnal. A címkék („pillek”) doboz és kapszula nélkül:
  az állapot színes szöveg egy kis ikonnal, a szűrők aláhúzott fülként, a számlálók sima számként jelennek meg.
  Az ÁSZF-ből kikerült a Flaticon-forrásmegjelölés (a menü alján megmaradt).
- **2026.11.03.12** — Saját adatlap minden dokumentumnak (a listában a névre kattintva). Bal oldalon:
  - az adatok, helyben szerkeszthetően: név, típus, kapcsolódó ingatlan, bérlő és tulajdonos, megjegyzés; mellettük a fájl, a formátum, a méret, a verzió és a feltöltés adatai;
  - a verziók listája;
  - a napló: ki mikor töltötte fel, nézte meg, töltötte le vagy módosította.

  Jobb oldalon az előnézet, alapból 60%-os nagyítással (100% = valódi méret), +/− gombokkal, oldalszámmal.
  Fejléc-műveletek: Letöltés, Új verzió (a korábbi verziók megmaradnak, visszanézhetők és letölthetők), Törlés.
  A dokumentumtípusok szerkeszthetők: új típus, átnevezés, sorrend, törlés („Típusok” gomb, vagy a típus-választó
  „Típusok szerkesztése…” eleme). A Dokumentumok fül Feltöltés gombja a fülsáv jobb sarkába került (mint a Rezsi fülön).
  Migráció: DocumentVersions, DocumentEvents, DocumentTypes (a meglévő dokumentumok 1. verzióként, típussal megmaradnak).
- **2026.11.03.11** — Minden ikon a Flaticon ikonkészletéből (Uicons, regular rounded): menü, gombok, fülek,
  táblák, a Dokumentumok fájltípus-jelei (PDF, Word, Excel, kép, CSV), a keresőmezők, a bezáró és eltávolító gombok
  (✕ helyett), a vissza/tovább nyilak és a legördülő listák nyila, pipája. Az ikonok saját szerverről jönnek; a
  Flaticon License szerinti forrásmegjelölés („Uicons by Flaticon”) a menü láblécében és az ÁSZF-ben szerepel.
- **2026.11.03.10** — Dokumentumok az ingatlan, bérlő és tulajdonos adatlapján (új „Dokumentumok” fül): szerződés,
  szkennelt rezsi számla, jegyzőkönyv, fotó vagy bármilyen PDF, kép, Word, Excel, szöveg (fájlonként legfeljebb 15 MB).
  Feltöltés gombbal vagy behúzással, egyszerre több fájl, folyamatjelzővel; a kategóriát a fájlnévből javasolja.
  Egy dokumentum több adatlaphoz is kapcsolható (pl. a bérleti szerződés az ingatlannál és a bérlőnél is látszik).
  Előnézet saját ablakban: PDF oldalanként nagyítással, kép, szöveg; lapozás a dokumentumok között. Átnevezés,
  kategória, megjegyzés módosítása, törlés. A fájlok az S3 tárhelyre kerülnek, ha az be van állítva, különben az
  adatbázisba (migráció: Documents, DocumentContents). A feltöltött fájl típusát a tartalma alapján is ellenőrizzük.
- **2026.11.03.9** — A kiállított számlák nem törölhetők, csak archiválhatók (a bizonylat és a PDF megmarad, de nem
  számít bele az összesítőkbe, a havi csomagba és a Vezérlőpultba; a táblákban „Archivált” címkével, halványan
  látszik). Archiválás után az időszak rezsi tételei újra szerkeszthetők, és új számla készíthető rá „/2”, „/3”
  végződésű sorszámmal (migráció: Invoices.ArchivedAt, ArchivedByUserId; az egyediség csak a nem archivált
  számlákra vonatkozik). A rezsi ablak alapból egy üres sorral nyílik, a meglévő tételek soron belül
  szerkeszthetők. A Számla csomagok év szűrője egyszerű legördülő lista, csak azokkal az évekkel, amelyekre van számla.
- **2026.11.03.8** — Arculat: mély erdőzöld (#1E5B47) fő szín, grafitszürke szöveg, meleg-szürke felületek. Emberibb,
  kevésbé „gépi” felület: nincsenek nagybetűs címkék a címek fölött, a táblafejlécek, menücsoportok és státuszcímkék
  mondatkezdők, a díszítő pöttyök eltűntek (a számlálók egy mondatban: „4 aktív, 3 hiányzó rezsi…”), a felületek
  laposak, árnyék nélkül. A szövegekből kikerült a hosszú gondolatjel és a „·” elválasztó (természetes írásjelek
  helyette; a jogi szövegekben a magyar „–” gondolatjel), az oldalcímek „Oldal | Lakáskezelő” alakúak, a Vezérlőpult
  kevesebb magyarázó alcímet kapott.
- **2026.11.03.7** — Arculat-finomítás: fekete és narancs helyett zafírkék (#2257E6) fő szín (gombok, linkek, aktív
  elemek, márkajel, diagramok), sötét tengerészkék (#13233F) szöveg, hideg kékesszürke felületek és világos
  oldalsáv. A diagramok kategóriaszínei narancs és citromsárga nélkül (színtévesztésre ellenőrizve).
- **2026.11.03.6** — Új arculat: Midnight Black (#02070A) szöveg, sötét oldalsáv és elsődleges gombok; Electric
  Blue (#0297DE) aktív elemek, fókusz és diagramok; Cantaloupe Orange (#FC734D) kiemelés (márkajel, aktív
  menüpont jelölője). Új betűtípus: Geist és Geist Mono (saját szerverről, magyar ékezetekkel). A színek
  kontrasztja WCAG AA szerint ellenőrizve.
- **2026.11.03.5** — A „Számlázás” menüpont megszűnt: számlát az ingatlan adatlapján és a Számla csomagok oldalon a
  „Számla készítése” gombbal lehet készíteni (alapból a következő számla időszakára; meglévő számlánál előbb
  rákérdez). A sikertelen számlák a Számla csomagok hónap-sorát lenyitva újrapróbálhatók, és minden számlatáblából
  (adatlapok, csomagok) törölhetők. A böngésző saját ablakai (figyelmeztetés, megerősítés, beírás) és a natív
  legördülő listák helyett mindenhol a Lakáskezelő saját, egységes dizájnú ablakai és listái jelennek meg
  (egérrel, érintéssel és billentyűzettel is).

## Élesben

### v2026.11.03.4 — 2026. 11. 03.

- **2026.11.03.4** — Számla generálása folyamatjelzővel (Számlázás: „Számla generálása most”, „Újrapróbálás”):
  a sáv a valós lépéseket követi (adatok, PDF, tárhely, e-mail, rögzítés), a végén az eredménnyel. A számlán a
  rezsi tételesen látszik (minden rögzített tétel külön sor, pl. Áram, Gáz, Közös költség), alatta „Összesen”
  sorral; a sorok sorrendje rögzített (migráció: InvoiceLines.Position). A már kiküldött számlák nem változnak.
- **2026.11.03.3** — A Beállítások oldal tetején „Előfizetés” rész: állapot (díjmentes / üzemeltető / próbaidő /
  aktív / fizetés sikertelen / csak olvasható), érvényesség, aktív ingatlanok, díj, mentett kártya, és gomb az
  Előfizetés oldalra (fizetés, kártya, számlák).
- **2026.11.03.2** — A „Rezsi: Rendben / Hiányzik” jelzés a következő számla időszakát nézi, nem az aktuális
  hónapot (pl. dec. 5-i számlázásnál a decemberi rezsinek kell meglennie — akár már november végén rögzítve).
  Az ingatlanlistán látszik, melyik hónap számlájához; az adatlapon és a Rezsi felvételben is ez az alapértelmezett
  hónap, a hónaptáblázat jelöli a „következő számla” hónapját. A Rezsi felvétel „Havi rezsi bontás” diagramja
  megújult (a Vezérlőpulttal egyező stílus: tengely, tételenkénti színek, buborék, kattintásra megnyíló hónap).
- **2026.11.03.1** — A Vezérlőpult a kezdőoldal: belépés után, a főoldalról és az oldalsáv logójáról is ide jut
  a felhasználó.

### v2026.11.03 — 2026. 11. 03.

- **2026.11.03** — Új „Vezérlőpult” menüpont diagramokkal, évválasztóval: kiszámlázott összeg (az előző év azonos
  időszakához mérve), rezsi összesen, kiadott ingatlanok aránya; havi kiszámlázott összeg az előző évvel
  összevetve; számlák állapota (kiküldve / nem küldött / sikertelen); ingatlanonkénti összegek; rezsi havonta
  kategóriánként; közelgő események a következő 30 napban (számla-kiküldések, be- és kiköltözések). Minden
  diagram mellett táblázatos nézet is van.

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
