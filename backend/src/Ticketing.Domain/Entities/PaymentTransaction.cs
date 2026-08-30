using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Ödeme uzerindeki her denemenin kaydı. PDF'in ER diyagramindaki
/// PaymentTransactions tablosu.
///
/// Neden ayri bir tablo? Payment'a birkaç sutun eklesek olmaz miydi?
///
/// Olmazdi. Çünkü bir odemede birden fazla işlem olabilir:
///   1. Tahsilat denemesi -> başarısız (kart limiti)
///   2. Tahsilat denemesi -> başarılı
///   3. Kismi iade (2 bilet)
///   4. Kalan iade
///
/// Payment'ta tek bir "Status" ve tek bir "ProviderReference" var; bu
/// dort adimi orada tutamam. Her adimin kendi zaman damgasi, kendi
/// sağlayıcı referansı ve kendi hata mesaji olmalı.
///
/// Bu tablo bir denetim izidir (audit trail): "bu odemede ne oldu?"
/// sorusunun cevabi burada. Kayitlar asla silinmez, asla guncellenmez;
/// sadece eklenir (append-only).
/// </summary>
public class PaymentTransaction : Entity
{
    private PaymentTransaction()
    {
    }

    public Guid PaymentId { get; private set; }

    public PaymentTransactionType Type { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? ProviderReference { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Payment Payment { get; private set; } = null!;

    internal static PaymentTransaction Create(
        Guid paymentId,
        PaymentTransactionType type,
        PaymentStatus status,
        string? providerReference = null,
        string? message = null)
        => new()
        {
            PaymentId = paymentId,
            Type = type,
            Status = status,
            ProviderReference = providerReference,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
