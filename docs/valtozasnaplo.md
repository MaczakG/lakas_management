# Változásnapló

A verziószám dátum alapú (`ÉÉÉÉ.HH.NN`), ld. `deploy/README.md` „Ágak, verziók, kiadások”.

## Csak a teszten (staging) — még nincs élesben

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

## Élesben

- **2026.10.30** — A számla csak munkanapon megy ki (hétvégére/ünnepre eső nap → következő munkanap).
- **2026.10.02** — .NET 10; kötelező e-mailes kétlépcsős belépés, fiókzárolás, visszavonható munkamenetek,
  biztonsági fejlécek; árfolyam devizánként egy, óránként frissülő sor; titkosított szerverlemez.
