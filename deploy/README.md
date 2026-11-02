# Éles környezet (AWS EC2)

- **Szerver:** `i-0f4ed5f6f5afb59f0` (eu-central-1, t3.micro, Ubuntu 24.04), `https://lakaskezelo.alts.hu`
  (Elastic IP: `63.188.91.152`). A root EBS-kötet KMS-sel titkosított.
- **Futtatókörnyezet:** .NET 10 (LTS) a `mcr.microsoft.com/dotnet/aspnet:10.0` image-ben, a beépített nem root
  `app` felhasználóként.
- **Könyvtár:** `/home/ubuntu/lakaskezelo` — `docker-compose.yml`, `nginx.conf`, `.env` (titkok, 600-as
  jogosultság), `backend/`, `frontend/`, `certbot-webroot/`. **Nem git-klón**: élesítéskor a `main`
  ág tartalma kerül ide.
- **Hozzáférés:** AWS Systems Manager (SSM) Run Command / Session Manager. SSH csak az AWS konzol
  „Connect → EC2 Instance Connect” funkciójával (a security group a 22-es portot csak az EC2
  Instance Connect szolgáltatás címtartományából engedi).
- **Mentés:** napi EBS-pillanatkép 7 napig + heti 4 hétig (DLM szabály, a kötet `Backup=lakaskezelo`
  címkéje alapján); élesítés előtt kézi adatbázis-dump a `/home/ubuntu` mappába.

## Ágak, verziók, kiadások

| Ág | Környezet | Cím |
|---|---|---|
| `staging` | teszt — kitalált adatok, e-mail csak a felhasználóknak megy | https://lakaskezelo.alts.hu:8443 |
| `main` | éles | https://lakaskezelo.alts.hu |

- **Munkamenet:** a fejlesztés munkaágon (`claude/...`) történik → PR a `staging`-be → kirakás a tesztre →
  kipróbálás. Élesre csak a tulajdonos jóváhagyásával kerül: PR `staging` → `main`, majd élesítés.
- **Verziószám (`AppVersion.Current`):** dátum alapú, `ÉÉÉÉ.HH.NN` (ha egy napon több kiadás van:
  `ÉÉÉÉ.HH.NN.2`). Minden érdemi változásnál nő; a felület oldalsávjában és a `/api/version`-en látszik, a
  tesztet borostyán „TESZT KÖRNYEZET” sáv jelzi.
- **Kiadás:** minden éles kiadás után `vÉÉÉÉ.HH.NN` git-címke a `main`-en, és a `docs/valtozasnaplo.md`-ben a
  „Csak a teszten” rész átkerül a kiadás alá.

## Teszt-környezet (staging)

- Ugyanazon a szerveren fut, külön konténerekben (`postgres-staging`, `api-staging`, memóriakorláttal), a
  közös nginx 8443-as portján. A kód a `/home/ubuntu/lakaskezelo/staging/{backend,frontend}` mappában.
- **Kirakás:** a `staging` ág adott commitjának `backend/` és `frontend/` mappája a `staging/` alá, majd
  `docker compose up -d --build api-staging` és `docker compose restart nginx`. A `deploy/` fájlok
  (compose, nginx) a szerveren közösek az éles rendszerrel: a `staging`-ről csak akkor kerülnek ki, ha a
  változás az éles szolgáltatásokat nem módosítja (pl. a teszt saját blokkjai); egyébként élesítéskor, a
  `main`-ről. Kiírás előtt `nginx -t` és `docker compose config` ellenőrzés.
- **Adatok:** a `deploy/staging-seed.sql` kitalált mintaadatai. A felhasználók (jelszó-hash) és az e-mail
  beállítások az élesről másolódnak, hogy a belépés és a belépési kód ugyanúgy működjön; a Rezsi-emlékeztető
  címzettje és az S3-tárhely nem. Valós ügyfél- vagy bérlői adat nem kerülhet a tesztre.

## Élesítés (csak a `main` ágról)

1. **Mentés:** `pg_dumpall` a `/home/ubuntu/db-backup-<időbélyeg>.sql` fájlba, és
   `tar czf /home/ubuntu/lakaskezelo-backup-<időbélyeg>.tgz lakaskezelo`.
2. **Kód:** a `main` ág adott commitjának letöltése
   (`https://codeload.github.com/MaczakG/lakas_management/tar.gz/<commit>`), majd a `backend/` és a
   `frontend/` mappa cseréje, valamint a `deploy/nginx.conf` → `nginx.conf` és a
   `deploy/docker-compose.yml` → `docker-compose.yml` másolása. A `.env` és a `certbot-webroot/`
   marad.
3. **Indítás:** `docker compose pull nginx postgres && docker compose up -d --build` (a buildhez
   érdemes `--pull`-t is adni, hogy a .NET alapimage biztonsági javításai is bekerüljenek).
4. **nginx újraindítása:** `docker compose restart nginx` — **kötelező**, mert a `frontend/` mappa
   cseréje után a futó konténer még a régi (törölt) mappát látná, és 403-at adna.
5. **Ellenőrzés:** az oldal 200-at ad, az API naplójában nincs hiba, a migrációk lefutottak.

A `nginx.conf` módosítása után először `docker run --rm -v $PWD/nginx.conf:/etc/nginx/conf.d/default.conf:ro -v /etc/letsencrypt:/etc/letsencrypt:ro nginx:stable-alpine nginx -t`
paranccsal érdemes ellenőrizni.

## Biztonsági beállítások röviden

- **Bejelentkezés:** jelszó + e-mailben kapott 6 jegyű kód (10 percig érvényes, legfeljebb 5
  próbálkozás). 5 egymás utáni hibás jelszó/kód után a fiók 15 percre zárolódik, és a tulajdonos
  e-mailt kap róla. A bejelentkezési végpontokat az nginx és a backend is IP-címenként korlátozza.
- **Munkamenetek:** a JWT 120 percig érvényes, de jelszócsere, e-mail-változás, inaktiválás vagy
  törlés után azonnal érvényét veszti (`User.SecurityStamp`).
- **Vészkapcsoló:** ha az e-mail küldés tartósan nem működik, és emiatt senki sem tud belépni, a
  `docker-compose.yml`-ben `Auth__TwoFactorEnabled: "false"`, majd `docker compose up -d api`.
  Utána mielőbb vissza `"true"`-ra.
- **Regisztráció és rendszerlevelek:** az önregisztráció a `Registration__Enabled: "true"` környezeti
  változóval nyílik meg (a teszten nyitva, élesben zárva). A rendszerlevelek (belépési kód, meghívó,
  regisztráció megerősítése) a `Platform__EmailCompanyId` cég (alapból az első, üzemeltetői cég)
  Beállításokban megadott e-mail fiókjáról mennek, `App__ProductName` (alapból „Lakáskezelő”) feladónévvel.
- **Titkok:** az SMTP-jelszót és a Mailgun API-kulcsot a Beállítások API nem adja vissza a
  böngészőnek (csak felülírni lehet őket).
