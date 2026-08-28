namespace Ticketing.Application.Abstractions.Reporting;

/// <summary>Disa aktarma bicimi. PDF Sprint 13: Excel, CSV, PDF.</summary>
public enum ReportFormat
{
    Csv = 1,
    Excel = 2,
    Pdf = 3
}

/// <summary>
/// Bicimden bagimsiz rapor verisi.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN TABLO SEKLINDE ARA BIR MODEL?
/// ==================================================================
/// Her rapor tipi icin ayri bir Excel/CSV/PDF yazici yazmak
/// 5 rapor x 3 bicim = 15 metot demekti.
///
/// Bunun yerine her rapor kendini bir TABLOYA (baslik satiri +
/// hucre satirlari) ceviriyor; uc yazici da yalnizca bu tabloyu
/// biliyor. 5 + 3 = 8 parca, ve yeni bir rapor eklemek yeni bir
/// yazici gerektirmiyor.
///
/// Hucreler METIN olarak tutuluyor: bicimlendirme (para birimi,
/// tarih, ondalik ayirici) raporu ureten tarafta yapiliyor.
/// Yaziciya "bu sutun para" demek gerekseydi, tablo modeli tip
/// sistemi tasimak zorunda kalirdi ve is yeniden karmasiklasirdi.
/// ==================================================================
/// </remarks>
public sealed record ReportTable(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Uretilen dosya.</summary>
public sealed record ExportedReport(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Rapor tablosunu istenen bicimde dosyaya cevirir.
/// </summary>
/// <remarks>
/// Arayuz Application katmaninda, uygulamasi Infrastructure'da.
/// Application, ClosedXML veya QuestPDF'i TANIMIYOR -- mimari
/// testimiz bunu her derlemede dogruluyor.
/// </remarks>
public interface IReportExporter
{
    ExportedReport Export(ReportTable table, ReportFormat format);
}
