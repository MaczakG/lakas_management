using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Api.Storage;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Egy adott ingatlan adott havi számlájának összeállítása, PDF-generálása, S3-ba mentése és
// e-mailes kiküldése — ezt hívja mind a BillingSchedulerService (automatikus, ütemezett), mind az
// InvoicesController manuális "Generálás most" / "Újraküldés" végpontja, hogy a logika egy helyen
// éljen.
public class InvoiceGenerationService(
    LakaskezeloDbContext db,
    AppSettingsService settingsService,
    S3InvoiceStorage storage,
    IEmailSender emailSender,
    Documents.DocumentStorage documents,
    Invoicing.InvoiceProviderResolver providers,
    ILogger<InvoiceGenerationService> logger)
{
    // A Beállítások oldalon szerkeszthető sablon alapértéke — csak akkor kerül elő, ha a mező
    // üres, hogy a régebbi (még nem testreszabott) telepítéseken ne változzon a kiküldött szöveg.
    private const string DefaultInvoiceEmailSubject = "Bizonylat — {ingatlan}";
    // Cégnél, számlázóval kiállított valódi számlánál.
    private const string DefaultProviderEmailSubject = "Számla — {ingatlan}";
    private const string DefaultProviderEmailBody = "Elkészült a(z) {ingatlan} ingatlanhoz tartozó számlád a(z) {időszak} időszakra.\n\nFizetendő összeg: {összeg}";
    private const string DefaultInvoiceEmailBody = "Elkészült a(z) {ingatlan} ingatlanhoz tartozó bizonylatod a(z) {időszak} időszakra.\n\nFizetendő összeg: {összeg}";

    // A levélhez csatolt fájlok (a bizonylat PDF-je és a rezsitételek csatolmányai) együttes mérete legfeljebb ennyi
    // lehet: a levelezőrendszerek jellemzően 20–25 MB-nál nagyobb levelet nem fogadnak. Ami nem fér bele, kimarad.
    public const long MaxEmailAttachmentBytes = 18 * 1024 * 1024;

    // sendAttachments: kézi bizonylatkészítésnél a felhasználó döntése arról, menjenek-e a rezsitételek csatolmányai
    // a levéllel. Csak akkor számít, ha a bérlőnél nincs beállítva (InvoiceAttachmentPreference.Ask); null (pl. az
    // időzített küldésnél, ahol nincs kit megkérdezni): mellékletek nélkül megy.
    // onProgress (opcionális): a felület folyamatjelzőjének — előbb a lépések terve („plan”), aztán minden lépés
    // kezdete (data, pdf, upload, email, save). A visszahívás hibája (pl. bezárt böngésző) a generálást nem állítja meg.
    public async Task<Invoice> GenerateAndSendAsync(Property property, int year, int month, CancellationToken ct,
        Func<GenerationProgress, Task>? onProgress = null, bool? sendAttachments = null)
    {
        async Task Report(GenerationProgress progress)
        {
            if (onProgress is null) return;
            try { await onProgress(progress); }
            catch (Exception ex) { logger.LogDebug(ex, "Invoice progress callback failed"); }
        }

        // Csak az aktív (nem sztornózott, nem sztornó) számlát frissítjük; sztornózott mellé új számla készül.
        var existing = await db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.PropertyId == property.Id && i.PeriodYear == year && i.PeriodMonth == month
                && i.CancelledAt == null && i.CancelsInvoiceId == null, ct);
        var cancelledCount = existing is null
            ? await db.Invoices.CountAsync(i => i.PropertyId == property.Id && i.PeriodYear == year && i.PeriodMonth == month
                && i.CancelledAt != null && i.CancelsInvoiceId == null, ct)
            : 0;

        var settings = await settingsService.GetAsync(ct);
        var company = await db.Companies.SingleAsync(c => c.Id == property.CompanyId, ct);
        // Cégnél, beállított számlázóval valódi számla készül a számlázóban (különben a saját bizonylatunk).
        var provider = providers.For(company, settings);
        // Kiállított számlát (vagy ha közben számlázóra váltottak, kiállított bizonylatot) nem lehet felülírni:
        // javításhoz sztornózni kell, és új készül.
        if (existing is not null && existing.Status != InvoiceStatus.Failed && (provider is not null || existing.Provider is not null))
        {
            throw new InvalidOperationException(
                $"Erre az időszakra már ki van állítva: {existing.Number}. Javításhoz sztornózd, és utána készíts újat.");
        }
        var tenant = await FindTenantAsync(property.Id, year, month, ct);
        var utilityLines = await BillableEntriesAsync(property.Id, year, month, existing?.Id, ct);
        foreach (var u in utilityLines) u.InvoiceId = null;

        // A bizonylat "Bérleti díj jogosultjai" táblázatához kell a tulajdonos-lista Owner-navigációval.
        await db.Entry(property).Collection(p => p.PropertyOwners).Query().Include(po => po.Owner).LoadAsync(ct);

        var invoice = existing ?? new Invoice
        {
            Id = Guid.NewGuid(),
            // Már most kell (nem csak mentéskor): a PDF tárhely-kulcsa a cégre épül (InvoicePdfStore.StorageKey).
            CompanyId = property.CompanyId,
            PropertyId = property.Id,
            PeriodYear = year,
            PeriodMonth = month,
        };

        invoice.TenantId = tenant?.Id;
        invoice.IssuedAt = DateTime.UtcNow;
        invoice.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);
        // Ha az időszakra már van sztornózott számla, az új a „/2”, „/3”… utótagot kapja, hogy a sorszám egyedi maradjon.
        invoice.Number = existing?.Number
            ?? BuildInvoiceNumber(property, year, month) + (cancelledCount > 0 ? $"/{cancelledCount + 1}" : "");

        // Ha a számla már elkészült a számlázóban, a későbbi lépések (PDF letöltés, tárhely, e-mail) hibája nem teheti
        // „Sikertelenné” — különben az újrapróbálás még egy számlát állítana ki.
        var issuedAtProvider = false;
        try
        {
            var storageConfigured = await storage.IsConfiguredAsync(ct);
            var willEmail = !string.IsNullOrWhiteSpace(tenant?.Email);
            var plan = new List<string> { GenerationSteps.Data, provider is null ? GenerationSteps.Pdf : GenerationSteps.Provider };
            if (storageConfigured) plan.Add(GenerationSteps.Upload);
            if (willEmail) plan.Add(GenerationSteps.Email);
            plan.Add(GenerationSteps.Save);
            await Report(new GenerationProgress(GenerationSteps.Plan, plan));
            await Report(new GenerationProgress(GenerationSteps.Data));

            // A bérleti díj sor összege a bérlőnél beállított devizanemtől függ (RentCurrency) — az
            // Ingatlan RentAmount mezője HUF helyett EUR/USD is lehet, de a számlán mindig a
            // legfrissebb tárolt MNB árfolyamon átváltott HUF összeg jelenik meg. Ha nincs még
            // elérhető árfolyam a beállított devizához, a számla Failed státusszal jön létre, jól
            // olvasható hibaüzenettel.
            var (rentAmountHuf, conversionNote) = await ResolveRentAmountAsync(property, tenant, ct);

            invoice.RentConversionNote = conversionNote;

            // A sorokat egy különálló, lokális listában építjük fel, és csak a db.InvoiceLines
            // DbSet-en át vesszük fel/töröljük — szándékosan NEM az invoice.Lines navigációs listát
            // mutáljuk (se .Clear(), se .Add()). Két egymásra ható EF change-tracker gotcha miatt:
            // (1) egy előre beállított Guid kulcsú új entitást navigációs listán át felvéve az EF
            // tévesen Modified-nek (UPDATE) jelöl Added (INSERT) helyett — ugyanez a hiba, mint amit
            // a PropertyOwners-nél találtunk; (2) a hívó (InvoicesController.Generate) a válasz
            // összeállításához explicit db.Entry(invoice).Collection(i => i.Lines).LoadAsync()-ot hív
            // — ha időközben kézzel is feltöltöttük volna az invoice.Lines listát, a friss lekérdezés
            // duplikálná a sorokat a végeredményben.
            var oldLines = existing?.Lines.ToList() ?? [];
            if (oldLines.Count > 0) db.InvoiceLines.RemoveRange(oldLines);

            // A lakbér sora a bérlőnél beállított fizetési mód szerinti hónapot viseli: előre fizetésnél a bizonylat
            // időszakát, utólagosnál az azt megelőző hónapot.
            var (rentYear, rentMonth) = RentPeriod(tenant, year, month);
            var newLines = new List<InvoiceLine>
            {
                new() { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Label = $"{InvoiceLine.RentLabel} ({rentYear}. {MonthName(rentMonth)})", Amount = rentAmountHuf, Position = 0 },
            };
            // Ha a bérlő fizeti a bérbeadó helyett az SZJA-t (magánszemély bérbeadónál), a lakbérből levonódik.
            var szja = SzjaDeduction(company, provider is not null, tenant, rentAmountHuf);
            if (szja != 0)
            {
                newLines.Add(new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Label = SzjaLabel, Amount = szja, Position = newLines.Count });
            }
            // A rezsi tételesen kerül a számlára: a Rezsi felvételben rögzített minden tétel külön sor, a felvett
            // megnevezéssel (pl. Áram, Gáz, Közös költség); előbb az időszak tételei a rögzítés sorrendjében, utána a
            // korábbi hónapokra utólag felvettek, a hónapjukkal jelölve.
            var billed = utilityLines.Where(u => u.Amount != 0)
                .OrderBy(u => u.Year == year && u.Month == month ? 0 : 1)
                .ThenBy(u => u.Year).ThenBy(u => u.Month)
                .ThenBy(u => u.CreatedAt).ThenBy(u => u.Label)
                .ToList();
            foreach (var u in billed)
            {
                var label = string.IsNullOrWhiteSpace(u.Label) ? "Rezsi" : u.Label.Trim();
                if (u.Year != year || u.Month != month) label = $"{label} ({u.Year}. {MonthName(u.Month)})";
                newLines.Add(new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Label = label, Amount = u.Amount, Position = newLines.Count });
            }
            db.InvoiceLines.AddRange(newLines);
            invoice.AmountTotal = newLines.Sum(l => l.Amount);

            // A bizonylatra került rezsitételek csatolmányai (pl. szolgáltatói számlák), a tételek sorrendjében.
            var attachmentDocs = await AttachmentsOfAsync(billed, ct);
            invoice.AttachmentsAvailable = attachmentDocs.Count;
            invoice.AttachmentsSent = 0;

            byte[]? pdfBytes;
            if (provider is null)
            {
                await Report(new GenerationProgress(GenerationSteps.Pdf));
                pdfBytes = InvoicePdfGenerator.Generate(invoice, property, tenant, settings, newLines);
            }
            else
            {
                await Report(new GenerationProgress(GenerationSteps.Provider));
                if (tenant is null) throw new Invoicing.ProviderException("Az ingatlanhoz nincs bérlő rendelve, így nincs kinek számlát kiállítani.");
                var rentVat = property.RentVat ?? settings.DefaultRentVat;
                var utilityVat = property.UtilityVat ?? settings.DefaultUtilityVat;
                var today = DateOnly.FromDateTime(BudapestClock.Now);
                var comment = $"{property.Name}, {year}. {MonthName(month)}"
                    + (string.IsNullOrWhiteSpace(conversionNote) ? "" : $"\n{conversionNote}");
                var request = new Invoicing.ProviderInvoiceRequest(
                    Invoicing.InvoicingProviders.BuyerFor(tenant, property),
                    [.. newLines.Select(l => new Invoicing.ProviderLine(
                        l.Label, l.Amount, l.Position == 0 ? rentVat : utilityVat))],
                    today, invoice.DueDate, invoice.DueDate, comment);
                var issued = await provider.IssueAsync(settings, request, ct);
                issuedAtProvider = true;
                invoice.Provider = provider.Name;
                invoice.ProviderDocumentId = issued.DocumentId;
                invoice.Number = issued.Number;
                invoice.IssuedAt = DateTime.UtcNow;
                if (issued.BillingoPartnerId is { } partnerId) tenant.BillingoPartnerId = partnerId;
                pdfBytes = issued.Pdf;
                if (pdfBytes is null)
                {
                    try { pdfBytes = await provider.DownloadPdfAsync(settings, invoice, ct); }
                    catch (Invoicing.ProviderException ex) { logger.LogWarning(ex, "Provider PDF of {Number} not available yet", invoice.Number); }
                }
            }

            // A tárhely hibája nem akaszthatja meg a számla kiküldését: ilyenkor a PDF letöltéskor
            // (InvoicePdfStore) a tárolt adatokból újragenerálódik és akkor mentődik el.
            invoice.PdfStorageKey = null;
            if (storageConfigured && pdfBytes is not null)
            {
                await Report(new GenerationProgress(GenerationSteps.Upload));
                try
                {
                    var key = InvoicePdfStore.StorageKey(invoice);
                    await storage.UploadAsync(key, pdfBytes, "application/pdf", ct);
                    invoice.PdfStorageKey = key;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Storing invoice PDF {Number} in S3 failed", invoice.Number);
                }
            }

            var emailed = false;
            if (willEmail && pdfBytes is not null)
            {
                await Report(new GenerationProgress(GenerationSteps.Email));
                var periodLabel = $"{year}.{month:D2}";
                var placeholders = new Dictionary<string, string>
                {
                    ["{ingatlan}"] = property.Name,
                    ["{időszak}"] = periodLabel,
                    ["{összeg}"] = MoneyFormat.Huf(invoice.AmountTotal),
                    ["{bizonylatszám}"] = invoice.Number,
                    ["{számlaszám}"] = invoice.Number, // régebbi, testreszabott sablonokhoz
                };
                string Fill(string template) => placeholders.Aggregate(template, (acc, kv) => acc.Replace(kv.Key, kv.Value));

                var subject = Fill(string.IsNullOrWhiteSpace(settings.InvoiceEmailSubject)
                    ? (provider is null ? DefaultInvoiceEmailSubject : DefaultProviderEmailSubject) : settings.InvoiceEmailSubject);
                var bodyText = Fill(string.IsNullOrWhiteSpace(settings.InvoiceEmailBody)
                    ? (provider is null ? DefaultInvoiceEmailBody : DefaultProviderEmailBody) : settings.InvoiceEmailBody);

                var bodyParagraphsHtml = string.Concat(bodyText
                    .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => $"""<p style="margin:0 0 12px;">{System.Net.WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</p>"""));
                var htmlBody = EmailTemplate.Render(preheader: $"{subject} — {periodLabel}", heading: subject, bodyHtml: bodyParagraphsHtml);
                var attachments = new List<EmailAttachment> { new($"{invoice.Number}.pdf", pdfBytes, "application/pdf") };
                // A rezsitételek csatolmányai: a bérlő beállítása dönt; ha nincs beállítva, a kézi készítéskor adott válasz.
                if (ShouldSendAttachments(tenant, sendAttachments))
                {
                    var room = MaxEmailAttachmentBytes - pdfBytes.LongLength;
                    foreach (var doc in attachmentDocs)
                    {
                        if (doc.SizeBytes > room) continue;
                        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.DocumentId == doc.Id && v.Number == doc.CurrentVersion, ct);
                        byte[]? data = null;
                        try { if (version is not null) data = await documents.ReadAsync(version, ct); }
                        catch (Exception ex) { logger.LogWarning(ex, "Attachment {DocumentId} of invoice {Number} could not be read", doc.Id, invoice.Number); }
                        if (data is null) continue;
                        attachments.Add(new EmailAttachment(doc.FileName, data, doc.ContentType));
                        room -= data.LongLength;
                    }
                }
                emailed = await emailSender.SendWithAttachmentsAsync(tenant.Email!, EmailTemplate.UniqueSubject(subject), htmlBody, bodyText, attachments, ct);
                if (emailed) invoice.AttachmentsSent = attachments.Count - 1;
            }

            invoice.Status = emailed ? InvoiceStatus.Sent : InvoiceStatus.Generated;
            invoice.SentAt = emailed ? DateTime.UtcNow : null;
            // A bizonylatra került rezsitételek innentől zárolva vannak, amíg a bizonylatot nem sztornózzák.
            foreach (var u in utilityLines) { u.InvoiceId = invoice.Id; u.ExcludedFromBilling = false; }
            invoice.ErrorMessage = pdfBytes is null ? "A számla elkészült, de a PDF-je még nem tölthető le a számlázóból, ezért e-mailben nem ment ki." : null;
        }
        catch (Exception ex) when (issuedAtProvider)
        {
            logger.LogWarning(ex, "Post-issue step failed for provider invoice {Number}", invoice.Number);
            invoice.Status = InvoiceStatus.Generated;
            foreach (var u in utilityLines) { u.InvoiceId = invoice.Id; u.ExcludedFromBilling = false; }
            invoice.ErrorMessage = $"A számla elkészült, de utána hiba történt: {ex.Message}";
        }
        catch (Exception ex)
        {
            invoice.Status = InvoiceStatus.Failed;
            invoice.ErrorMessage = ex.Message;
        }

        await Report(new GenerationProgress(GenerationSteps.Save));
        if (existing is null) db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    // A bizonylat az időszakban ott lakó bérlőé (a kiköltözött korábbi bérlő nem kaphatja, és az egyenlegébe sem
    // kerülhet); több jelölt közül a legutóbb beköltözött.
    private async Task<Tenant?> FindTenantAsync(Guid propertyId, int year, int month, CancellationToken ct)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var tenant = await db.Tenants
            .Where(t => t.PropertyId == propertyId
                && (t.MoveOutDate == null || t.MoveOutDate >= periodStart)
                && (t.MoveInDate == null || t.MoveInDate <= periodEnd))
            .OrderByDescending(t => t.MoveOutDate == null).ThenByDescending(t => t.MoveInDate)
            .FirstOrDefaultAsync(ct);
        // Ha a beköltözés dátuma pontatlan (az időszak utánra esik), a jelenlegi bérlő kapja.
        return tenant ?? await db.Tenants.Where(t => t.PropertyId == propertyId && t.MoveOutDate == null)
            .OrderByDescending(t => t.MoveInDate).FirstOrDefaultAsync(ct);
    }

    // A bizonylatra az ingatlan minden még nem zárolt (bizonylathoz nem kötött) rezsitétele kerül, amely az időszakra
    // vagy legfeljebb 35 hónappal korábbra (összesen 36 hónap) szól: a később felvett tételek így a következő
    // bizonylaton számlázódnak. Az újragenerált bizonylat saját korábbi tételei is ide tartoznak. A bevezetés előtti,
    // kizárt (ExcludedFromBilling) tétel csak a saját hónapja bizonylatára kerülhet.
    private async Task<List<UtilityCostEntry>> BillableEntriesAsync(Guid propertyId, int year, int month, Guid? existingId, CancellationToken ct)
    {
        var (fromYear, fromMonth) = BillingWindowStart(year, month);
        var fromKey = fromYear * 12 + fromMonth;
        var toKey = year * 12 + month;
        return await db.UtilityCostEntries
            .Where(u => u.PropertyId == propertyId && (!u.ExcludedFromBilling || (u.Year == year && u.Month == month))
                && (u.InvoiceId == null || u.InvoiceId == existingId)
                && u.Year * 12 + u.Month >= fromKey && u.Year * 12 + u.Month <= toKey)
            .ToListAsync(ct);
    }

    // A megadott rezsitételek csatolmányai, a tételek sorrendjében (tételen belül a feltöltés sorrendjében).
    private async Task<List<Document>> AttachmentsOfAsync(IReadOnlyList<UtilityCostEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return [];
        var ids = entries.Select(e => e.Id).ToList();
        var docs = await db.Documents.Where(d => d.UtilityCostEntryId != null && ids.Contains(d.UtilityCostEntryId.Value)).ToListAsync(ct);
        return [.. docs.OrderBy(d => ids.IndexOf(d.UtilityCostEntryId!.Value)).ThenBy(d => d.UploadedAt)];
    }

    // Mennek-e a csatolmányok a levéllel: a bérlő beállítása dönt; ha nincs beállítva, a kézi készítéskor adott válasz
    // (ennek hiányában, pl. az időzített küldésnél: nem).
    public static bool ShouldSendAttachments(Tenant? tenant, bool? answer) => tenant?.InvoiceAttachments switch
    {
        InvoiceAttachmentPreference.Send => true,
        InvoiceAttachmentPreference.DontSend => false,
        _ => answer ?? false,
    };

    // Kézi bizonylatkészítés előtt: milyen csatolmányok mennének a levéllel, és kell-e rákérdezni. Ugyanazokat a
    // tételeket nézi, amelyek a bizonylatra kerülnének.
    public async Task<Contracts.InvoiceAttachmentPlanDto> PlanAttachmentsAsync(Property property, int year, int month, CancellationToken ct)
    {
        var existingId = await db.Invoices
            .Where(i => i.PropertyId == property.Id && i.PeriodYear == year && i.PeriodMonth == month
                && i.CancelledAt == null && i.CancelsInvoiceId == null)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        var tenant = await FindTenantAsync(property.Id, year, month, ct);
        var entries = (await BillableEntriesAsync(property.Id, year, month, existingId, ct)).Where(u => u.Amount != 0)
            .OrderBy(u => u.Year == year && u.Month == month ? 0 : 1)
            .ThenBy(u => u.Year).ThenBy(u => u.Month).ThenBy(u => u.CreatedAt).ThenBy(u => u.Label)
            .ToList();
        var labels = entries.ToDictionary(e => e.Id, e => e.Label);
        var docs = await AttachmentsOfAsync(entries, ct);
        return new Contracts.InvoiceAttachmentPlanDto(
            tenant?.Name, tenant?.InvoiceAttachments ?? InvoiceAttachmentPreference.Ask, !string.IsNullOrWhiteSpace(tenant?.Email),
            [.. docs.Select(d => new Contracts.InvoiceAttachmentDto(d.Id, d.Name, d.FileName, d.SizeBytes, labels[d.UtilityCostEntryId!.Value]))]);
    }

    // Sztornózás: a kiállított (legenerált vagy kiküldött) bizonylatról sztornó bizonylat készül az eredeti sorok
    // negatív összegeivel, saját sorszámmal („…-S”), az eredetire hivatkozva. Az eredeti „sztornózott” lesz; mindkettő
    // megmarad, és a havi csomagba is bekerül. Ha az eredeti ki volt küldve, a sztornó is kimegy a bérlőnek.
    // Ezután az időszak rezsije szerkeszthető, és új számla készíthető rá.
    public async Task<Invoice> CancelAsync(Invoice original, Guid userId, CancellationToken ct)
    {
        if (original.IsStorno) throw new InvalidOperationException("Sztornó bizonylatot nem lehet sztornózni.");
        if (original.CancelledAt is not null) throw new InvalidOperationException("Ezt a bizonylatot már sztornózták.");
        if (original.Status == InvoiceStatus.Failed) throw new InvalidOperationException("Sikertelen bizonylatot nem kell sztornózni: nem lett kiállítva. Próbáld újra.");

        await db.Entry(original).Collection(i => i.Lines).LoadAsync(ct);
        await db.Entry(original).Reference(i => i.Property).LoadAsync(ct);
        await db.Entry(original).Reference(i => i.Tenant).LoadAsync(ct);
        var property = original.Property!;
        await db.Entry(property).Collection(p => p.PropertyOwners).Query().Include(po => po.Owner).LoadAsync(ct);
        var settings = await settingsService.GetAsync(ct);
        var now = DateTime.UtcNow;

        var storno = new Invoice
        {
            Id = Guid.NewGuid(),
            CompanyId = original.CompanyId,
            PropertyId = original.PropertyId,
            TenantId = original.TenantId,
            PeriodYear = original.PeriodYear,
            PeriodMonth = original.PeriodMonth,
            Number = $"{original.Number}-S",
            IssuedAt = now,
            DueDate = DateOnly.FromDateTime(now),
            CancelsInvoiceId = original.Id,
            Status = InvoiceStatus.Generated,
        };
        var lines = InvoiceLine.InDisplayOrder(original.Lines).Select((l, i) => new InvoiceLine
        {
            Id = Guid.NewGuid(), CompanyId = original.CompanyId, InvoiceId = storno.Id, Label = l.Label, Amount = -l.Amount, Position = i,
        }).ToList();
        // A sztornó mindig pontosan az eredeti végösszegét nullázza. Ha az eredeti tételei nem adják ki a végösszeget
        // (régi vagy importált bizonylat), a különbség egy külön soron szerepel.
        var difference = -original.AmountTotal - lines.Sum(l => l.Amount);
        if (difference != 0)
        {
            lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), CompanyId = original.CompanyId, InvoiceId = storno.Id,
                Label = "Egyéb tétel az eredeti bizonylat végösszege szerint", Amount = difference, Position = lines.Count,
            });
        }
        storno.AmountTotal = -original.AmountTotal;

        byte[]? pdf;
        if (original.Provider is null)
        {
            pdf = InvoicePdfGenerator.Generate(storno, property, original.Tenant, settings, lines, original.Number);
        }
        else
        {
            // Számlázóban kiállított számla sztornója is ott készül (saját sorszámmal).
            var provider = providers.ByName(original.Provider)
                ?? throw new InvalidOperationException("A számlát kiállító számlázó nincs beállítva.");
            if (!Invoicing.InvoicingProviders.IsConfigured(settings) || settings.InvoiceProvider != original.Provider)
            {
                throw new InvalidOperationException($"A sztornóhoz a(z) {original.Provider} összekötésnek beállítva kell lennie (Beállítások).");
            }
            Invoicing.ProviderInvoice cancelled;
            try { cancelled = await provider.CancelAsync(settings, original, ct); }
            catch (Invoicing.ProviderException ex) { throw new InvalidOperationException(ex.Message, ex); }
            storno.Provider = original.Provider;
            storno.ProviderDocumentId = cancelled.DocumentId;
            storno.Number = cancelled.Number;
            pdf = cancelled.Pdf;
            if (pdf is null)
            {
                try { pdf = await provider.DownloadPdfAsync(settings, storno, ct); }
                catch (Invoicing.ProviderException ex) { logger.LogWarning(ex, "Storno PDF {Number} not available yet", storno.Number); }
            }
        }
        if (pdf is not null && await storage.IsConfiguredAsync(ct))
        {
            try
            {
                var key = InvoicePdfStore.StorageKey(storno);
                await storage.UploadAsync(key, pdf, "application/pdf", ct);
                storno.PdfStorageKey = key;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Storing storno PDF {Number} in S3 failed", storno.Number);
            }
        }

        var tenant = original.Tenant;
        if (pdf is not null && original.Status == InvoiceStatus.Sent && !string.IsNullOrWhiteSpace(tenant?.Email))
        {
            var kind = original.Provider is null ? "bizonylat" : "számla";
            var kindAcc = original.Provider is null ? "bizonylatot" : "számlát";
            var subject = $"Sztornó {kind} — {property.Name}";
            var text = $"A(z) {original.Number} számú {kindAcc} ({original.PeriodYear}.{original.PeriodMonth:D2}) sztornóztuk. "
                + $"A sztornó {kindAcc} ({storno.Number}) mellékeltük.\n\nKérjük, az eredeti {kindAcc} tekintsd érvénytelennek.";
            var html = string.Concat(text.Split("\n\n").Select(p => $"""<p style="margin:0 0 12px;">{System.Net.WebUtility.HtmlEncode(p)}</p>"""));
            var body = EmailTemplate.Render(preheader: subject, heading: subject, bodyHtml: html);
            try
            {
                if (await emailSender.SendAsync(tenant.Email!, EmailTemplate.UniqueSubject(subject), body, text,
                        new EmailAttachment($"{storno.Number}.pdf", pdf, "application/pdf"), ct))
                {
                    storno.Status = InvoiceStatus.Sent;
                    storno.SentAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sending storno {Number} failed", storno.Number);
            }
        }

        original.CancelledAt = now;
        original.CancelledByUserId = userId;
        // A sztornózott bizonylat rezsitételei feloldódnak: szerkeszthetők, és a következő bizonylatra kerülnek.
        await db.UtilityCostEntries.Where(u => u.InvoiceId == original.Id).ForEachAsync(u => u.InvoiceId = null, ct);
        db.Invoices.Add(storno);
        db.InvoiceLines.AddRange(lines);
        await db.SaveChangesAsync(ct);
        return storno;
    }

    public const string SzjaLabel = "SZJA levonás (15%)";
    public const decimal SzjaRate = 0.15m;
    // Ennyi hónapra visszamenőleg lehet rezsitételt utólag felvenni (az időszak hónapjával együtt).
    public const int LateUtilityMonths = 36;

    public static (int Year, int Month) BillingWindowStart(int year, int month)
    {
        var key = year * 12 + (month - 1) - (LateUtilityMonths - 1);
        return (key / 12, key % 12 + 1);
    }

    public static (int Year, int Month) RentPeriod(Tenant? tenant, int year, int month) =>
        tenant?.RentPaymentTiming == RentPaymentTiming.Arrears
            ? (month == 1 ? (year - 1, 12) : (year, month - 1))
            : (year, month);

    // Csak magánszemély bérbeadónál (saját bizonylat): cégnél a társasági adózás miatt nincs ilyen levonás.
    public static decimal SzjaDeduction(Company company, bool viaProvider, Tenant? tenant, decimal rentHuf) =>
        tenant is { TenantPaysSzja: true } && !company.IsBusiness && !viaProvider && rentHuf > 0
            ? -Math.Round(rentHuf * SzjaRate, 0, MidpointRounding.AwayFromZero)
            : 0;

    private static string MonthName(int month) =>
        new[] { "január", "február", "március", "április", "május", "június", "július", "augusztus", "szeptember", "október", "november", "december" }[month - 1];

    private async Task<(decimal AmountHuf, string? ConversionNote)> ResolveRentAmountAsync(Property property, Tenant? tenant, CancellationToken ct)
    {
        var currency = tenant?.RentCurrency ?? RentCurrency.HUF;
        if (currency == RentCurrency.HUF)
        {
            return (property.RentAmount, null);
        }

        var currencyCode = currency.ToString();
        var rate = await db.ExchangeRates
            .Where(r => r.CurrencyCode == currencyCode)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Nincs elérhető MNB árfolyam ehhez: {currencyCode}. A rezsi/díj számításhoz az Árfolyamok oldalon kell legalább egy sikeres lekérdezésnek megtörténnie.");

        var amountHuf = Math.Round(property.RentAmount * rate.RateToHuf, 0, MidpointRounding.AwayFromZero);
        var note = $"Bérleti díj devizában: {MoneyFormat.N2(property.RentAmount)} {currencyCode} × {MoneyFormat.N2(rate.RateToHuf)} Ft/{currencyCode} MNB árfolyamon ({rate.RateDate:yyyy.MM.dd.}).";
        return (amountHuf, note);
    }

    // A bizonylat száma "{Előtag}-{hónap}-{év}" alakú, pl. "A22-8-2026" — az ingatlanon
    // beállított előtaggal (Property.InvoicePrefix), hónap nullák nélkül.
    private static string BuildInvoiceNumber(Property property, int year, int month)
    {
        var prefix = string.IsNullOrWhiteSpace(property.InvoicePrefix) ? "SZLA" : property.InvoicePrefix;
        return $"{prefix}-{month}-{year}";
    }
}

// A számla-generálás egy lépése a folyamatjelzőhöz (ld. InvoicesController.GenerateWithProgress).
public record GenerationProgress(string Step, IReadOnlyList<string>? Plan = null);

public static class GenerationSteps
{
    public const string Plan = "plan";
    public const string Data = "data";
    public const string Pdf = "pdf";
    public const string Provider = "provider";
    public const string Upload = "upload";
    public const string Email = "email";
    public const string Save = "save";
}
