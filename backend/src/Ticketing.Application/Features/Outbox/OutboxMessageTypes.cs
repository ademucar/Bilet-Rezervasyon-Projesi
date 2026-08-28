namespace Ticketing.Application.Features.Outbox;

/// <summary>
/// Outbox mesaj turleri. PDF Sprint 9'un saydigi senaryolar.
///
/// ==================================================================
/// NEDEN SABIT? Neden enum degil?
/// ==================================================================
/// Bu deger veritabaninda METIN olarak saklaniyor ve yillarca orada
/// duracak. Enum kullansaydik iki sorun cikardi:
///
/// 1) Enum'u sayi olarak saklarsak, birinin enum siralamasini
///    degistirmesi TABLODAKI ESKI KAYITLARIN ANLAMINI degistirirdi.
///    "3 = EventCancelled" idi, araya bir deger eklendi, artik
///    "3 = PaymentSucceeded". Islenmemis mesajlar yanlis isleyiciye
///    gider.
///
/// 2) Enum'u metin olarak saklasak bile, bir uyeyi YENIDEN ADLANDIRMAK
///    derleyici hatasi vermez ama tablodaki eski kayitlar artik
///    hicbir isleyiciyle eslesmez -- sessizce olu mesaja donerler.
///
/// Sabit metinler bu riskleri gorunur kilar: degeri degistirmek
/// bilincli bir karar gerektirir ve gecis (migration) yazilmasi
/// gerektigi bellidir.
/// ==================================================================
/// </summary>
public static class OutboxMessageTypes
{
    /// <summary>Odeme tamamlandi, biletler uretildi. PDF: "Bilet satin alindi e-postasi".</summary>
    public const string TicketsIssued = "TicketsIssued";

    /// <summary>PDF: "Odeme basari bildirimi".</summary>
    public const string PaymentSucceeded = "PaymentSucceeded";

    /// <summary>PDF: "Rezervasyon suresi doldu bildirimi".</summary>
    public const string ReservationExpired = "ReservationExpired";

    /// <summary>PDF: "Etkinlik iptal bildirimi".</summary>
    public const string EventCancelled = "EventCancelled";

    /// <summary>PDF Sprint 9 Background Job: "Yaklasan etkinlik hatirlatmasi".</summary>
    public const string EventReminder = "EventReminder";

    /// <summary>PDF: "Rapor hazirlama".</summary>
    public const string DailySalesSummary = "DailySalesSummary";

    /// <summary>
    /// PDF Sprint 13: "Rapor uretimi background job olarak
    /// calistirilmali ve tamamlandiginda kullaniciya bildirim
    /// gonderilmelidir."
    /// </summary>
    public const string ReportExport = "ReportExport";

    /// <summary>PDF Sprint 14 e-posta sablonu: "Rezervasyon olusturuldu".</summary>
    public const string ReservationCreated = "ReservationCreated";
}

// ===================================================================
// PAYLOAD TIPLERI
// ===================================================================
// Payload veritabaninda JSON metni olarak duruyor. Bu record'lar o
// metnin SEMASI.
//
// Neden anonim nesne yerine record?
//   - Yazan taraf (komut) ile okuyan taraf (isleyici) AYNI tipi
//     kullaniyor. Alan adini birinde degistirip digerinde unutmak
//     derleyici hatasi veriyor.
//   - Anonim nesneyle yazip elle okusaydik, uyusmazlik ancak
//     CALISMA ZAMANINDA -- hem de arka planda, kimse bakmazken --
//     ortaya cikardi.
//
// DIKKAT: Bu tiplere alan EKLEMEK guvenlidir (eski kayitlarda alan
// olmaz, varsayilan deger gelir). Alan SILMEK veya YENIDEN ADLANDIRMAK
// tablodaki islenmemis eski mesajlari bozar.
// ===================================================================

/// <param name="TicketIds">Bilgi amacli; isleyici biletleri yine de veritabanindan okur.</param>
public sealed record TicketsIssuedPayload(
    Guid ReservationId,
    Guid UserId,
    Guid PaymentId,
    IReadOnlyList<Guid> TicketIds);

public sealed record PaymentSucceededPayload(
    Guid PaymentId,
    Guid ReservationId,
    Guid UserId,
    decimal Amount,
    string Currency);

public sealed record ReservationExpiredPayload(
    Guid ReservationId,
    Guid UserId,
    string ReservationCode,
    string EventTitle,
    int SeatCount);

public sealed record EventCancelledPayload(
    Guid EventId,
    string EventTitle,
    string? Reason);

public sealed record EventReminderPayload(
    Guid EventSessionId,
    Guid UserId,
    string EventTitle,
    string VenueName,
    DateTimeOffset StartDate);

public sealed record ReservationCreatedPayload(
    Guid ReservationId,
    Guid UserId,
    string ReservationCode,
    int SeatCount,
    int ExpiresInMinutes);

public sealed record DailySalesSummaryPayload(
    DateOnly Date,
    int TicketCount,
    decimal GrossAmount,
    decimal RefundedAmount,
    string Currency,
    int ReservationCount,
    int ExpiredReservationCount);
