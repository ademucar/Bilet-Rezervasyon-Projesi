using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ticketing.Application.Abstractions.Reporting;

namespace Ticketing.Infrastructure.Reporting;

/// <summary>
/// Rapor tablosunu CSV, Excel veya PDF'e cevirir. PDF Sprint 13.
/// </summary>
internal sealed class ReportExporter : IReportExporter
{
    /// <summary>
    /// QUESTPDF LISANSI -- KODDA BELIRTILMEK ZORUNDA
    ///
    /// QuestPDF, "Community" lisansi altinda yillik geliri 1 milyon
    /// USD altindaki kuruluslar için UCRETSIZ. Bu proje için uygun.
    ///
    /// Ama kutuphane, lisans turunun ACIKCA belirtilmesini sart
    /// kosuyor. Belirtilmezse ilk PDF uretiminde istisna firlatiyor.
    ///
    /// Static kurucu: uygulama omrunde BIR KEZ ve ilk kullanimdan
    /// önce çalışıyor. Her Export cagrisinda atama yapmak gereksiz
    /// olurdu.
    ///
    /// NOT: Bu proje ticari bir urune donuserse lisans yeniden
    /// degerlendirilmeli. Bunu buraya yazıyorum ki karar görünür
    /// kalsin.
    /// </summary>
    static ReportExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ExportedReport Export(ReportTable table, ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(table);

        // Dosya adinda kullanilamayan karakterleri temizliyorum.
        //
        // Rapor başlığı kullanicidan gelmiyor ama tarih iceriyor ve
        // ilerde ozellestirilebilir olabilir. Dosya adina doğrudan
        // metin koymak, "../" gibi dizin gecisi denemelerine kapi
        // acar (path traversal).
        var guvenliAd = string.Concat(
            table.Title.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' '))
            .Trim()
            .Replace(' ', '-');

