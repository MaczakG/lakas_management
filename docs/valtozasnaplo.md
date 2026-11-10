# Változásnapló

A verziószám dátum alapú (`ÉÉÉÉ.HH.NN`), ld. `deploy/README.md` „Ágak, verziók, kiadások”.

## Csak a teszten (staging) — még nincs élesben

- **2026.11.10.6** — Adatlap: mentés gombbal, figyelmeztetés mentetlen adatoknál (V1.1 / 5. pont, átdolgozva).
  - Az adatlap mezői már nem mentődnek maguktól. Amint egy tábla valamelyik mezője módosul, a tábla jobb felső
    sarkában megjelenik a „Mégsem” és a „Mentés” gomb, a módosított mező mellett pedig a „Nincs mentve” jelzés.
  - „Mentés”: a tábla összes módosított mezőjét egyszerre menti (Enter a mezőben ugyanezt teszi). Hibás értéknél
    nem ment, a hibát a mező mellett jelzi.
  - „Mégsem”: visszaírja az utoljára mentett értékeket (Esc a mezőben csak azt a mezőt állítja vissza).
  - Ha mentetlen adat van, és menüpontra, hivatkozásra vagy a Kilépés gombra kattintasz, a program rákérdez:
    „Vannak mentetlen adatok. Biztosan ki akarsz lépni?” (Maradok / Kilépés mentés nélkül).
  - A böngészőfül bezárásakor, frissítéskor és a böngésző Vissza gombjánál a böngésző saját figyelmeztetése
    jelenik meg (ennek a szövegét a böngésző adja).
  - Érvényes az ingatlan, a bérlő, a tulajdonos és a dokumentum adatlapján. Az ingatlan „Beköltözés” fülén a
    Mentés és a Mégsem gomb a fülsáv jobb oldalán jelenik meg; mentetlen szöveg nem küldhető ki.
  - Migráció nincs.
- **2026.11.10.5** — Figyelmeztetés módosított adatlap elhagyásakor (V1.1 / 5. pont, első változat).
  - Az azonnali mentés mellett kilépéskor felsorolta a módosításokat, és vissza lehetett állítani őket. A
    2026.11.10.6 felváltotta: élesbe már az új működés kerül.
- **2026.11.10.4** — Beköltözési tudnivalók e-mailben (V1.1 / 4. pont).
  - Az ingatlan adatlapján új „Beköltözés” fül: egyetlen szabad szöveg (pl. kapukód, wifi, mérőórák helye,
    szemétszállítás, hibabejelentés, házirend), legfeljebb 5000 karakter. A mező elhagyásakor magától mentődik.
  - „A bérlőnek kiküldhető” pipa: ha nincs bepipálva, a szöveg belső feljegyzés marad, és nem küldhető ki.
  - „Kiküldés a bérlőnek” gomb: megerősítés után e-mailben elküldi a szöveget. Címzett az ingatlan jelenlegi
    bérlője; ha leendő (még be nem költözött) bérlő is van, listából választható.
  - A fül mutatja, hogy a bérlő megkapta-e már, és mikor. Újra is kiküldhető, például ha változott a szöveg.
  - A levél a cég nevében megy, a válasz a cég e-mail címére érkezik (mint a bizonylatoknál).
  - Migráció: `MoveInInfo` (két új mező az ingatlanon, egy a bérlőn; meglévő adatot nem módosít).
- **2026.11.10.3** — Összesített egyenleg a Vezérlőpulton (V1.1 / 3. pont).
  - Új csempe: „Összesített egyenleg” (tartozás pirossal, túlfizetés zölddel, vagy rendezett), alatta a várt és a
    kifizetett összeg. Az Ingatlan választóval egy ingatlanra szűkíthető.
  - Új diagram: „Várt és kifizetett összeg ingatlanonként”. Az X tengelyen az ingatlanok, az Y tengelyen az összeg,
    ingatlanonként két oszlop (várt, kifizetett). A buborék és a „Táblázat” az ingatlan egyenlegét is mutatja.
  - Számítás, a teljes időszakra (az Év választó erre a részre nem hat):
    - várt: a kiállított bizonylatok (a sztornó negatív) és a bizonylatra váró rezsi;
    - kifizetett: a rögzített befizetések;
    - egyenleg: a kettő különbsége. Megegyezik a bérlők adatlapján látható egyenlegek összegével.
  - A bizonylathoz kötött befizetés a bizonylat ingatlanához, a többi a bérlő ingatlanához számít. Ingatlan nélküli bérlő
    befizetése csak az összesítettben szerepel.
  - Migráció nincs.
