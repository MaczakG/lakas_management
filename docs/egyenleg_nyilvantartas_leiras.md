# Lakás-egyenleg nyilvántartás — fejlesztési leírás

## 1. Háttér és cél

Jelenleg a bérleti díj és a rezsi elszámolása (2890 Tata, I és II épület excel-nyilvántartása alapján)
ingatlanonként, hónapra bontva **két, egymástól független, kumulált egyenleget** vezet:

- **Társasházi egyenleg**: mennyit ír elő a társasházkezelő, és mennyit fizetünk ki ténylegesen.
- **Bérlői egyenleg**: mennyit kell a bérlőnek fizetnie, és mennyi érkezik be ténylegesen.

A cél, hogy ez a két egyenleg beépüljön a Lakáskezelő alkalmazásba, a meglévő **Rezsi felvétel**
oldal bővítéseként — hónapra pontosan lebontva és kumulálva is.

## 2. Fogalmak

| Fogalom | Jelentés | Állapot |
|---|---|---|
| Előirányzat | A társasházkezelő által előírt havi költség (közös költség, felújítási alap, lift stb.) | Már megvan (rezsi tételek) |
| Fizetve (társasháznak) | Amit ténylegesen kifizetünk a társasháznak | **Új** |
| Társasházi egyenleg | Előirányzat − Fizetve, hónaponként és kumulálva | **Új** |
| Fizetendő (bérlő felé) | A kiállított számla teljes (bruttó) összege — **bérleti díj és rezsi együtt**, nem csak a rezsi rész | Már megvan (Invoice.AmountTotal) |
| Beérkezett (bérlőtől) | A bérlőtől ténylegesen befolyt **teljes** összeg (bérleti díj + rezsi együtt) — két részből áll (lásd 4. pont) | **Új** |
| Bérlői egyenleg | Fizetendő − Beérkezett, hónaponként és kumulálva | **Új** |

## 3. Adatmodell-bővítés

### Tenant (Bérlő) — új mezők

- `SzjaWithholdingEnabled` (igen/nem, alapból nem)
- `SzjaWithholdingRate` (%, alapból 15%, csak akkor releváns, ha az előző igen)
- `UtilityBillingOneMonthLag` (igen/nem, alapból nem) — ha aktív, a bérlő számláján mindig a
  *megelőző* hónap rezsi tételei jelennek meg (nem a folyó hónapé). Bérlőnkénti beállítás, mert nem
  minden bérlőnél/ingatlannál egyforma a minta.

### Új entitás — havi fizetés-nyilvántartás (property + hónap szinten)

- PropertyId, Year, Month
- `PaidToCondo` — ténylegesen kifizetett összeg a társasháznak
- `ReceivedFromTenant` — ténylegesen beérkezett összeg a bérlőtől (a tényleges banki/készpénz rész)
- `Note` — szabad szöveges megjegyzés utólagos korrekciókhoz (pl. „a májusi közös költség utólag
  csökkent 40.688-ról 38.866-ra”)

A SZJA-levonás összege nem tárolt adat — a Tenant beállítása és az adott havi számla lakbér-sora
alapján mindig számolt érték.

### Rezsi tétel (meglévő) — új mező

- `Note` (szöveg, opcionális) — pl. „elszámolás: 2026.01.31–07.22” — arra való, hogy egy utólag
  (később, pontos leolvasás alapján) érkező korrekciós tétel jelezze, ténylegesen melyik
  időszakra vonatkozik. **Nincs szükség külön entitásra**: a valós excel-adatok szerint egy
  utólagos korrekció egyszerűen a *jelenlegi, még nyitott hónap* rezsi tételei közé kerül —
  ugyanabba a kategóriába (pl. Fűtés, Vízdíj), nem egy külön "elszámolás" sorba, és nem íródik
  vissza a régi, már lezárt hónapba.

  Konkrét példa a 2/16 lakásnál: a szeptemberi hónapban felvett „Fűtés” (4080 Ft) és „Vízdíj
  meleg” (2040 Ft) tételek megjegyzése „2026.01.31–07.22” — vagyis egy fél évet átfogó tényleges
  elszámolás, ami a feldolgozás hónapjában (szeptember) jelenik meg, nem az érintett hónapokban.

### Rezsi fajta (ingatlanonkénti lista) — **MÁR MEGVALÓSÍTVA** (2026.08.25)

