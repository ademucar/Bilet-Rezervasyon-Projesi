using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Time;

namespace Ticketing.Infrastructure.Payments;

/// <summary>
/// Her zaman BASARILI donen simülasyon sağlayıcısı. PDF Sprint 8.
///
/// Gerçek bir saglayiciyla aynı ARAYUZU uyguluyor; yalnızca ic
/// islemleri taklit. Boylece gerçek saglayiciya gecerken Application
/// katmaninda tek satır degismeyecek.
/// </summary>
internal sealed class MockPaymentProvider : IPaymentService
{
    public string ProviderName => "MockPaymentProvider";

    /// <summary>
    /// Uretilen işlem referanslarini tutar.
    ///
    /// ==============================================================
    /// NEDEN BELLEKTE BIR SOZLUK?
    /// ==============================================================
    /// VerifyPaymentAsync'in ANLAMLI olmasını için. Sozluk olmasaydı
    /// "her referansı dogrula" derdik ve doğrulama adimi hiçbir sey
    /// test etmezdi -- uydurma bir referans bile gecerdi.
    ///
    /// Boylece gerçek davranisi taklit ediyoruz: yalnızca BIZIM
    /// urettigimiz referanslar dogrulanabiliyor. Sahte callback
    /// senaryosunu test edebiliyoruz.
    ///
    /// ConcurrentDictionary: bu servis SINGLETON ve es zamanlı
    /// isteklerden erisilecek. Duz Dictionary kullansaydık es zamanlı
    /// yazmada bozulabilir ve sonsuz donguye bile girebilirdi.
    ///
    /// NOT: Bu yalnızca simülasyon için. Uretimde sağlayıcı bu bilgiyi
    /// kendi sisteminde tutar.
    /// </summary>
    private readonly ConcurrentDictionary<string, decimal> _issuedReferences =
        new(StringComparer.Ordinal);

    private readonly IDateTimeProvider _clock;

    public MockPaymentProvider(IDateTimeProvider clock) => _clock = clock;

    public Task<PaymentResult> CreatePaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reference = GenerateReference();
        _issuedReferences[reference] = request.Amount;

        return Task.FromResult(PaymentResult.Success(reference));
    }

    public Task<PaymentResult> VerifyPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        // Bizim uretmedigimiz bir referans -> DOGRULAMA BASARISIZ.
        //
        // Gerçek hayattaki karşılığı: saldirgan callback adresimize
        // uydurma bir referansla "ödeme başarılı" isteği gonderdi.
        // Saglayiciya sorunca "boyle bir işlem yok" cevabi geliyor.
        if (!_issuedReferences.ContainsKey(providerReference))
        {
            return Task.FromResult(PaymentResult.Failure(
                "payment.reference_not_found",
                "Ödeme referansı saglayicida bulunamadı."));
        }

        return Task.FromResult(PaymentResult.Success(providerReference));
    }

    public Task<PaymentResult> RefundPaymentAsync(
        string providerReference,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (!_issuedReferences.TryGetValue(providerReference, out var originalAmount))
        {
            return Task.FromResult(PaymentResult.Failure(
                "payment.reference_not_found",
                "Ödeme referansı bulunamadı."));
        }

        // Sağlayıcı da odenenden fazlasini iade etmeyi reddeder.
        //
        // Payment entity'sinde de aynı kural var. Iki yerde olmasını
        // tekrar değil: biri BIZIM tarafimizin butunlugunu korur,
        // digeri sağlayıcının davranisini taklit eder. Gerçek
        // hayatta ikisi ayrı sistemlerdir ve ikisi de kontrol eder.
        if (amount > originalAmount)
        {
            return Task.FromResult(PaymentResult.Failure(
                "payment.refund_exceeds_amount",
                "İade tutari odenen tutari aşamaz."));
        }

        return Task.FromResult(PaymentResult.Success("REF-" + GenerateReference()));
    }

    public Task<PaymentResult> CancelPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        _issuedReferences.TryRemove(providerReference, out _);

        return Task.FromResult(PaymentResult.Success(providerReference));
    }

    /// <summary>
    /// İşlem referansı üretir. Ornek: MOCK-20260827-A7B3C9D2
    ///
    /// RandomNumberGenerator kullanıyorum, Random değil.
    /// Simulasyon bile olsa aliskanligi doğru kurmak önemli: gerçek
    /// saglayicida tahmin edilebilir referanslar, saldirganin
    /// baskasinin islemini sorgulamasina zemin hazirlar.
    /// </summary>
    private string GenerateReference()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        var date = _clock.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        return string.Concat("MOCK-", date, "-", Convert.ToHexString(bytes));
    }
}

/// <summary>
/// Her zaman BASARISIZ donen sağlayıcı. PDF Sprint 8.
///
/// Ne ise yarar? "Ödeme başarısız olursa ne olur?" akisini test
/// etmek için. Gerçek bir karti reddettirmek zor ve guvenilmezdir;
/// bu sağlayıcı o senaryoyu deterministik hale getiriyor.
///
/// Yapilandirmadan secilebiliyor (Payment:Provider = "Failed"),
/// boylece gelistirme ortaminda başarısız ödeme akışı kod
/// degistirmeden denenebiliyor.
/// </summary>
internal sealed class FailedPaymentProvider : IPaymentService
{
    public string ProviderName => "FailedPaymentProvider";

    public Task<PaymentResult> CreatePaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Failure(
            "payment.declined",
            "Ödeme reddedildi. Kart limitiniz yetersiz olabilir."));

    public Task<PaymentResult> VerifyPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Failure(
            "payment.declined",
            "Ödeme dogrulanamadi."));

    public Task<PaymentResult> RefundPaymentAsync(
        string providerReference,
        decimal amount,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Failure(
            "payment.refund_failed",
            "İade islemi gerceklestirilemedi."));

    public Task<PaymentResult> CancelPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Success(providerReference));
}
