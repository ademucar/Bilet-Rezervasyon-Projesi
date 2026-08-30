using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Bir etkinlik yayina alindiginda firlatilir.
///
/// Bunu dinleyecekler (Sprint 9+):
///   - Etkinligi favorileyen kullanicilara bildirim gonderen handler
///   - Arama index'ini guncelleyen handler
///   - Etkinlik listesi cache'ini temizleyen handler
///
/// Event sinifi bunlarin HICBIRINI bilmiyor. Sadece "ben yayinlandim" diyor.
/// Yarin SMS bildirimi eklersek Event sinifina dokunmayacagiz.
/// </summary>
public sealed record EventPublishedDomainEvent(
    Guid EventId,
    Guid OrganizerId,
    string Title,
    DateTimeOffset OccurredOn) : IDomainEvent;
