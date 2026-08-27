namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>
/// Bir Outbox mesaj turunu isleyen bilesen. PDF Sprint 9.
///
/// ==================================================================
/// NEDEN AYRI BIR ARAYUZ? Neden processor'in icinde dev bir switch degil?
/// ==================================================================
/// En kolay yol soyle olurdu:
///
///     switch (mesaj.Type)
///     {
///         case "TicketsIssued":       ... 40 satir ...
///         case "ReservationExpired":  ... 30 satir ...
///         case "EventCancelled":      ... 50 satir ...
///     }
///
/// PDF Sprint 9 alti farkli senaryo sayiyor ve ilerideki sprintlerde
/// daha da artacak. O switch birkac yuz satirlik, test edilemez bir
/// blok haline gelirdi: tek bir senaryoyu test etmek icin processor'in
/// tamamini ayaga kaldirmak gerekirdi.
///
/// Ayri arayuz ile her senaryo kendi sinifinda, kendi bagimliliklariyla
/// ve tek basina test edilebilir. Processor ise hicbir senaryoyu
/// tanimadan calisir -- yeni bir mesaj turu eklemek icin processor'a
/// DOKUNULMAZ.
/// ==================================================================
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>
    /// Bu isleyicinin ilgilendigi mesaj turu.
    /// OutboxMessageTypes sabitlerinden biri olmalidir.
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// Mesaji isler.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// ISLEYICILER IDEMPOTENT OLMAK ZORUNDA
    /// ==============================================================
    /// Outbox "en az bir kez teslim" (at-least-once) garantisi verir,
    /// "tam olarak bir kez" (exactly-once) DEGIL.
    ///
    /// Somut senaryo: isleyici e-postayi gonderdi, tam o anda sunucu
    /// coktu ve ProcessedAt yazilamadi. Sistem ayaga kalkinca mesaj
    /// hala islenmemis gorunur ve tekrar denenir.
    ///
    /// Exactly-once, dagitik sistemlerde e-posta gibi DIS servislerle
    /// teorik olarak imkansizdir (mesaji gonderdikten sonra "gonderdim"
    /// kaydini yazmak ayri bir islemdir; ikisi atomik olamaz).
    ///
    /// Bu yuzden cozum tarafi degistiriyoruz: mesaji iki kez islemek
    /// ZARARSIZ olmali. Ornegin bildirim yazmadan once "bu bildirim
    /// zaten var mi?" diye bakariz.
    ///
    /// Hata firlatilirsa mesaj basarisiz sayilir, RetryCount artar ve
    /// ustel geri cekilme ile yeniden denenir.
    /// ==============================================================
    /// </remarks>
    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
