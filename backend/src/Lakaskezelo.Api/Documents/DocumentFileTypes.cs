using System.Text;

namespace Lakaskezelo.Api.Documents;

// Engedélyezett fájltípusok. A típust a kiterjesztés adja, de a tartalom elejét (aláírás) is ellenőrizzük,
// hogy pl. egy átnevezett HTML ne kerülhessen fel PDF-ként. A Content-Type-ot mindig mi határozzuk meg,
// nem a böngésző által küldött értéket használjuk. (SVG és HTML szándékosan nincs a listán: szkriptet
// tartalmazhatnak.)
public static class DocumentFileTypes
{
    private record FileType(string ContentType, Func<byte[], bool> Matches);

    private static bool StartsWith(byte[] data, params byte[] prefix) =>
        data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static bool Ascii(byte[] data, int offset, string text) =>
        data.Length >= offset + text.Length && Encoding.ASCII.GetString(data, offset, text.Length) == text;

    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    // Szövegfájl: nincs benne NUL bájt (bináris tartalom) az első 8 KB-ban.
    private static bool IsText(byte[] data) => !data.AsSpan(0, Math.Min(data.Length, 8192)).Contains((byte)0);

    private static bool IsHeic(byte[] data) =>
        Ascii(data, 4, "ftyp") && data.Length >= 12
        && new[] { "heic", "heix", "hevc", "heim", "heis", "mif1", "msf1" }.Contains(Encoding.ASCII.GetString(data, 8, 4));

    private static readonly Dictionary<string, FileType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new("application/pdf", d => Ascii(d, 0, "%PDF-")),
        [".jpg"] = new("image/jpeg", d => StartsWith(d, 0xFF, 0xD8, 0xFF)),
        [".jpeg"] = new("image/jpeg", d => StartsWith(d, 0xFF, 0xD8, 0xFF)),
        [".png"] = new("image/png", d => StartsWith(d, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)),
        [".gif"] = new("image/gif", d => Ascii(d, 0, "GIF8")),
        [".webp"] = new("image/webp", d => Ascii(d, 0, "RIFF") && Ascii(d, 8, "WEBP")),
        [".heic"] = new("image/heic", IsHeic),
        [".heif"] = new("image/heif", IsHeic),
        [".doc"] = new("application/msword", d => StartsWith(d, Ole)),
        [".xls"] = new("application/vnd.ms-excel", d => StartsWith(d, Ole)),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", d => StartsWith(d, Zip)),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", d => StartsWith(d, Zip)),
        [".odt"] = new("application/vnd.oasis.opendocument.text", d => StartsWith(d, Zip)),
        [".ods"] = new("application/vnd.oasis.opendocument.spreadsheet", d => StartsWith(d, Zip)),
        [".txt"] = new("text/plain", IsText),
        [".csv"] = new("text/csv", IsText),
    };

    public static string AllowedList => "PDF, JPG, PNG, GIF, WEBP, HEIC, DOC(X), XLS(X), ODT, ODS, TXT, CSV";

    // A fájl Content-Type-ja, vagy null, ha a kiterjesztés nem engedélyezett vagy a tartalom nem egyezik vele.
    public static string? Detect(string fileName, byte[] data)
    {
        var ext = Path.GetExtension(fileName);
        return Types.TryGetValue(ext, out var type) && type.Matches(data) ? type.ContentType : null;
    }

    public static bool IsAllowedExtension(string fileName) => Types.ContainsKey(Path.GetExtension(fileName));
}