- **2026.11.10.2** — Ingatlanválasztó a Vezérlőpulton (V1.1 / 2. pont).
  - A fejlécben az Év mellett „Ingatlan” választó: „Összes ingatlan” vagy egy ingatlan (az inaktívak a lista végén,
    „(inaktív)” jelöléssel).
  - Egy ingatlant választva minden csak arra vonatkozik: a kiállított összeg, az előző évvel való összevetés, a rezsi,
    a havi diagramok, a bizonylatok állapota és a következő 30 nap eseményei.
  - A „Kiadott ingatlanok” csempe helyén ilyenkor az „Ingatlan állapota” látszik (Kiadva / Üres / Inaktív) a mai
    bérlő nevével. Az „Ingatlanonként” bontás eltűnik, a „Következő 30 nap” teljes szélességű.
  - Évváltáskor a kiválasztott ingatlan megmarad.
  - Migráció nincs.
- **2026.11.10** — Megjegyzés a rezsitételhez (V1.1 / 1. pont).
  - A rezsi-felvételi ablakban minden tételhez megadható egy nem kötelező megjegyzés (pl. mérőállás, a szolgáltatói
    számla száma), legfeljebb 500 karakter.
  - A rögzített tétel neve alatt látszik, és a tétel szerkesztésekor módosítható vagy törölhető.
  - Csak belső: a bizonylatra (PDF, bizonylat adatlapja, e-mail) nem kerül rá.
  - Migráció: UtilityEntryNote. Egy új oszlop kerül be; meglévő adatot nem módosít.

## Élesben

### v2026.11.04.3 — 2026. 11. 04.

- **2026.11.04.3** — Rezsi felvétel: az évválasztó és a hónapok táblázata sincs külön dobozban (csak a grafikon kártya).
- **2026.11.04.2** — Rezsi felvétel:
  - az ingatlan kiválasztása után nem ugrik fel magától a felvételi ablak, csak az ingatlan rezsi-felülete nyílik meg;
  - az ingatlanlista teljes szélességű, külön doboz nélküli táblázat.
- **2026.11.04** — Rezsi felvétel: először a lakás kiválasztása.
  - Ha több ingatlan van, az oldal egy listával indul. Minden sorban szerepel:
    - a bérlő;
    - a következő bizonylat hónapja és a kiállítás napja;
    - a rezsi állapota (Rögzítve / Hiányzik).

    Az inaktív ingatlanok a lista végén vannak.
  - Egy sorra kattintva megnyílik az ingatlan rezsi-felülete (hónapok, grafikon), és rögtön feljön a felvételi
    ablak a következő bizonylat hónapjára.
  - „Másik ingatlan” gombbal vagy a böngésző Vissza gombjával lehet visszamenni a listára. Az ingatlan címe a
    linkben is benne van (`?id=…`), így közvetlenül is megnyitható.
  - Ha csak egy ingatlan van, a lista kimarad.
  - Telefonon a lista kártyaszerű sorokból áll, nincs oldalirányú görgetés.

### v2026.11.03.21 — 2026. 11. 03.

- **2026.11.03.21** — A felvett, de még bizonylatra nem került rezsitétel is tartozásként jelenik meg a bérlő
  egyenlegében.
  - **Kinél:** az ingatlan jelenlegi bérlőjénél, mert ő kapja meg a következő bizonylaton.
  - **Folyószámla:** külön sor „Rezsi: Víz (2026. november)” megnevezéssel és „még nincs bizonylaton” jelöléssel.
  - **Összesítők:**
    - folyószámla: „Rezsi, még nincs bizonylaton”;
    - egyenleg-sáv: „Rezsi, bizonylatra vár”.

    A Bérlők lista egyenlege is tartalmazza.
  - **Bizonylatra kerüléskor:** a tétel kikerül ebből a sorból, és a bizonylat terhel helyette, tehát nincs dupla
    terhelés. Sztornó után a tétel újra itt jelenik meg.
  - **Kivételek:** a régi, kizárt tételek és a 36 hónapnál régebbiek nem számítanak bele.
  - **Lejárt hátralék:** a bizonylatra váró rezsi nem lehet lejárt, ezért nem számít bele.