A 4. pont 5. szabálya (kategóriánkénti, részletes vezetés) alapjaként minden ingatlanhoz külön
felvehető, karbantartható **rezsi fajta lista** tartozik — ez már elkészült, megelőlegezve a
lenti üzleti szabályt:

- Új entitás: `UtilityType` (Id, PropertyId, Name, SortOrder) — ingatlanonként egyedi név,
  törléskor kaszkádban törlődik az ingatlannal együtt.
- Karbantartás: az Ingatlan modal **Rezsi** tabjának tetején, egyszerű hozzáadás/törlés listaként
  (`GET/POST /api/properties/{id}/utility-types`, `DELETE .../{typeId}`).
- Felhasználás: a rezsi tétel felviteli popup (`utility-modal.js`) Megnevezés mezője ebből a
  listából kínál javaslatot (HTML `datalist`) — **nem szigorú legördülő**, mert az utólagos
  korrekciós tételeknél (ld. fent, 4. pont) továbbra is szabad szöveges megnevezés kell hogy
  maradjon lehetséges. A `UtilityCostEntry.Label` mező emiatt változatlanul szabad szöveg marad,
  a fajta-lista csak a felület gyorsítására szolgáló javaslat, nem adatmodell-szintű
  kényszerítés/kapcsolat.

## 4. Üzleti szabályok

1. **A számlázás alap-logikája nem változik.** A bérleti díj + rezsi összege továbbra is a teljes
   (bruttó) összeg — SZJA-levonás nélkül. Példa: 100-as lakbérnél a számla összege marad 100. (Az
   aktuális bérlői egyenleg ehhez képest, külön lépésként, hozzáadódik/levonódik — ld. 8. pont.)
2. **SZJA-levonás a beérkezés-nyilvántartásban.** Ha a bérlőnél aktív a levonás, a rendszer
   automatikusan kiszámolja a levonás összegét (fizetendő × ráta), és csak a fennmaradó részt várja
   tényleges befizetésként. Példa: 100-as lakbér, 15%-os SZJA → 15 a levonás, 85 a várt tényleges
   befizetés — a kettő együtt rendezi a 100-at, az egyenleg nem mutat hiányt.
3. **Egy hónapos csúszás a rezsi számlázásban — bérlőnkénti beállítás.** Ha a bérlőnél aktív a
   `UtilityBillingOneMonthLag`, a számlagenerálás nem a folyó, hanem a *megelőző* hónap rezsi
   tételeit veszi figyelembe (a folyó havi tételek csak a következő havi számlán jelennek meg).
   Alapból kikapcsolva marad (a mai, folyó hónapos logika), mert nem minden bérlőnél/ingatlannál
   ugyanaz a minta.
4. **Utólagos/elmaradt korrekciós tétel** (pl. ha a szolgáltató késve küldi a tényleges elszámolást
   egy korábbi, akár több hónapot átfogó időszakra): rezsi tételként kerül fel, a `Note` mezőben
   jelezve, melyik időszakra vonatkozik ténylegesen — akár a jelenlegi, akár egy már korábbi
   (számlázott) hónapba, lásd a 7. pontot.
5. **Minden tételt részletesen, kategóriánként kell vezetni** — nem összevont "rezsi" összeg, hanem
   a társasházkezelő tényleges bontása szerint (közös költség, felújítási alap, üzemeltetés, lift,
   portaszolgálat, kertgondozás, fűtés/vízmelegítés átalány, vízdíj átalány, áram stb.), ahogy ma is
   történik a rezsi tételeknél — ez az egyenleg-számítás pontossága miatt fontos, mert a
   társasházi egyenleg is kategóriánként/tételenként vezetendő, nem csak összesítve. Az
   ingatlanonkénti rezsi fajta lista (ld. fent, 3. pont) ehhez **már megvalósított** támogatás.
6. **Átalány vs. tényleges (mért) tétel.** Egyes kategóriák (jellemzően fűtés/vízmelegítés, vízdíj)
   havonta fix **átalány**-ként kerülnek felvitelre — ezt a 4. pont szerinti utólagos korrekció
   igazítja a tényleges leolvasáshoz, amikor megérkezik. Ez nem külön kategória, csak egy mintázat:
   ugyanaz a rezsi-kategória hónapról hónapra ismétlődő, hasonló összeggel jelenik meg (átalány),
   majd időnként egy attól eltérő összegű, `Note`-tal ellátott tétel korrigálja.
