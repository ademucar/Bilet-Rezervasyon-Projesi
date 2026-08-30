using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Ödeme kaydı. PDF Sprint 8.
///
/// Gerçek bir ödeme sağlayıcısı kullanmiyorum ama entegrasyona BENZER
/// bir yapi kuruyorum: IPaymentService arayuzu + MockPaymentProvider.
/// Bu entity, hangi sağlayıcı kullanilirsa kullanilsin aynı kalır.
/// </summary>
public class Payment : ConcurrentEntity
{
    private Payment()
    {
        ProviderName = string.Empty;
        Amount = Money.Zero("TRY");
        RefundedAmount = Money.Zero("TRY");
    }

    private static readonly Dictionary<PaymentStatus, PaymentStatus[]> AllowedTransitions = new()
    {
        [PaymentStatus.Pending] =
        [
            PaymentStatus.Processing,
            PaymentStatus.Cancelled
        ],
        [PaymentStatus.Processing] =
        [
            PaymentStatus.Successful,
            PaymentStatus.Failed,
            PaymentStatus.Cancelled
        ],
        [PaymentStatus.Successful] =
        [
            PaymentStatus.Refunded
        ],

        // Failed, Cancelled, Refunded son durumlar.
        //
        // Failed'dan Processing'e donus YOK. Bu kasitli: başarısız bir
        // ödemeyi "yeniden denemek" aynı kaydı diriltmek değil, YENI bir
        // Payment kaydı olusturmak demektir. Boylece her deneme ayrı
        // kayıtlı kalır ve denetim izi bozulmaz.
    };

    public Guid ReservationId { get; private set; }

    /// <summary>Ornek: "MockPaymentProvider", "Iyzico", "Stripe".</summary>
    public string ProviderName { get; private set; }

    /// <summary>
    /// Saglayicinin bana verdiği işlem referansı.
    /// Mutabakat (reconciliation) ve destek talepleri için sart:
    /// saglayiciya "su islemde ne oldu" diye sorarken bu numarayi veririz.
    /// </summary>
    public string? ProviderReference { get; private set; }

    public PaymentStatus Status { get; private set; }

    public Money Amount { get; private set; }

    /// <summary>
    /// Simdiye kadar iade edilen toplam tutar.
    ///
    /// Neden bir bool değil de TUTAR? Çünkü kismi iade var
    /// (bkz. CancellationPolicy: %50 iade). Bir rezervasyondaki 4 biletten
    /// 2'si iade edilirse tutarin yarisi geri döner. bool ile bunu
    /// modelleyemezdim.
    /// </summary>
    public Money RefundedAmount { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>PDF Sprint 15: ödeme baslatma idempotent olmalıdır.</summary>
    public string? IdempotencyKey { get; private set; }

    public Reservation Reservation { get; private set; } = null!;

    private readonly List<PaymentTransaction> _transactions = [];

    public IReadOnlyCollection<PaymentTransaction> Transactions => _transactions.AsReadOnly();

    public static Payment Create(
        Guid reservationId,
        Money amount,
        string providerName,
        string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new DomainException("Ödeme sağlayıcısı belirtilmelidir.", "payment.provider_required");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainException("Ödeme tutari sıfırdan büyük olmalıdır.", "payment.invalid_amount");
        }

        return new Payment
        {
            ReservationId = reservationId,
            Amount = amount,
            RefundedAmount = Money.Zero(amount.Currency),
            ProviderName = providerName,
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey,
        };
    }

