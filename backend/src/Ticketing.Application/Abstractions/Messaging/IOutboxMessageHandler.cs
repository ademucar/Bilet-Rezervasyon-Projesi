namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>
/// Bir Outbox mesaj turunu isleyen bileşen. PDF Sprint 9.
///
/// ==================================================================
/// NEDEN AYRI BIR ARAYUZ? Neden processor'in içinde dev bir switch değil?
/// ==================================================================
/// En kolay yol soyle olurdu:
///
///     switch (mesaj.Type)
///     {
///         case "TicketsIssued":       ... 40 satır ...
///         case "ReservationExpired":  ... 30 satır ...
///         case "EventCancelled":      ... 50 satır ...
///     }
///
/// PDF Sprint 9 alti farklı senaryo sayiyor ve ilerideki sprintlerde
/// daha da artacak. O switch birkaç yuz satirlik, test edilemez bir
/// blok haline gelirdi: tek bir senaryoyu test etmek için processor'in
/// tamamini ayaga kaldirmak gerekirdi.
///
/// Ayrı arayüz ile her senaryo kendi sinifinda, kendi bagimliliklariyla
/// ve tek başına test edilebilir. Processor ise hiçbir senaryoyu
/// tanimadan çalışır -- yeni bir mesaj türü eklemek için processor'a
/// DOKUNULMAZ.
/// ==================================================================
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>
    /// Bu isleyicinin ilgilendigi mesaj türü.
    /// OutboxMessageTypes sabitlerinden biri olmalıdır.
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// Mesaji isler.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// ISLEYICILER IDEMPOTENT OLMAK ZORUNDA
    /// ==============================================================
    /// Outbox "en az bir kez teslim" (at-least-önce) garantisi verir,
    /// "tam olarak bir kez" (exactly-önce) DEĞİL.
    ///
    /// Somut senaryo: isleyici e-postayi gonderdi, tam o anda sunucu
    /// coktu ve ProcessedAt yazilamadi. Sistem ayaga kalkinca mesaj
    /// hâlâ islenmemis görünür ve tekrar denenir.
    ///
    /// Exactly-önce, dagitik sistemlerde e-posta gibi DIS servislerle
    /// teorik olarak imkansizdir (mesaji gonderdikten sonra "gonderdim"
    /// kaydini yazmak ayrı bir islemdir; ikisi atomik olamaz).
    ///
    /// Bu yüzden çözüm tarafi degistiriyoruz: mesaji iki kez islemek
    /// ZARARSIZ olmalı. Ornegin bildirim yazmadan önce "bu bildirim
    /// zaten var mi?" diye bakariz.
    ///
    /// Hata firlatilirsa mesaj başarısız sayilir, RetryCount artar ve
    /// ustel geri cekilme ile yeniden denenir.
    /// ==============================================================
    /// </remarks>
    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
