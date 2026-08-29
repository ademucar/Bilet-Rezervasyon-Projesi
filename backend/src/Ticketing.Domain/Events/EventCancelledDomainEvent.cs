using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Bir etkinlik iptal edildiginde firlatilir.
///
/// PDF Sprint 1, soru 9: Etkinlik iptal edildiginde aktif rezervasyonlar
/// iptal edilmeli, satılmış biletler iade edilmeli, koltuklar serbest
/// birakilmali, etkilenen kullanicilara bildirim gitmeli.
///
/// Bu islerin HEPSINI Event.Cancel() metodunun içinde yapmak yanlış olurdu:
/// Event sinifinin rezervasyonları, ödemeleri ve e-posta servisini bilmesi
/// gerekirdi. O zaman Domain katmani altyapiya bagimli hale gelirdi ve
/// architecture testimiz kırmızı yanardi.
///
/// Bunun yerine Event sadece "iptal edildim" diyor; ne yapilacagina
/// Application katmanindaki handler'lar karar veriyor.
/// </summary>
public sealed record EventCancelledDomainEvent(
    Guid EventId,
    Guid OrganizerId,
    string Title,
    string? Reason,
    DateTimeOffset OccurredOn) : IDomainEvent;