7. **A rezsi-időszak zárolása megszűnik.** A mai szabály (ha egy hónapra már ment számla, a rezsi
   tételei onnantól nem szerkeszthetők) törlésre kerül. Bármely hónap rezsi tételei bármikor
   felvehetők/módosíthatók/törölhetők, függetlenül attól, hogy ment-e már rá számla. **Fontos
   következmény**: egy már kiküldött számla adatai emiatt *nem* frissülnek utólag automatikusan —
   a visszamenőleges rezsi-módosítás csak a naplózásra/egyenlegre hat, a korábban kiküldött
   bizonylatot nem írja felül. Ez a felület egyszerűsítését szolgálja (nincs "Lezárva" állapot,
   nincs tiltott gomb, nincs magyarázkodás, miért nem szerkeszthető valami).
8. **A számla rendezi az aktuális bérlői egyenleget.** Számlagenerráláskor a rendszer a bérleti
   díj + rezsi mellé egy külön, jól látható sorként felveszi az addig felhalmozott **bérlői
   egyenleget** is: ha a bérlő tartozik (az egyenleg negatív), a hiányzó összeg hozzáadódik a
   számla végösszegéhez; ha túlfizetett (pozitív egyenleg), a többlet levonásra kerül. A sor a
   számlán elkülönül a bérleti díj és a rezsi soraitól (pl. "Korábbi egyenleg rendezése: +12 400
   Ft"), hogy a bérlő lássa, miért tér el az összeg a szokásos bérleti díj + rezsi összegétől. Csak
   a **bérlői** egyenleg jelenik meg így (ez a bérlő felé kiállított bizonylat) — a társasházi
   egyenleg nem érinti a bérlő számláját.

   **Nyitott kérdés, tisztázandó implementáció előtt**: ha az egyenleg-rendezés bekerül a
   számlába, az adott hónap "Fizetendő" összege (ami a bérlői egyenleg számításának alapja, ld. 2.
   pont fogalomtáblázat) ettől kezdve már *tartalmazza* az előző hónapok rendezését is — ezt a
   következő havi egyenleg-számításnak figyelembe kell vennie, különben a már rendezett összeg
   újra megjelenne hiányként/többletként (dupla számolás). Azt is el kell dönteni, hogy a
   rendezés minden hónapban automatikus (a teljes aktuális egyenleget mindig nullázza), vagy csak
   akkor lép életbe, ha valaki kifejezetten kéri/jóváhagyja az adott havi számlázáskor.

## 5. Felhasználói felület

**Már megvalósítva és élesítve** (2026.08.25, `lakaskezelo.alts.hu`, verzió 2026.08.25):

- Az Ingatlan modal Rezsi tabja fölött ingatlanonkénti "Rezsi fajták" lista (hozzáadás/törlés), a
  rezsi tétel felviteli popup Megnevezés mezője ebből javasol (ld. 3. pont).
- A hónap-popup (ingatlan + hónap kattintásra megnyíló ablak) két új mezőt kapott: „Fizetve a
  társasháznak” és „Beérkezett a bérlőtől” (utóbbi automatikusan felkínálja az SZJA-levonás
  összegét, ha az a bérlőnél aktív; a mező a bérlőtől befolyt **teljes** összeget jelenti, bérleti
  díj és rezsi együtt, nem csak a rezsi rész).
- A hónapok táblázata új oszlopokat kapott: **Számlázott összeg** (az automatikusan számított,
  ténylegesen kiszámlázott összeg — bérleti díj + rezsi, nem csak a rezsi tételek összege, hogy a
  táblázatból önmagában is látszódjon, mennyi ment ki számlán, nem csak mennyi volt a rezsi),
  **Társasházi egyenleg** és **Bérlői egyenleg** (havi és kumulált érték egyaránt).
- A meglévő havi rezsi-oszlopdiagram mellé, jobbra egy külön panel került, amelyben a két
  kumulált egyenleg-vonaldiagram (társasházi és bérlői) egymás alatt jelenik meg.

## 6. Amit érintetlenül hagyunk

- Számlagenerálás, PDF-készítés, e-mail kiküldés logikája
- A meglévő rezsi tétel (előirányzat) felvitele
- 2FA, felhasználókezelés, Google Drive/Gmail integráció