        var zaman = DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);

        return format switch
        {
            ReportFormat.Csv => new ExportedReport(
                $"{guvenliAd}-{zaman}.csv",
                "text/csv",
                CsvUret(table)),

            ReportFormat.Excel => new ExportedReport(
                $"{guvenliAd}-{zaman}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelUret(table)),

            ReportFormat.Pdf => new ExportedReport(
                $"{guvenliAd}-{zaman}.pdf",
                "application/pdf",
                PdfUret(table)),

            _ => throw new ArgumentOutOfRangeException(
                nameof(format), format, "Desteklenmeyen disa aktarma bicimi."),
        };
    }

    // CSV

    /// <summary>
    /// RFC 4180 uyumlu CSV üretir.
    /// </summary>
    /// <remarks>
    /// NEDEN KUTUPHANE KULLANMIYORUM?
    ///
    /// CsvHelper gibi paketler var ama CSV yazma kurallari toplam
    /// uc satır:
    ///   - Alan içinde virgul, tirnak veya satır sonu varsa tirnak ic
    ///   - Icerideki tirnaklari ikiye katla
    ///   - Satirlari CRLF ile ayir
    ///
    /// Ucuncu bir bagimlilik eklemek; güvenlik taramasi, surum takibi
    /// ve gecisli bagimlilik maliyeti getirir. Bu kadar küçük bir is
    /// için degmez.
    ///
    /// (OKUMA farklı olurdu: CSV ayristirmak çok daha zor ve orada
    /// kutuphane kullanirdim.)
    ///
    /// UTF-8 BOM -- EXCEL ICIN ŞART
    ///
    /// BOM olmadan Excel, CSV'yi sistem kod sayfasiyla acar ve
    /// Turkce karakterler bozulur: "İstanbul" yerine "Ä°stanbul".
    ///
    /// Kullanıcının gozunde bu "sizin raporunuz bozuk" demektir --
    /// oysa dosya teknik olarak doğru. Uc baytlik BOM bu sorunu
    /// tamamen cozuyor.
    /// </remarks>
    private static byte[] CsvUret(ReportTable table)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(',', table.Headers.Select(Kacir)));

        foreach (var row in table.Rows)
        {
            sb.AppendLine(string.Join(',', row.Select(Kacir)));
        }

        // BOM'U ELLE EKLIYORUM -- YAKALADIGIM HATA
        //
        // Önce soyle yazmistim:
        //
        //     return new UTF8Encoding(true).GetBytes(...)
        //
        // "encoderShouldEmitUTF8Identifier: true" parametresi BOM
        // ekliyor SANDIM. EKLEMIYOR.
        //
        // O bayrak yalnızca GetPreamble() metodunun ne donduregini
        // belirliyor; GetBytes ONU KULLANMIYOR. BOM ancak StreamWriter
        // gibi preamble'i kendisi yazan siniflarla eklenir.
        //
        // Uretilen dosyayı inceleyerek buldum: ilk baytlar EF BB BF
        // yerine "Etki" (45 74 6B 69) idi.
        //
        // Yani yorumda "BOM ekliyorum" yaziyordu ama EKLENMIYORDU --
        // ve Turkce karakterler Excel'de bozuk cikacakti. Kodun
        // NIYETINI değil, URETTIGI CIKTIYI kontrol etmek gerekiyor.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        var preamble = encoding.GetPreamble();
        var content = encoding.GetBytes(sb.ToString());

        var result = new byte[preamble.Length + content.Length];

        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);

        return result;
    }

    private static string Kacir(string? deger)
    {
        var s = deger ?? string.Empty;

        var kacisGerekli = s.Contains(',', StringComparison.Ordinal)
                        || s.Contains('"', StringComparison.Ordinal)
                        || s.Contains('\n', StringComparison.Ordinal)
                        || s.Contains('\r', StringComparison.Ordinal);

        if (!kacisGerekli)
        {
            return s;
        }

        // Icerideki her tirnak ikiye katlanir, sonra tümü tirnak icine alinir.
        return $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    // EXCEL

    private static byte[] ExcelUret(ReportTable table)
    {
        using var workbook = new XLWorkbook();

        // Sayfa adı en fazla 31 karakter olabilir (Excel sınırı).
        // Uzun basliklarda kirpmasaydik ClosedXML istisna firlatirdi.
        var sayfaAdi = table.Title.Length > 31 ? table.Title[..31] : table.Title;

        var sheet = workbook.Worksheets.Add(sayfaAdi);

        for (var i = 0; i < table.Headers.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = table.Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                // Hucreleri METİN olarak yazıyorum.
                //
                // ClosedXML sayi gibi görünen değerleri otomatik
                // sayiya cevirebilir ve bu ISTEDIGIMIZ SEY DEĞİL:
                // "01" gibi bir bilet numarasi "1"e donusur, uzun
                // Guid'ler bilimsel gosterime kayar.
                //
                // Rapor bicimlendirmesi zaten üretim tarafında
                // yapıldı; burada olduğu gibi aktarmak doğru.
                sheet.Cell(r + 2, c + 1).SetValue(table.Rows[r][c]);
            }
        }

        // Başlık satirini dondur: uzun raporlarda asagi kaydirinca
        // sutun adları görünür kalır.
        sheet.SheetView.FreezeRows(1);

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    // PDF

    private static byte[] PdfUret(ReportTable table)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                // YATAY (LANDSCAPE) SAYFA
                //
                // Raporlarin çoğu 5-8 sutunlu. Dikey A4'te sutunlar
                // sikisip okunamaz hale gelir.
                //
                // Yatay cevirmek, tablo raporlari için doğru
                // varsayılan.
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header()
                    .Column(col =>
                    {
                        col.Item().Text(table.Title).FontSize(16).Bold();
                        col.Item().Text(
                            $"Oluşturulma: {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC")
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken1);
                    });

                page.Content()
                    .PaddingVertical(10)
                    .Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            // Tüm sutunlara esit genislik.
                            //
                            // Icerige göre otomatik genislik daha
                            // guzel olurdu ama QuestPDF'te bunun için
                            // her sutunun icerigini onceden olcmek
                            // gerekiyor. Rapor sayısı ve sutun
                            // cesitliligi dusunuldugunde esit dagitim
                            // yeterince okunakli.
                            for (var i = 0; i < table.Headers.Count; i++)
                            {
                                cols.RelativeColumn();
                            }
                        });

                        t.Header(header =>
                        {
                            foreach (var baslik in table.Headers)
                            {
                                header.Cell()
                                    .Background(Colors.Grey.Lighten2)
                                    .Padding(4)
                                    .Text(baslik).Bold();
                            }
                        });

                        foreach (var row in table.Rows)
                        {
                            foreach (var hucre in row)
                            {
                                t.Cell()
                                    .BorderBottom(0.5f)
                                    .BorderColor(Colors.Grey.Lighten2)
                                    .Padding(4)
                                    .Text(hucre);
                            }
                        }
                    });

                // Sayfa numarasi: çok sayfali raporlarda ciktinin
                // siralamasi kaybolmasin.
                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
            });
        }).GeneratePdf();
    }
}
