-- A teszt-környezet (staging) kitalált mintaadatai. Valós ügyfél- vagy bérlői adat SOHA nem kerülhet a
-- tesztre. Az e-mail címek .invalid végűek (RFC 2606): ezekre semmilyen levél nem kézbesíthető, és a
-- staging amúgy is csak a felhasználóknak küld e-mailt (Email:OnlyToUsers).
-- Futtatás: üres (frissen migrált) staging adatbázison, ld. deploy/README.md "Teszt-környezet".

BEGIN;

-- Minden mintaadat az alapértelmezett cégé (Company.DefaultCompanyId). A CompanyId-nak nincs alapértéke
-- (az alkalmazás tölti ki), ezért a betöltés idejére ideiglenesen adunk neki egyet.
ALTER TABLE "Owners" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';
ALTER TABLE "Properties" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';
ALTER TABLE "PropertyOwners" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';
ALTER TABLE "Tenants" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';
ALTER TABLE "UtilityCostEntries" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';
ALTER TABLE "Invoices" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';
ALTER TABLE "InvoiceLines" ALTER COLUMN "CompanyId" SET DEFAULT 'd0000000-0000-4000-8000-000000000001';

INSERT INTO "Owners" ("Id","Name","Email","Phone","TaxId","BankAccount","Address","Notes","CreatedAt") VALUES
 ('a0000000-0000-4000-8000-000000000001','Minta Anna','anna@minta.invalid','+36 30 111 2233','8412345678','11773016-00000000-00000000','1111 Budapest, Minta utca 1.',NULL,now()),
 ('a0000000-0000-4000-8000-000000000002','Példa Péter','peter@pelda.invalid','+36 20 444 5566',NULL,'10700024-00000000-00000000','2890 Tata, Példa tér 2.',NULL,now()),
 ('a0000000-0000-4000-8000-000000000003','Teszt Ingatlan Kft.','iroda@tesztingatlan.invalid','+36 1 555 0100','12345678-2-11','12010000-00000000-00000000','1051 Budapest, Teszt köz 3.','Céges tulajdonos (mintaadat).',now());

INSERT INTO "Properties" ("Id","Name","RentAmount","InvoicePrefix","BillingDayOfMonth","BillingHour","BillingMinute","IsActive","CreatedAt",
  "PropertyType","PostalCode","City","Street","FloorDoor","Hrsz","FloorArea","Rooms","BuildYear","ElectricityMeter","GasMeter","WaterMeter","Deposit","Notes") VALUES
 ('b0000000-0000-4000-8000-000000000001','Tata, Kossuth tér 4. A/12',185000,'T12',1,8,0,true,now(),
  'Lakás','2890','Tata','Kossuth tér 4.','3. em. 12.','1453/2/A/12',62.5,2.5,1978,'E-48213377','G-0912764','V-552019',370000,'Két kulcs a bérlőnél, egy a tulajdonosnál. 2025-ben felújított fürdőszoba.'),
 ('b0000000-0000-4000-8000-000000000002','Budapest, Minta utca 18. 2/5',690,'BP5',5,8,0,true,now(),
  'Lakás','1137','Budapest','Minta utca 18.','2. em. 5.',NULL,48,2,1936,'E-11002233',NULL,'V-100200',NULL,NULL),
 ('b0000000-0000-4000-8000-000000000003','Tatabánya, Példa út 7. üzlet',240000,'TB7',10,9,30,true,now(),
  'Üzlethelyiség','2800','Tatabánya','Példa út 7.','fszt.',NULL,95,NULL,2004,'E-77889900','G-5566778',NULL,480000,'Kávézó; a bérleti díj évente infláció követéssel emelkedik.'),
 ('b0000000-0000-4000-8000-000000000004','Tata, Garázs 3.',25000,'G3',1,8,0,false,now(),
  'Garázs','2890','Tata','Ady Endre utca 9.','3. garázs',NULL,18,NULL,NULL,NULL,NULL,NULL,NULL,'Jelenleg üres.');

INSERT INTO "PropertyOwners" ("Id","PropertyId","OwnerId","Share") VALUES
 (gen_random_uuid(),'b0000000-0000-4000-8000-000000000001','a0000000-0000-4000-8000-000000000001','1/2'),
 (gen_random_uuid(),'b0000000-0000-4000-8000-000000000001','a0000000-0000-4000-8000-000000000002','1/2'),
 (gen_random_uuid(),'b0000000-0000-4000-8000-000000000002','a0000000-0000-4000-8000-000000000001',NULL),
 (gen_random_uuid(),'b0000000-0000-4000-8000-000000000003','a0000000-0000-4000-8000-000000000003',NULL),
 (gen_random_uuid(),'b0000000-0000-4000-8000-000000000004','a0000000-0000-4000-8000-000000000002',NULL);

