using System.Text;
using Lakaskezelo.Api.Documents;

namespace Lakaskezelo.Tests;

// A feltöltött dokumentum típusát a kiterjesztés adja, de a tartalom elejének is egyeznie kell vele.
public class DocumentFileTypesTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Real_files_are_accepted_with_server_side_content_type()
    {
        Assert.Equal("application/pdf", DocumentFileTypes.Detect("Szerződés.PDF", Ascii("%PDF-1.7\n...")));
        Assert.Equal("image/jpeg", DocumentFileTypes.Detect("fotó.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0]));
        Assert.Equal("image/png", DocumentFileTypes.Detect("szamla.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("text/plain", DocumentFileTypes.Detect("meroallas.txt", Encoding.UTF8.GetBytes("Villany: 123 kWh")));
        Assert.NotNull(DocumentFileTypes.Detect("jegyzokonyv.docx", [0x50, 0x4B, 0x03, 0x04, 0x14]));
        Assert.Equal("image/heic", DocumentFileTypes.Detect("IMG_1234.HEIC", [0, 0, 0, 24, .. Ascii("ftypheic")]));
    }

    [Theory]
    [InlineData("gonosz.pdf", "<html><script>alert(1)</script></html>")] // átnevezett HTML
    [InlineData("kep.png", "%PDF-1.4")]                                  // rossz kiterjesztés
    [InlineData("oldal.html", "<html></html>")]                          // nem engedélyezett típus
    [InlineData("rajz.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("program.exe", "MZ")]
    public void Mismatching_or_disallowed_files_are_rejected(string name, string content)
    {
        Assert.Null(DocumentFileTypes.Detect(name, Ascii(content)));
    }

    [Fact]
    public void Binary_content_is_not_accepted_as_text()
    {
        Assert.Null(DocumentFileTypes.Detect("adat.csv", [0x41, 0x00, 0x42]));
    }
}
