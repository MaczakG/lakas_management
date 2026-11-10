# Lakáskezelő — fejlesztési lista

Ez a dokumentum a fejlesztések nyomon követésére szolgál — **innen dolgozunk**, ezt tartjuk
naprakészen minden munkamenet elején/végén. Legfrissebb frissítés: 2026-10-02.

**Igazságforrás: a GitHub `main` ág.** Élesíteni csak a `main`-ről szabad. Az augusztusi, csak
helyben élesített munka emiatt veszett el élesben (ld. lent).

| Állapot | Fejlesztés | Megjegyzés |
|---|---|---|
| 🟡 Kód kész | Új dizájn (Yardly-minta) és ingatlan-adatlap | 2026-11-02: a felület a Yardly „Meridian” dizájnrendszerét követi a logó zöldjével (#146754 → #319B7D): Instrument Sans saját szerverről (Google Fonts kikerült, a CSP szigorodott), Lucide ikonok, csoportosított oldalsáv, fejlécek élő számlálóval. Minden ingatlannak saját adatlapja van (`property-detail.html`): helyben szerkeszthető panelek (alapadatok, bérlés és számlázás, műszaki adatok és mérőórák, tulajdonosok, jelenlegi bérlő, összegzés, megjegyzés), alul füles lista (számlák, rezsi, bérlők). Új adatlap-mezők migrációval (`PropertySheet`). **Hátravan: élesítés.** |
| ✅ Kész | Számla csak munkanapon | 2026-10-30: ha a beállított számlázási nap hétvégére vagy magyar munkaszüneti napra esik (pl. 2026.11.01. vasárnap, Mindenszentek), a számla az utána következő első munkanapon megy ki ugyanabban az órában (`Billing/BillingCalendar`); a számlázott időszak nem változik. A „Következő számlázás” oszlop a szerver számolta, munkanapra tolt időpontot mutatja. A rendeletben évente áthelyezett pihenőnapokat nem ismeri. |
| ✅ Kész | .NET 10 (LTS) és titkosított szerverlemez | 2026-10-02: a .NET 9 támogatása 2026.11.10-én megszűnik → átállás .NET 10-re (csomagok 10.0.x, Ubuntu 24.04 alapú image, nem root `app` felhasználó). A szerver root EBS-kötete KMS-sel titkosítva (új kötet: `vol-0677135b903fb7065`; a régi, titkosítatlan `vol-024f7452f2ba77d5a` lecsatolva, visszaállításhoz megtartva — pár nap múlva törölhető a titkosítás előtti kézi pillanatképekkel együtt). A régi Render-telepítés nyomai (render.yaml, onrender.com API-cím) kikerültek a kódból. **Hátravan: a Render-fiókban a szolgáltatások és a `lakaskezelo-db` adatbázis törlése (csak a fiók tulajdonosa tudja).** |
| ✅ Kész | Biztonság: kötelező e-mailes 2FA + IT-biztonsági keményítés | 2026-10-02 (élesítve): belépés jelszó + e-mailben kapott 6 jegyű kóddal (mind a 3 felhasználónak). Fiókzárolás 5 hibás jelszó/kód után 15 percre (értesítő e-maillel), IP-alapú korlát a bejelentkezési végpontokon (backend + nginx), visszavonható munkamenetek (jelszócsere/inaktiválás után a régi token azonnal érvénytelen), az SMTP-jelszó és a Mailgun-kulcs nem megy ki a böngészőbe, XSS-javítás (számla-sorszám az onclick-ben, idézőjel-biztos escapeHtml). nginx: HSTS, CSP, kattintásrablás elleni fejlécek, TLS 1.2/1.3, támogatott nginx-ág; docker: naplóforgatás, no-new-privileges. A szerverkonfiguráció mostantól a repóban: `deploy/` (élesítési leírással). AWS: SSH csak EC2 Instance Connectről, az EC2-szerepkörből kikerült az AmazonSSMFullAccess, napi + heti EBS-mentés a lakáskezelő kötetről; OS: root SSH-belépés tiltva, automatikus újraindítás biztonsági frissítés után (02:30 UTC). |
| ✅ Kész | Árfolyamok: devizánként egy, óránként frissülő sor | 2026-10-02: a napi árfolyam-előzmény (lista) helyett EUR és USD egy-egy sora, amit az óránkénti MNB-lekérdezés és a „Frissítés most” gomb felülír. A migráció a régi sorokból devizánként a legfrissebbet tartja meg. A számlázás eddig is a legutolsó árfolyamot használta, és a számla a felhasznált árfolyamot a saját szövegében őrzi, így a régi számlákat nem érinti. Élesítve 2026-10-02. |
| 🔴 Sürgős | Sikertelen szept./okt. számlák újrapróbálása | 2026-10-01: a lejárt Google-token (`invalid_grant`) miatt szeptember 1. óta minden számla `Failed` lett, egyik sem ment ki. A Google-fiók 15:33-kor újra lett csatlakoztatva, így a jelenlegi éles verzióval a Számlázás oldalon az „Újrapróbálás” már működik. |
| 🟡 Kód kész | Számla-PDF tárolás Amazon S3-ban (Google Drive helyett) | 2026-10-01: a Google Drive teljesen kikerült (Beállítások szekció, OAuth, ingatlanonkénti Drive-mappa, Dokumentumok fül). Új Beállítások szekció: S3 bucket + régió + kapcsolat-teszt. Hozzáférés az EC2-höz rendelt IAM-szereppel, kulcs nincs tárolva. Az S3-hiba nem állítja le a számla kiküldését. A régi (Drive-os) számlák PDF-je letöltéskor újragenerálódik a tárolt adatokból. **Hátravan: bucket + IAM-szerep a konzolban, commit + push, élesítés.** |
| 🟡 Kód kész | Számla csomagok oldal | 2026-10-01: `invoice-bundles.html`, havonta egy ZIP az összes kiküldött/legenerált számla PDF-jével és egy `osszesito.csv`-vel (UTF-8 BOM, pontosvessző, magyar Excelhez). A sikertelen számlák nem kerülnek bele. |
| 🟡 Kód kész | Tulajdoni hányad tulajdonosonként | 2026-10-01: `PropertyOwner.Share` (pl. „1/2”), az Ingatlan modalban adható meg. Vagy minden tulajdonosnál meg kell adni (összesen 1), vagy egyiknél sem (ekkor 1/n). A számlán a fejlécben és egy „Tulajdoni hányad” oszlopban jelenik meg. |
| ⏸️ Elhalasztva | Lakás-egyenleg nyilvántartás | 2026-10-01: a felhasználó döntése szerint egyelőre Excelben követik. Az augusztusi kód (egyenleg, fizetések, SZJA/csúszás, rezsi fajták, a zárolás megszüntetése) **nincs a `main`-en**, de a helyi git stash-ben megvan: `stash@{0}` („augusztusi egyenleg-nyilvantartas…”). Az éles adatbázisban a táblái/oszlopai megmaradtak, de a kód nem használja őket. Leírás: [egyenleg_nyilvantartas_leiras.md](egyenleg_nyilvantartas_leiras.md). |
| ✅ Kész | SMTP e-mail küldés, a 2FA kikapcsolva a tesztidőszakra | 2026-09-23 (másik munkamenet), élesítve 2026-10-01. `AuthController.TwoFactorEnabled = false` kapcsolja vissza. |
| ✅ Kész | Havi rezsi oszlopdiagram (Rezsi felvétel oldal) | Kategóriánként színezett, arányos oszlopdiagram a lakás/év választó és a hónapok táblázata között. |
| ✅ Kész | Bulk rezsi-import Excelből | Egyszeri, kézi API-hívásos import egy ingatlanhoz (55 sor), a többi ingatlan később. |
| ✅ Kész | AWS EC2 self-hosted deploy | Render → `lakaskezelo.alts.hu`, Docker Compose, Let's Encrypt (webroot), `t3.micro`, 2 GB swap. |
| ✅ Kész | App verziószám kijelzés | `GET /api/version`, sidebar footer — melyik deploy fut éppen. |
| ✅ Kész | Outlook CTA gomb javítás | Jelszó-visszaállító e-mail gombja táblázat-alapú "bulletproof button" mintára cserélve. |
| ✅ Kész | Admin jelszó-visszaállítás indítás | Felhasználók oldal, soronkénti gomb. |
| ✅ Kész | Számla PDF valódi csatolmányként | Az e-mailben a PDF melléklet, nem link. |
| ⏸️ Elhalasztva | Bérlő kiválasztása az ingatlanoknál | Jelenleg a Bérlők oldalon rendelhető ingatlanhoz bérlő; az Ingatlan modal Alapadatok tabjából egyelőre csak olvasható. Nem kezdődött el. |

## V1.1 fejlesztési pontok

2026-11-10: a tulajdonos listája. Egyesével haladunk rajta; a Státusz oszlopot menet közben frissítjük
(⚪ Nyitott → 🔵 Folyamatban → 🟡 Teszten → ✅ Élesben).

| # | Megnevezés | Leírás | Verzió | Státusz |
|---|---|---|---|---|
| 1 | Tételsor megjegyzés | Számla tételek rögzítéséhez kell tétel megjegyzés ami opcionális | V1.1 | 🟡 Teszten (2026.11.10; csak belső megjegyzés, a bizonylatra nem kerül rá) |
| 2 | Dashboard lakásválasztó | Dashboardon lakást is lehessen választani valamint ha egy lakás van kiválasztva akkor csak annak a számai jelenjenek meg | V1.1 | 🟡 Teszten (2026.11.10.2) |
| 3 | Dashboard összesített egyenleg | Dashboardon kell összesített egyenleg. A legördülő listában kell, hogy ki lehessen választani az összes lakást lehetőséget és egyenként az ingatlanokat is. Kell egy diagram is ahol az oszlopok a kifizetett és várt összeg X tengelyen lakások, Y tengelyen összeg | V1.1 | 🟡 Teszten (2026.11.10.3; teljes időszakra számol) |
| 4 | Beköltözés e-mail | Az alap beköltözési adatokat egy gombbal ki tudja küldeni a felhasználó. Nyilvántartva egy új taboldalon legyen szabadszövegesként ami a ki lehet választani, hogy kiküld vagy sem. | V1.1 | 🟡 Teszten (2026.11.10.4; az ingatlan adatlapján, egyetlen szabad szöveg) |
| 5 | Adatlap figyelmeztetés | Az adatlapon ha volt változás és a felhasználó ki akar lépni akkor szóljon, hogy volt módosított adatlapon akkor egy megerősítést kérjen, hogy biztosan módosítani szeretted volna? | V1.1 | 🟡 Teszten (2026.11.10.6; táblánként Mentés és Mégsem gomb, mentetlen adatnál kilépéskor kérdez) |
| 6 | Csatolmány rögzítés | A tételhez csatolmányt rögzíteni lehessen. A számlázáshoz csapja hozzá és a számlázásnál kérdezzen rá, hogy mellékeltekkel szeretné kiküldeni. Bérlő adatlapon lehessen beállítani, hogy menjen neki alapból vagy sem. Akinél nincsen beállítva ott kérdezzen rá és ha egynél több van akkor egy listában kérdezzen rá ne egyesével. | V1.1 | 🟡 Teszten (2026.11.10.7; beállítás nélkül az időzített küldés mellékletek nélkül megy, a listás rákérdezés kimaradt) |
| 7 | Rezsi reform | Új taboldal Rezsi tételek amikben 4 oszlop van. Megnevezés, Összeg, Fix, Tovább számlázandó ez ami bekerül a bérleti díjba és már alapból úgy van megadva az adatlapon, hogy bele van kalkulálva így nem kell belevenni mert már a bérleti díj tartalmazza.<br><br>A tételek kerüljenek bele automatikusan a hónap rezsijébe, amit behúz számlázáskor. A számlázásos hónapot létre kell hoznia ami, így nem minden hónapra kell megadni a rezsit hanem a számlázás hozza létre automatikusan vagy gombról.<br><br>A fix költségek fix díjjal belekerülnek a nem fixeket pedig ki kell tölteni. Az egyedi tételnél is lehessen tovább számlázandót is beállítani de az nem kerül bele a díjba csak tulajdonosnak elszámolás. | V1.1 | ⚪ Nyitott |
| 8 | Rezsi reform | A rezsi felvétel reformja: A rezsit a rezsi tételekből húzza be a számlázás csomagba amit előkészít a rendszer gombnyomásra vagy beállított időre; utólagos felvétellel felvett egyedi tételek továbbra is kerüljenek bele. | V1.1 | ⚪ Nyitott |
| 9 | Rezsi értesítő | A kitöltendő rezsi tételekre értesítsen a rezsi emlékeztető, hogy vannak nem fix tételek amiket ki kell tölteni. Ettől függetlenül ki kell menni és ami nem fix tétel és nem lett összeg hozzá írva az ne szerepeljen a bizonylaton mint tétel. Utólagosan ha megküldi a szolgáltató a számlát majd fel lesz rögzítve. | V1.1 | ⚪ Nyitott |
| 10 | Több e-mail, címzett | Több címzett is legyen az értesítőkhöz. Bérlőknek. | V1.1 | ⚪ Nyitott |
| 11 | Bérleti szerződés lejárat | A bérlő adatlapján legyen egy bérleti szerződés lejárat amiről menjen értesítő a beállításokban beállított e-mail címre, ahol meg lehet adni napot is, hogy mennyivel előtte szóljon. Több e-mailt is lehessen megadni. | V1.1 | ⚪ Nyitott |
| 12 | Adózás kimutatás | Dashboardon legyen két csempe ami a 10% költséghányaddal ennyit fizetne és az aktuális költségeivel mennyit fizetne. Semmi, ami tovább számlázandó nem vonható le költségként. Ezt úgy kell számolni, hogy a havi profitot kell kimutatni a két esetben. Minden ami egyedi és nem továbbszámlázandó az költségként levonható. Legyen egy figyelmeztető, hogy minden egyedileg felvitt tétel beleszámít a költségekbe. | V1.1 | ⚪ Nyitott |

## Állapot-jelölés

- 🔴 **Sürgős** — azonnali teendő, valami most nem működik.
- 🔵 **Következő** — ezen dolgozunk éppen / ez a soron következő tétel.
- 🟡 **Kód kész** — a kód elkészült és helyben ellenőrizve, élesítésre vár.
- ✅ **Kész** — élesben fut, lezárva.
- ⏸️ **Elhalasztva** — tudatosan később, nyitva tartott igény.