### v2026.11.03.20 — 2026. 11. 03.

- **2026.11.03.20** — Egyenleg-sáv a bérlő adatlapján, a fejléc alatt.
  - Nagy számmal mutatja az egyenleget, színnel jelölve: tartozás (piros), túlfizetés (zöld), rendezett.
  - Mellette:
    - a kiállított és a befizetett összeg;
    - a lejárt hátralék (a fizetési határidőn túli, ki nem fizetett bizonylatokból, legfeljebb a teljes tartozás);
    - az utolsó befizetés dátuma és összege.
  - Gyorsgombok: „Befizetés rögzítése” és „Folyószámla”, ez utóbbi az Egyenleg fülre ugrik.
  - Telefonon az adatok egymás alá kerülnek, a négy adat két oszlopban.
  - Az Összegzés panelről az Egyenleg sor kikerült, mert a sáv mutatja.
- **2026.11.03.19** — Fizetés rögzítése a bizonylatra dátummal és összeggel, részletekben is.
  - A „Fizetés rögzítése” ablak (pipa gomb a bizonylattáblákban, gomb az adatlapon) kéri:
    - a dátumot (alapból ma);
    - a befizetett összeget (alapból a hátralék);
    - egy nem kötelező megjegyzést.

    Az ablak mutatja a végösszeget, az eddig befizetett összeget és a hátralékot.
  - Ha a bérlő fizeti az SZJA-t, a teljes összeg az SZJA-mentes végösszeg (a bizonylaton az „SZJA levonás (15%)” sor már
    levonja). Az ablakban ilyenkor „Végösszeg (SZJA-mentes)” látszik.
  - Állapot:
    - „Részben fizetve”, amíg a befizetések nem érik el a végösszeget;
    - utána „Fizetve” az utolsó részlet dátumával.

    Az adatlap a befizetéseket egyenként is listázza.
  - Minden befizetés a bérlő Egyenleg fülén is megjelenik, a bizonylatra hivatkozva. Ha ott törlöd, a bizonylat
    fizetettsége újraszámolódik. „Befizetések visszavonása”: a bizonylat összes befizetését törli.
  - Migráció: InvoicePartialPayments (befizetett összeg a bizonylaton, a meglévő befizetésekből kitöltve).
- **2026.11.03.18** — A bizonylat fizetettre állítható, dátummal.
  - A bizonylattáblákban pipa gomb, a bizonylat adatlapján „Fizetettre állítás” gomb. A fizetés napja alapból a mai
    nap, átírható; jövőbeli dátum nem adható meg.
  - A fizetett bizonylat állapota „Fizetve ÉÉÉÉ. HH. NN.”, az adatlapon külön „Fizetve” sor is van.
  - A bizonylat végösszege befizetésként a bérlő egyenlegébe is bekerül, a bizonylatra hivatkozva.
  - Visszavonás: az adatlapon a „Fizetés visszavonása” gombbal, vagy a befizetés törlésével az Egyenleg fülön. Mindkét
    esetben a jelölés és a befizetés is megszűnik.
  - Migráció: InvoicePaid. Csak új oszlopok kerülnek be; meglévő adatot nem módosít.
- **2026.11.03.17** — Egyenlegkövetés, lakbérfizetés módja és SZJA a bérlőnél, tételenkénti rezsi-zárolás.
  - Rezsi: a bizonylatra került tétel zárolt, amíg a bizonylatot nem sztornózzák. Sztornó után újra szerkeszthető, és a
    következő bizonylatra kerül. Zárolt tételnél látszik, melyik bizonylaton szerepel.
  - Utólagos tétel: már bizonylatolt hónapra is felvehető tétel, legfeljebb 36 hónapra visszamenőleg. Nem zárolt, és a
    következő bizonylatra kerül külön sorként, a hónapjával, pl. „Víz (2026. szeptember)”.
  - Bérlő adatlap → Bérlet:
    - „Lakbér fizetése”: előre (a bizonylaton a tárgyhónap) vagy utólag (az előző hónap), pl. „Bérleti díj (2026. október)”;
    - „SZJA-t fizeti” (csak magánszemély bérbeadónál): ha a bérlő, a bizonylaton „SZJA levonás (15%)” sor csökkenti a lakbért.
  - Egyenleg: a bérlő adatlapján új „Egyenleg” fül a kiállított bizonylatokkal (a sztornó negatív), a befizetésekkel és
    futó egyenleggel. „Befizetés rögzítése” gomb (dátum, összeg, megjegyzés), befizetés törölhető. Az egyenleg az
    Összegzés panelen és a Bérlők listában is látszik (tartozás pirossal, túlfizetés zölddel).
  - Javítás: a bizonylat az időszakban ott lakó bérlőé; korábban a kiköltözött bérlő is megkaphatta.
  - Migráció: BalanceAndLateUtilities. Új oszlopok és TenantPayments tábla. A meglévő rezsitételek az időszakuk aktív
    bizonylatához kötődnek (zároltak). A korábbi hónapok soha nem bizonylatolt tételei nem kerülnek utólag a következő
    bizonylatra; a sztornózott időszakéi igen.
