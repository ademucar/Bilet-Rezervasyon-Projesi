using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Odeme uzerindeki her denemenin kaydi. PDF'in ER diyagramindaki
/// PaymentTransactions tablosu.
///
/// ------------------------------------------------------------------
/// NEDEN AYRI BIR TABLO? Payment'a birkac sutun eklesek olmaz miydi?
/// ------------------------------------------------------------------
/// Olmazdi. Cunku bir odemede BIRDEN FAZLA islem olabilir:
///   1. Tahsilat denemesi -> basarisiz (kart limiti)
///   2. Tahsilat denemesi -> basarili
///   3. Kismi iade (2 bilet)
///   4. Kalan iade
///
/// Payment'ta tek bir "Status" ve tek bir "ProviderReference" var; bu
/// dort adimi orada tutamayiz. Her adimin kendi zaman damgasi, kendi
/// saglayici referansi ve kendi hata mesaji olmali.
///
/// Bu tablo bir DENETIM IZIDIR (audit trail): "bu odemede ne oldu?"
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
