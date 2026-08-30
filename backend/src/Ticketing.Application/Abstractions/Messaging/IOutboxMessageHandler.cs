namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>
/// Bir Outbox mesaj turunu isleyen bileşen. PDF Sprint 9.
///
/// Once processor'in icine buyuk bir switch yazmayi dusundum:
///
///     switch (mesaj.Type)
///     {
///         case "TicketsIssued":       ... 40 satır ...
///         case "ReservationExpired":  ... 30 satır ...
///         case "EventCancelled":      ... 50 satır ...
///     }
///
/// Vazgectim. PDF Sprint 9 alti senaryo sayiyor ve sonraki
/// sprintlerde artiyor; o switch birkac yuz satirlik, test edilemez
/// bir bloga donusurdu. Tek bir senaryoyu test etmek icin
/// processor'in tamamini ayaga kaldirmak gerekirdi.
///
/// Ayrı arayüz ile her senaryo kendi sinifinda, kendi bagimliliklariyla
/// ve tek başına test edilebilir. Processor ise hiçbir senaryoyu
/// tanimadan çalışır -- yeni bir mesaj türü eklemek için processor'a
/// Dokunulmaz.
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
    /// Isleyicilerin idempotent olmasi sart, cunku Outbox
    /// "en az bir kez teslim" (at-least-once) garantisi veriyor,
    /// "tam olarak bir kez" (exactly-once) degil.
    ///
    /// Somut senaryo: isleyici e-postayi gonderdi, tam o anda sunucu
    /// coktu ve ProcessedAt yazilamadi. Sistem ayaga kalkinca mesaj
    /// hâlâ islenmemis görünür ve tekrar denenir.
    ///
    /// Exactly-once, dagitik sistemlerde e-posta gibi DIS servislerle
    /// teorik olarak imkansizdir (mesaji gonderdikten sonra "gonderdim"
    /// kaydini yazmak ayrı bir islemdir; ikisi atomik olamaz).
    ///
    /// Bu yüzden çözüm tarafi degistiriyorum: mesaji iki kez islemek
    /// ZARARSIZ olmalı. Ornegin bildirim yazmadan önce "bu bildirim
    /// zaten var mi?" diye bakariz.
    ///
    /// Hata firlatilirsa mesaj başarısız sayilir, RetryCount artar ve
    /// ustel geri cekilme ile yeniden denenir.
    /// </remarks>
    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
