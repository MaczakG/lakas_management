using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Lakaskezelo.Api.Settings;

namespace Lakaskezelo.Api.Storage;

// A számla-PDF-ek és a feltöltött dokumentumok tárhelye (Amazon S3) — platform-szintű: a bucket neve és régiója a Platform admin
// felületen állítható (az üzemeltető AppSettings sora), a cégek PDF-jei a companies/{cégId}/ előtag
// alatt vannak. A hozzáférést élesben az EC2-höz rendelt IAM-szerep adja, kulcsot nem tárolunk.
public class S3InvoiceStorage(AppSettingsService settingsService)
{
    private const string DefaultRegion = "eu-central-1";

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace((await settingsService.GetPlatformAsync(ct)).S3BucketName);

    public async Task UploadAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        using (client)
        using (var stream = new MemoryStream(content))
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
            }, ct);
        }
    }

    // null, ha az objektum nem létezik (pl. a feltöltés annak idején nem sikerült).
    public async Task<byte[]?> DownloadAsync(string key, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        using (client)
        {
            try
            {
                using var response = await client.GetObjectAsync(bucket, key, ct);
                using var buffer = new MemoryStream();
                await response.ResponseStream.CopyToAsync(buffer, ct);
                return buffer.ToArray();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        using (client)
        {
            await client.DeleteObjectAsync(bucket, key, ct);
        }
    }

    // Egy próba-objektum feltöltése és törlése — a Beállítások oldal "Kapcsolat tesztelése" gombja.
    public async Task<(bool Success, string? Error)> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!await IsConfiguredAsync(ct)) return (false, "Nincs megadva S3 bucket.");
        try
        {
            var key = $"_kapcsolat-teszt/{Guid.NewGuid():N}.txt";
            await UploadAsync(key, "ok"u8.ToArray(), "text/plain", ct);
            var (client, bucket) = await CreateClientAsync(ct);
            using (client)
            {
                await client.DeleteObjectAsync(bucket, key, ct);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(AmazonS3Client Client, string Bucket)> CreateClientAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetPlatformAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.S3BucketName))
        {
            throw new InvalidOperationException("Nincs megadva S3 bucket (Platform admin → E-mail és tárhely).");
        }
        var region = string.IsNullOrWhiteSpace(settings.S3Region) ? DefaultRegion : settings.S3Region.Trim();
        return (new AmazonS3Client(RegionEndpoint.GetBySystemName(region)), settings.S3BucketName.Trim());
    }
}