    private void TransitionTo(PaymentStatus target)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !Array.Exists(allowed, s => s == target))
        {
            throw new DomainException(
                $"Ödeme {Status} durumundan {target} durumuna gecemez.",
                "payment.invalid_transition");
        }

        Status = target;
    }

    /// <summary>
    /// Saglayicinin verdiği işlem referansini kaydeder.
    ///
    /// Ayrı bir metot çünkü referans, StartProcessing'den SONRA
    /// (sağlayıcı cagrisi donunce) belli oluyor. StartProcessing'e
    /// parametre olarak vermek, cagri sırasını yanlış anlasilir
    /// kilardi.
    /// </summary>
    public void SetProviderReference(string? providerReference)
    {
        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            ProviderReference = providerReference;
        }
    }

    /// <summary>Saglayiciya istek gönderildi, cevap bekleniyor.</summary>
    public void StartProcessing(string? providerReference = null)
    {
        TransitionTo(PaymentStatus.Processing);
        ProviderReference = providerReference;

        _transactions.Add(PaymentTransaction.Create(
            Id, PaymentTransactionType.Charge, PaymentStatus.Processing, providerReference));
    }

    /// <summary>
    /// Ödeme başarılı.
    ///
    /// IDEMPOTENCY -- PDF: "Callback islemleri idempotent olmalıdır."
    ///
    /// Ödeme saglayicilari callback'i birden fazla kez gonderebilir.
    /// Bu bir hata değil, normal davranistir: sağlayıcı cevap alamadigini
    /// dusunurse tekrar dener.
    ///
    /// Idempotent olmasaydı aynı rezervasyon için iki kez bilet uretilirdi.
    /// Bu yüzden zaten Successful ise sessizce donuyorum -- hata firlatmiyorum.
    ///
    /// Hata firlatsaydim sağlayıcı "callback başarısız" deyip tekrar
    /// tekrar denerdi ve sonsuz dongu olusurdu.
    /// </summary>
    public bool Complete(string? providerReference, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Successful)
        {
            // Zaten tamamlanmis. Ikinci callback'i sessizce yok say.
            // false donuyorum ki cagiran taraf "yeni bir sey olmadi,
            // bilet üretme" diye anlasin.
            return false;
        }

        TransitionTo(PaymentStatus.Successful);

        ProviderReference = providerReference ?? ProviderReference;
        CompletedAt = now;

        _transactions.Add(PaymentTransaction.Create(
            Id, PaymentTransactionType.Charge, PaymentStatus.Successful, providerReference));

        return true;
    }

    public void Fail(string? reason, DateTimeOffset now, Guid userId)
    {
        if (Status == PaymentStatus.Failed)
        {
            return;   // idempotent
        }

        TransitionTo(PaymentStatus.Failed);

        FailureReason = reason;
        CompletedAt = now;

        _transactions.Add(PaymentTransaction.Create(
            Id, PaymentTransactionType.Charge, PaymentStatus.Failed, ProviderReference, reason));

        Raise(new PaymentFailedDomainEvent(Id, ReservationId, userId, reason, now));
    }

    public void Cancel() => TransitionTo(PaymentStatus.Cancelled);

    /// <summary>
    /// İade islemi. Kismi iade destekler.
    /// </summary>
    public void Refund(Money refundAmount, string? providerReference = null)
    {
        if (Status != PaymentStatus.Successful && Status != PaymentStatus.Refunded)
        {
            throw new DomainException(
                "Yalnızca başarılı ödeme iade edilebilir.",
                "payment.not_refundable");
        }

        var yeniToplam = RefundedAmount + refundAmount;

        // Odenenden fazlasini iade etmek imkansiz olmalı.
        //
        // Bu kontrol olmasaydı, tekrarlanan bir iade isteği (örneğin
        // callback iki kez gelirse) kullanıcıya iki kat para gonderirdi.
        // Money zaten negatif tutarı engelliyor ama bu farklı bir kural:
        // "toplam iade, ödemeyi aşamaz".
        if (yeniToplam > Amount)
        {
            throw new DomainException(
                $"İade tutari odenen tutari aşamaz. Odenen: {Amount}, " +
                $"toplam iade girisimi: {yeniToplam}",
                "payment.refund_exceeds_amount");
        }

        RefundedAmount = yeniToplam;

        _transactions.Add(PaymentTransaction.Create(
            Id, PaymentTransactionType.Refund, PaymentStatus.Refunded, providerReference));

        // Tam iade yapildiysa durumu da güncelle.
        // Kismi iadede Successful kaliyor -- çünkü ödeme hâlâ geçerli,
        // sadece bir kismi geri donmus.
        if (RefundedAmount.Amount == Amount.Amount && Status == PaymentStatus.Successful)
        {
            TransitionTo(PaymentStatus.Refunded);
        }
    }

    /// <summary>İade edilebilecek kalan tutar.</summary>
    public Money GetRefundableAmount() => Amount - RefundedAmount;
}
