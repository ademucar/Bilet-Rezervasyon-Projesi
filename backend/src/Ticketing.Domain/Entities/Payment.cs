using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Odeme kaydi. PDF Sprint 8.
///
/// Gercek bir odeme saglayicisi kullanmiyoruz ama entegrasyona BENZER
/// bir yapi kuruyoruz: IPaymentService arayuzu + MockPaymentProvider.
/// Bu entity, hangi saglayici kullanilirsa kullanilsin ayni kalir.
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
        ]

        // Failed, Cancelled, Refunded son durumlar.
        //
        // Failed'dan Processing'e donus YOK. Bu kasitli: basarisiz bir
        // odemeyi "yeniden denemek" ayni kaydi diriltmek degil, YENI bir
        // Payment kaydi olusturmak demektir. Boylece her deneme ayri
        // kayitli kalir ve denetim izi bozulmaz.
    };

    public Guid ReservationId { get; private set; }

    /// <summary>Ornek: "MockPaymentProvider", "Iyzico", "Stripe".</summary>
    public string ProviderName { get; private set; }

    /// <summary>
    /// Saglayicinin bize verdigi islem referansi.
    /// Mutabakat (reconciliation) ve destek talepleri icin sart:
    /// saglayiciya "su islemde ne oldu" diye sorarken bu numarayi veririz.
    /// </summary>
    public string? ProviderReference { get; private set; }

    public PaymentStatus Status { get; private set; }

    public Money Amount { get; private set; }

    /// <summary>
    /// Simdiye kadar iade edilen toplam tutar.
    ///
    /// Neden bir bool degil de TUTAR? Cunku kismi iade var
    /// (bkz. CancellationPolicy: %50 iade). Bir rezervasyondaki 4 biletten
    /// 2'si iade edilirse tutarin yarisi geri doner. bool ile bunu
    /// modelleyemezdik.
    /// </summary>
    public Money RefundedAmount { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>PDF Sprint 15: odeme baslatma idempotent olmalidir.</summary>
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
            throw new DomainException("Odeme saglayicisi belirtilmelidir.", "payment.provider_required");
        }

        if (amount.Amount <= 0)
        {
            throw new DomainException("Odeme tutari sifirdan buyuk olmalidir.", "payment.invalid_amount");
        }

        return new Payment
        {
            ReservationId = reservationId,
            Amount = amount,
            RefundedAmount = Money.Zero(amount.Currency),
            ProviderName = providerName,
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey
        };
    }

    private void TransitionTo(PaymentStatus target)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !Array.Exists(allowed, s => s == target))
        {
            throw new DomainException(
                $"Odeme {Status} durumundan {target} durumuna gecemez.",
                "payment.invalid_transition");
        }

        Status = target;
    }

    /// <summary>
    /// Saglayicinin verdigi islem referansini kaydeder.
    ///
    /// Ayri bir metot cunku referans, StartProcessing'den SONRA
    /// (saglayici cagrisi donunce) belli oluyor. StartProcessing'e
    /// parametre olarak vermek, cagri sirasini yanlis anlasilir
    /// kilardi.
    /// </summary>
    public void SetProviderReference(string? providerReference)
    {
        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            ProviderReference = providerReference;
        }
    }

    /// <summary>Saglayiciya istek gonderildi, cevap bekleniyor.</summary>
    public void StartProcessing(string? providerReference = null)
    {
        TransitionTo(PaymentStatus.Processing);
        ProviderReference = providerReference;

        _transactions.Add(PaymentTransaction.Create(
            Id, PaymentTransactionType.Charge, PaymentStatus.Processing, providerReference));
    }

    /// <summary>
    /// Odeme basarili.
    ///
    /// ------------------------------------------------------------------
    /// IDEMPOTENCY -- PDF: "Callback islemleri idempotent olmalidir."
    /// ------------------------------------------------------------------
    /// Odeme saglayicilari callback'i BIRDEN FAZLA KEZ gonderebilir.
    /// Bu bir hata degil, normal davranistir: saglayici cevap alamadigini
    /// dusunurse tekrar dener.
    ///
    /// Idempotent olmasaydi ayni rezervasyon icin iki kez bilet uretilirdi.
    /// Bu yuzden zaten Successful ise sessizce donuyorum -- hata firlatmiyorum.
    ///
    /// Hata firlatsaydim saglayici "callback basarisiz" deyip tekrar
    /// tekrar denerdi ve sonsuz dongu olusurdu.
    /// </summary>
    public bool Complete(string? providerReference, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Successful)
        {
            // Zaten tamamlanmis. Ikinci callback'i sessizce yok say.
            // false donuyorum ki cagiran taraf "yeni bir sey olmadi,
            // bilet uretme" diye anlasin.
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
    /// Iade islemi. Kismi iade destekler.
    /// </summary>
    public void Refund(Money refundAmount, string? providerReference = null)
    {
        if (Status != PaymentStatus.Successful && Status != PaymentStatus.Refunded)
        {
            throw new DomainException(
                "Yalnizca basarili odeme iade edilebilir.",
                "payment.not_refundable");
        }

        var yeniToplam = RefundedAmount + refundAmount;

        // Odenenden fazlasini iade etmek imkansiz olmali.
        //
        // Bu kontrol olmasaydi, tekrarlanan bir iade istegi (ornegin
        // callback iki kez gelirse) kullaniciya iki kat para gonderirdi.
        // Money zaten negatif tutari engelliyor ama bu farkli bir kural:
        // "toplam iade, odemeyi asamaz".
        if (yeniToplam > Amount)
        {
            throw new DomainException(
                $"Iade tutari odenen tutari asamaz. Odenen: {Amount}, " +
                $"toplam iade girisimi: {yeniToplam}",
                "payment.refund_exceeds_amount");
        }

        RefundedAmount = yeniToplam;

        _transactions.Add(PaymentTransaction.Create(
            Id, PaymentTransactionType.Refund, PaymentStatus.Refunded, providerReference));

        // Tam iade yapildiysa durumu da guncelle.
        // Kismi iadede Successful kaliyor -- cunku odeme hala gecerli,
        // sadece bir kismi geri donmus.
        if (RefundedAmount.Amount == Amount.Amount && Status == PaymentStatus.Successful)
        {
            TransitionTo(PaymentStatus.Refunded);
        }
    }

    /// <summary>Iade edilebilecek kalan tutar.</summary>
    public Money GetRefundableAmount() => Amount - RefundedAmount;
}
