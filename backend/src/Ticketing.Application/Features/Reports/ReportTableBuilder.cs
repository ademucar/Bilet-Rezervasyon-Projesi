using System.Globalization;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Reporting;

namespace Ticketing.Application.Features.Reports;

/// <summary>
/// Rapor verisini bicimden bağımsız bir TABLOYA cevirir.
/// PDF Sprint 13 export akisinin ortasindaki parca.
/// </summary>
/// <remarks>
/// ==================================================================
/// BICIMLENDIRME NEDEN BURADA?
/// ==================================================================
/// Para, tarih ve yüzde bicimlendirmesini yaziciya (CSV/Excel/PDF)
/// birakmadim. Sebep: uc yazici da aynı bicimlendirmeyi tekrar
/// yazmak zorunda kalırdı ve birinde farklı yaparsak aynı rapor
/// Excel'de başka, PDF'te başka görünürdü.
///
/// Burada bir kez bicimlendirip metin olarak veriyoruz.
///
/// ------------------------------------------------------------------
/// NEDEN InvariantCulture?
/// ------------------------------------------------------------------
/// Rapor dosyalari BASKA SISTEMLERE aktariliyor: muhasebe yazilimi,
/// bir başka Excel, bir veri ambari.
///
/// Turkce kulturde ondalik ayirici VIRGUL. "1.234,56" yazan bir CSV,
/// virgulle ayrilmis bir dosyada SUTUN KAYMASINA yol acar -- alan
/// tirnak icine alinsa bile karsi taraf sayiyi ayristiramaz.
///
/// Nokta ayirici (1234.56) makineler için evrensel. Ekranda Turkce
/// göstermek arayuzun isi; disa aktarilan dosyanin isi değil.
/// ------------------------------------------------------------------
/// </remarks>
internal static class ReportTableBuilder
{
    // ==============================================================
    // BASLIK DIZILERI static readonly -- CA1861
    // ==============================================================
    // Metot içinde "new[] { ... }" yazsaydık her cagirimda YENI bir
    // dizi ayrilirdi. Analiz kuralı bunu yakaladi.
    //
    // Rapor üretimi zaten arka planda ve seyrek çalışıyor, yani
    // performans farki onemsiz. Yine de kurala uyuyorum: basliklar
    // zaten sabit ve tek yerde durmalari okunakliligi da artiriyor.
    // ==============================================================
    private static readonly string[] SalesSummaryHeaders = ["Metrik", "Deger"];

    private static readonly string[] OccupancyHeaders =
        ["Etkinlik", "Tarih", "Toplam", "Satilan", "Kilitli", "Bos", "Doluluk %"];

    private static readonly string[] RevenueHeaders = ["Etkinlik", "Bilet", "Gelir"];

    private static readonly string[] TicketTypeHeaders =
        ["Bilet türü", "Satilan", "Iade", "Gelir", "Ortalama fiyat"];

    private static readonly string[] PaymentStatusHeaders =
        ["Durum", "Adet", "Tutar", "Oran %"];

    public static async Task<ReportTable> BuildAsync(
        IApplicationDbContext context,
        ReportScope scope,
        ReportExportPayload data,
        CancellationToken cancellationToken)
    {
        return data.Type switch
        {
            ReportType.SalesSummary => await SalesSummaryAsync(
                context, scope, data, cancellationToken).ConfigureAwait(false),

            ReportType.EventOccupancy => await EventOccupancyAsync(
                context, scope, cancellationToken).ConfigureAwait(false),

            ReportType.RevenueByEvent => await RevenueByEventAsync(
                context, scope, data, cancellationToken).ConfigureAwait(false),

            ReportType.TicketTypeSales => await TicketTypeSalesAsync(
                context, scope, data, cancellationToken).ConfigureAwait(false),

            ReportType.PaymentStatuses => await PaymentStatusesAsync(
                context, scope, data, cancellationToken).ConfigureAwait(false),

            // Bu dala DUSULMEMELI: ExportReportCommandValidator
            // IsInEnum ile taniamayan değerleri zaten reddediyor.
            //
            // Yine de yazıyorum: sessizce boş rapor uretmektense
            // acikca patlamak iyi. Outbox bunu dead letter yapar ve
            // izleme ekraninda görünür.
            _ => throw new ArgumentOutOfRangeException(
                nameof(data), data.Type, "Bilinmeyen rapor türü."),
        };
    }

    // ---- Bicimlendirme yardimcilari ----

    private static string P(decimal tutar)
        => tutar.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Y(double yuzde)
        => yuzde.ToString("0.0", CultureInfo.InvariantCulture);

    private static string T(DateTimeOffset tarih)
        => tarih.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string S(int sayi)
        => sayi.ToString(CultureInfo.InvariantCulture);