- **2026.11.03.16** — Cégeknek: Számlázz.hu vagy Billingo összekötés.
  - Regisztrációkor választható: magánszemélyként vagy cégként / egyéni vállalkozóként adsz bérbe. A Beállításokban később is átállítható.
  - Cégnél a Beállítások → „Bérbeadó és számlázó” részben köthető össze a számlázó:
    - Billingo: API-kulcs és számlatömb;
    - Számlázz.hu: Számla Agent kulcs, nem kötelező előtag;
    - kapcsolatpróba;
    - alapértelmezett áfa a bérleti díjra és a rezsire (TAM, AAM, 27%, 18%, 5%, 0%), ingatlanonként az adatlapon felülírható.
  - Összekötés után a havi díjról valódi számla készül a számlázóban (saját sorszám, NAV-bejelentés):
    - a PDF-et onnan vesszük át;
    - a levelet a Lakáskezelő küldi;
    - a sztornó is a számlázóban készül.
  - Kiállított számla nem készíthető újra, javításhoz sztornózni kell.
  - A kulcsok a felületről nem olvashatók vissza.
  - Javítás: a sztornó mindig pontosan az eredeti végösszegét nullázza. Ha az eredeti tételei nem adják ki, egy kiegyenlítő sor kerül rá. A tesztkörnyezet mintabizonylatainál a rezsi sorok hiányoztak, ezt a mintaadatokban is javítottam.
  - Migráció: InvoicingProviders. Csak új oszlopok kerülnek be; meglévő adatot nem módosít.
- **2026.11.03.15** — „Számla” helyett mindenhol „bizonylat”, mert magánszemély bérbeadó nem állít ki számlát, csak
  bizonylatot:
  - menü, oldalcímek, gombok, fülek, táblák, ablakok és üzenetek: például „Bizonylatok” menüpont, „Bizonylat készítése”, „Sztornó bizonylat”;
  - a kiállítás szavai is átírva: „Kiállítás napja” a „Számlázási nap” helyett, „Következő kiállítás”, „Kiállítva 2026-ban”;
  - a bérlőnek küldött levél alapértelmezett szövege, a rezsi emlékeztető, a havi csomag (bizonylatok-ÉÉÉÉ-HH.zip, „Bizonylatszám” oszlop).

  Az e-mail sablonban az új helyőrző a {bizonylatszám}; a régi {számlaszám} is működik tovább. Nem változott: a
  bankszámla, a rezsi-szolgáltatók számlái (dokumentumtípus) és a saját előfizetés Billingo-számlái.
- **2026.11.03.14** — Az ikonok a Phosphor Icons készletből jönnek (MIT licenc: ingyenes, a felületen semmit nem kell
  feltüntetni). A Flaticon ikonok és a „Uicons by Flaticon” forrásmegjelölés mindenhonnan kikerültek (menü lábléce,
  platform menü). A legördülő nyíl, a kiválasztott elem pipája és az állapotcímkék ikonjai is lecserélődtek.
  A rezsi emlékeztető e-mail is a következő számla időszakát nézi (nem az aktuális hónapot): ingatlanonként a
  számlázás előtti utolsó határnapon (Beállítások → Rezsi emlékeztető, pl. 25 → dec. 1-i számlázásnál nov. 25.)
  szól, ha arra az időszakra még nincs rezsi; a levélben ingatlanonként az időszak és a számlázás ideje is szerepel.
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
  (✕ helyett), a vissza/tovább nyilak és a legördülő listák nyila, pipája. Az ikonok saját szerverről jönnek
  (a 2026.11.03.14-től Phosphor Icons).
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
