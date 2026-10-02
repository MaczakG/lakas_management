# Éles környezet (AWS EC2)

- **Szerver:** `i-0f4ed5f6f5afb59f0` (eu-central-1, t3.micro, Ubuntu 24.04), `https://lakaskezelo.alts.hu`
- **Könyvtár:** `/home/ubuntu/lakaskezelo` — `docker-compose.yml`, `nginx.conf`, `.env` (titkok, 600-as
  jogosultság), `backend/`, `frontend/`, `certbot-webroot/`. **Nem git-klón**: élesítéskor a `main`
  ág tartalma kerül ide.
- **Hozzáférés:** AWS Systems Manager (SSM) Run Command / Session Manager. SSH csak az AWS konzol
  „Connect → EC2 Instance Connect” funkciójával (a security group a 22-es portot csak az EC2
  Instance Connect szolgáltatás címtartományából engedi).
- **Mentés:** napi EBS-pillanatkép 7 napig + heti 4 hétig (DLM szabály, a kötet `Backup=lakaskezelo`
  címkéje alapján); élesítés előtt kézi adatbázis-dump a `/home/ubuntu` mappába.

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
- **Titkok:** az SMTP-jelszót és a Mailgun API-kulcsot a Beállítások API nem adja vissza a
  böngészőnek (csak felülírni lehet őket).