    // ==================================================================
    // 1) SATIS OZETI
    // ==================================================================

    private static async Task<ReportTable> SalesSummaryAsync(
        IApplicationDbContext context,
        ReportScope scope,
        ReportExportPayload data,
        CancellationToken cancellationToken)
    {
        var r = await GetSalesSummaryReportQueryHandler
            .RunAsync(context, scope, data.From, data.To, cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // TEK SATIRLIK RAPORU DIKEY YAZIYORUM
        // ==============================================================
        // Satış özeti 8 metrikten olusan TEK bir kayıt. Yatay yazsaydık
        // 8 sutunlu ve 1 satirlik bir tablo çıkardı -- Excel'de saga
        // doğru kaydirilmasi gereken, PDF'te sigmayan bir sey.
        //
        // Metrik/deger ciftleri halinde DIKEY yazmak, tek kayıtlı
        // raporlar için doğru biçim.
        // ==============================================================
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Satılan bilet", S(r.TicketCount) },
            new[] { "Brüt gelir", $"{P(r.GrossRevenue)} {r.Currency}" },
            new[] { "İade tutarı", $"{P(r.RefundedAmount)} {r.Currency}" },
            new[] { "Net gelir", $"{P(r.NetRevenue)} {r.Currency}" },
            new[] { "İade edilen bilet", S(r.RefundedTicketCount) },
            new[] { "Toplam rezervasyon", S(r.ReservationCount) },
            new[] { "Süresi dolan rezervasyon", S(r.ExpiredReservationCount) },
        };

        return new ReportTable("Satış Özeti", SalesSummaryHeaders, rows);
    }

    // ==================================================================
    // 2) ETKİNLİK DOLULUGU
    // ==================================================================

    private static async Task<ReportTable> EventOccupancyAsync(
        IApplicationDbContext context,
        ReportScope scope,
        CancellationToken cancellationToken)
    {
        var rows = await GetEventOccupancyReportQueryHandler
            .RunAsync(context, scope, cancellationToken)
            .ConfigureAwait(false);

        return new ReportTable(
            "Etkinlik Dolulugu",
            OccupancyHeaders,
            rows.Select(x => (IReadOnlyList<string>)new[]
            {
                x.Title,
                T(x.EventDate),
                S(x.TotalSeats),
                S(x.SoldSeats),
                S(x.LockedSeats),
                S(x.AvailableSeats),
                Y(x.OccupancyRate),
            }).ToList());
    }

    // ==================================================================
    // 3) ETKİNLİK BAZLI GELIR
    // ==================================================================

    private static async Task<ReportTable> RevenueByEventAsync(
        IApplicationDbContext context,
        ReportScope scope,
        ReportExportPayload data,
        CancellationToken cancellationToken)
    {
        var rows = await GetRevenueByEventReportQueryHandler
            .RunAsync(context, scope, data.From, data.To, cancellationToken)
            .ConfigureAwait(false);

        return new ReportTable(
            "Etkinlik Bazli Gelir",
            RevenueHeaders,
            rows.Select(x => (IReadOnlyList<string>)new[]
            {
                x.Title,
                S(x.TicketCount),
                P(x.Revenue),
            }).ToList());
    }

    // ==================================================================
    // 4) BİLET TURU SATISLARI
    // ==================================================================

    private static async Task<ReportTable> TicketTypeSalesAsync(
        IApplicationDbContext context,
        ReportScope scope,
        ReportExportPayload data,
        CancellationToken cancellationToken)
    {
        var rows = await GetTicketTypeSalesReportQueryHandler
            .RunAsync(context, scope, data.From, data.To, cancellationToken)
            .ConfigureAwait(false);

        return new ReportTable(
            "Bilet Türü Satışları",
            TicketTypeHeaders,
            rows.Select(x => (IReadOnlyList<string>)new[]
            {
                x.TicketTypeName,
                S(x.SoldCount),
                S(x.RefundedCount),
                P(x.Revenue),
                P(x.AveragePrice),
            }).ToList());
    }

    // ==================================================================
    // 5) ÖDEME DURUMLARI
    // ==================================================================

    private static async Task<ReportTable> PaymentStatusesAsync(
        IApplicationDbContext context,
        ReportScope scope,
        ReportExportPayload data,
        CancellationToken cancellationToken)
    {
        var rows = await GetPaymentStatusReportQueryHandler
            .RunAsync(context, scope, data.From, data.To, cancellationToken)
            .ConfigureAwait(false);

        return new ReportTable(
            "Ödeme Durumları",
            PaymentStatusHeaders,
            rows.Select(x => (IReadOnlyList<string>)new[]
            {
                x.StatusName,
                S(x.Count),
                P(x.TotalAmount),
                Y(x.Percentage),
            }).ToList());
    }
}
