namespace Ticketing.Application.Features.Outbox;

/// <summary>
/// Outbox mesaj türleri. PDF Sprint 9'un saydığı senaryolar.
///
/// NEDEN SABIT? Neden enum değil?
///
/// Bu deger veritabaninda METİN olarak saklaniyor ve yillarca orada
/// duracak. Enum kullansaydım iki sorun çıkardı:
///
/// 1) Enum'u sayi olarak saklarsak, birinin enum siralamasini
///    degistirmesi TABLODAKI ESKİ KAYITLARIN ANLAMINI degistirirdi.
///    "3 = EventCancelled" idi, araya bir deger eklendi, artık
///    "3 = PaymentSucceeded". Islenmemis mesajlar yanlış isleyiciye
///    gider.
///
/// 2) Enum'u metin olarak saklasak bile, bir uyeyi YENIDEN ADLANDIRMAK
///    derleyici hatası vermez ama tablodaki eski kayitlar artık
///    hiçbir isleyiciyle eslesmez -- sessizce olu mesaja donerler.
///
/// Sabit metinler bu riskleri görünür kilar: değeri degistirmek
/// bilinçli bir karar gerektirir ve gecis (migration) yazilmasi
/// gerektigi bellidir.
/// </summary>
public static class OutboxMessageTypes
{
    /// <summary>Ödeme tamamlandı, biletler üretildi. PDF: "Bilet satin alındı e-postası".</summary>
    public const string TicketsIssued = "TicketsIssued";

    /// <summary>PDF: "Ödeme basari bildirimi".</summary>
    public const string PaymentSucceeded = "PaymentSucceeded";

    /// <summary>PDF: "Rezervasyon süresi doldu bildirimi".</summary>
    public const string ReservationExpired = "ReservationExpired";

    /// <summary>PDF: "Etkinlik iptal bildirimi".</summary>
    public const string EventCancelled = "EventCancelled";

    /// <summary>PDF Sprint 9 Background Job: "Yaklasan etkinlik hatirlatmasi".</summary>
    public const string EventReminder = "EventReminder";

    /// <summary>PDF: "Rapor hazirlama".</summary>
    public const string DailySalesSummary = "DailySalesSummary";

    /// <summary>
    /// PDF Sprint 13: "Rapor üretimi background job olarak
    /// calistirilmali ve tamamlandiginda kullanıcıya bildirim
    /// gonderilmelidir."
    /// </summary>
    public const string ReportExport = "ReportExport";

    /// <summary>PDF Sprint 14 e-posta sablonu: "Rezervasyon oluşturuldu".</summary>
    public const string ReservationCreated = "ReservationCreated";
}

// PAYLOAD TIPLERI
//
// Payload veritabaninda JSON metni olarak duruyor. Bu record'lar o
// metnin SEMASI.
//
// Neden anonim nesne yerine record?
//   - Yazan taraf (komut) ile okuyan taraf (isleyici) AYNI tipi
//     kullaniyor. Alan adını birinde degistirip digerinde unutmak
//     derleyici hatası veriyor.
//   - Anonim nesneyle yazip elle okusaydim, uyusmazlik ancak
//     CALISMA ZAMANINDA -- hem de arka planda, kimse bakmazken --
//     ortaya çıkardı.
//
// DIKKAT: Bu tiplere alan EKLEMEK guvenlidir (eski kayitlarda alan
// olmaz, varsayılan deger gelir). Alan SILMEK veya YENIDEN ADLANDIRMAK
// tablodaki islenmemis eski mesajlari bozar.

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
