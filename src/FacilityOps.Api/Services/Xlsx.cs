using ClosedXML.Excel;

namespace FacilityOps.Api.Services;

/// <summary>Excel (.xlsx) üretir. Metin hücreleri her zaman düz metin yazılır; "=" ile başlayan kullanıcı girdisi formül olarak çalışmaz.</summary>
public static class Xlsx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Olustur(string sayfaAdi, IReadOnlyList<string> basliklar, IEnumerable<IReadOnlyList<object?>> satirlar)
    {
        using var kitap = new XLWorkbook();
        var sayfa = kitap.AddWorksheet(sayfaAdi);

        for (var c = 0; c < basliklar.Count; c++) sayfa.Cell(1, c + 1).SetValue(basliklar[c]);
        sayfa.Row(1).Style.Font.Bold = true;
        sayfa.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9EEF5");

        var r = 2;
        foreach (var satir in satirlar)
        {
            for (var c = 0; c < satir.Count; c++)
            {
                var hucre = sayfa.Cell(r, c + 1);
                switch (satir[c])
                {
                    case null: break;
                    case string s: hucre.SetValue(s); break;
                    case DateTime d: hucre.SetValue(d); hucre.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss"; break;
                    case bool b: hucre.SetValue(b ? "Evet" : "Hayır"); break;
                    case int i: hucre.SetValue(i); break;
                    case long l: hucre.SetValue(l); break;
                    case decimal m: hucre.SetValue(m); break;
                    default: hucre.SetValue(satir[c]!.ToString() ?? ""); break;
                }
            }
            r++;
        }

        sayfa.SheetView.FreezeRows(1);
        sayfa.Columns().AdjustToContents(1, Math.Min(r, 500), 8, 60);
        if (r > 2) sayfa.Range(1, 1, r - 1, basliklar.Count).SetAutoFilter();

        using var akis = new MemoryStream();
        kitap.SaveAs(akis);
        return akis.ToArray();
    }
}
