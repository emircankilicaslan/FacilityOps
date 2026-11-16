using ClosedXML.Excel;
using FacilityOps.Api.Services;

namespace FacilityOps.Tests;

/// <summary>Excel üretimi için birim testleri.</summary>
public class XlsxTests
{
    private static IXLWorksheet Ac(byte[] bayt) => new XLWorkbook(new MemoryStream(bayt)).Worksheet(1);

    [Fact]
    public void BaslikVeTipliDegerler_DogruYazilir()
    {
        var an = new DateTime(2026, 11, 12, 10, 30, 15, DateTimeKind.Utc);
        var s = Ac(Xlsx.Olustur("Deneme", ["Ad", "Zaman", "Sayı", "Gecikmiş"],
            [new object?[] { "Trafo 1", an, 5, true }, new object?[] { "Trafo 2", null, 7L, false }]));

        Assert.Equal("Deneme", s.Name);
        Assert.True(s.Cell(1, 1).Style.Font.Bold);
        Assert.Equal("Trafo 1", s.Cell(2, 1).GetString());
        Assert.Equal(XLDataType.DateTime, s.Cell(2, 2).DataType);
        Assert.Equal(an, s.Cell(2, 2).GetDateTime());
        Assert.Equal(5, s.Cell(2, 3).GetDouble());
        Assert.Equal("Evet", s.Cell(2, 4).GetString());
        Assert.True(s.Cell(3, 2).IsEmpty());
        Assert.Equal("Hayır", s.Cell(3, 4).GetString());
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A2)")]
    [InlineData("-2+3")]
    [InlineData("@cmd")]
    [InlineData("=HYPERLINK(\"http://kotu.example\",\"tikla\")")]
    public void FormulGibiGorunenMetin_FormulOlarakCalismaz(string deger)
    {
        var hucre = Ac(Xlsx.Olustur("Deneme", ["Metin"], [new object?[] { deger }])).Cell(2, 1);
        Assert.False(hucre.HasFormula);
        Assert.Equal(XLDataType.Text, hucre.DataType);
        Assert.Equal(deger, hucre.GetString());
    }

    [Fact]
    public void BosListe_YalnizcaBasligiIcerir()
    {
        var s = Ac(Xlsx.Olustur("Bos", ["A", "B"], []));
        Assert.Equal("A", s.Cell(1, 1).GetString());
        Assert.True(s.Cell(2, 1).IsEmpty());
    }
}