INSERT INTO "Tenants" ("Id","Name","Email","Phone","PropertyId","Address","TaxId","MoveInDate","MoveOutDate","Notes","CreatedAt","RentCurrency") VALUES
 ('c0000000-0000-4000-8000-000000000001','Teszt Elek','elek@teszt.invalid','+36 70 123 4567','b0000000-0000-4000-8000-000000000001',NULL,NULL,'2025-03-01',NULL,NULL,now(),0),
 ('c0000000-0000-4000-8000-000000000002','Korábbi Kata','kata@teszt.invalid',NULL,'b0000000-0000-4000-8000-000000000001',NULL,NULL,'2022-09-01','2025-02-15',NULL,now(),0),
 ('c0000000-0000-4000-8000-000000000003','Minta Bt.','iroda@mintabt.invalid','+36 1 222 3344','b0000000-0000-4000-8000-000000000002','1137 Budapest, Minta utca 18.','23456789-1-41','2024-01-01',NULL,'EUR-ban fizet.',now(),1),
 ('c0000000-0000-4000-8000-000000000004','Példa Kávézó Kft.','kave@peldakavezo.invalid','+36 34 555 666','b0000000-0000-4000-8000-000000000003','2800 Tatabánya, Példa út 7.','34567890-2-11','2023-05-01',NULL,NULL,now(),0);

-- Rezsi az elmúlt három hónapra (a Tatai lakásra), és az aktuális hónapra a budapestire.
INSERT INTO "UtilityCostEntries" ("Id","PropertyId","Year","Month","Label","Amount","CreatedByUserId","CreatedAt")
SELECT gen_random_uuid(),'b0000000-0000-4000-8000-000000000001',
       extract(year from d)::int, extract(month from d)::int, l.label, l.amount + extract(month from d)::int * 120, NULL, now()
FROM generate_series(date_trunc('month', now()) - interval '2 months', date_trunc('month', now()), interval '1 month') d
CROSS JOIN (VALUES ('Közös költség', 18500), ('Víz', 4000)) l(label, amount);
INSERT INTO "UtilityCostEntries" ("Id","PropertyId","Year","Month","Label","Amount","CreatedByUserId","CreatedAt")
VALUES (gen_random_uuid(),'b0000000-0000-4000-8000-000000000002',extract(year from now())::int,extract(month from now())::int,'Közös költség',22000,NULL,now());

-- Számlák az elmúlt négy hónapra (a Tatai lakásra) — a legrégebbi "sikertelen" mintának.
INSERT INTO "Invoices" ("Id","PropertyId","TenantId","PeriodYear","PeriodMonth","Number","IssuedAt","DueDate","AmountTotal","Status","SentAt","ErrorMessage")
SELECT gen_random_uuid(),'b0000000-0000-4000-8000-000000000001','c0000000-0000-4000-8000-000000000001',
       extract(year from d)::int, extract(month from d)::int,
       'T12-' || extract(month from d)::int || '-' || extract(year from d)::int,
       d + interval '7 hours', (d + interval '8 days')::date,
       185000 + 18500 + 4000 + extract(month from d)::int * 240,
       CASE WHEN d = date_trunc('month', now()) - interval '4 months' THEN 2 ELSE 1 END,
       CASE WHEN d = date_trunc('month', now()) - interval '4 months' THEN NULL ELSE d + interval '7 hours' END,
       CASE WHEN d = date_trunc('month', now()) - interval '4 months' THEN 'SMTP hiba (mintaadat)' ELSE NULL END
FROM generate_series(date_trunc('month', now()) - interval '4 months', date_trunc('month', now()) - interval '1 month', interval '1 month') d;

INSERT INTO "InvoiceLines" ("Id","InvoiceId","Label","Amount","Position")
SELECT gen_random_uuid(), i."Id", l.label, l.amount + CASE WHEN l.pos = 0 THEN 0 ELSE i."PeriodMonth" * 120 END, l.pos
FROM "Invoices" i
CROSS JOIN (VALUES ('Bérleti díj', 185000, 0), ('Közös költség', 18500, 1), ('Víz', 4000, 2)) l(label, amount, pos);

ALTER TABLE "Owners" ALTER COLUMN "CompanyId" DROP DEFAULT;
ALTER TABLE "Properties" ALTER COLUMN "CompanyId" DROP DEFAULT;
ALTER TABLE "PropertyOwners" ALTER COLUMN "CompanyId" DROP DEFAULT;
ALTER TABLE "Tenants" ALTER COLUMN "CompanyId" DROP DEFAULT;
ALTER TABLE "UtilityCostEntries" ALTER COLUMN "CompanyId" DROP DEFAULT;
ALTER TABLE "Invoices" ALTER COLUMN "CompanyId" DROP DEFAULT;
ALTER TABLE "InvoiceLines" ALTER COLUMN "CompanyId" DROP DEFAULT;

COMMIT;
