# Lakáskezelő — fejlesztési lista

Ez a dokumentum a fejlesztések nyomon követésére szolgál — **innen dolgozunk**, ezt tartjuk
naprakészen minden munkamenet elején/végén. Legfrissebb frissítés: 2026-10-02.

**Igazságforrás: a GitHub `main` ág.** Élesíteni csak a `main`-ről szabad. Az augusztusi, csak
helyben élesített munka emiatt veszett el élesben (ld. lent).

| Állapot | Fejlesztés | Megjegyzés |
|---|---|---|
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

## Állapot-jelölés

- 🔴 **Sürgős** — azonnali teendő, valami most nem működik.
- 🔵 **Következő** — ezen dolgozunk éppen / ez a soron következő tétel.
- 🟡 **Kód kész** — a kód elkészült és helyben ellenőrizve, élesítésre vár.
- ✅ **Kész** — élesben fut, lezárva.
- ⏸️ **Elhalasztva** — tudatosan később, nyitva tartott igény.
