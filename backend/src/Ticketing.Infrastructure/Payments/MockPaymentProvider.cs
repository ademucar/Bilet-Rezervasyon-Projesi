using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Time;

namespace Ticketing.Infrastructure.Payments;

/// <summary>
/// Her zaman BASARILI donen simulasyon saglayicisi. PDF Sprint 8.
///
/// Gercek bir saglayiciyla ayni ARAYUZU uyguluyor; yalnizca ic
/// islemleri taklit. Boylece gercek saglayiciya gecerken Application
/// katmaninda tek satir degismeyecek.
/// </summary>
internal sealed class MockPaymentProvider : IPaymentService
{
    public string ProviderName => "MockPaymentProvider";

    /// <summary>
    /// Uretilen islem referanslarini tutar.
    ///
    /// ==============================================================
    /// NEDEN BELLEKTE BIR SOZLUK?
    /// ==============================================================
    /// VerifyPaymentAsync'in ANLAMLI olmasi icin. Sozluk olmasaydi
    /// "her referansi dogrula" derdik ve dogrulama adimi hicbir sey
    /// test etmezdi -- uydurma bir referans bile gecerdi.
    ///
    /// Boylece gercek davranisi taklit ediyoruz: yalnizca BIZIM
    /// urettigimiz referanslar dogrulanabiliyor. Sahte callback
    /// senaryosunu test edebiliyoruz.
    ///
    /// ConcurrentDictionary: bu servis SINGLETON ve es zamanli
    /// isteklerden erisilecek. Duz Dictionary kullansaydik es zamanli
    /// yazmada bozulabilir ve sonsuz donguye bile girebilirdi.
    ///
    /// NOT: Bu yalnizca simulasyon icin. Uretimde saglayici bu bilgiyi
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
        // Gercek hayattaki karsiligi: saldirgan callback adresimize
        // uydurma bir referansla "odeme basarili" istegi gonderdi.
        // Saglayiciya sorunca "boyle bir islem yok" cevabi geliyor.
        if (!_issuedReferences.ContainsKey(providerReference))
        {
            return Task.FromResult(PaymentResult.Failure(
                "payment.reference_not_found",
                "Odeme referansi saglayicida bulunamadi."));
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
                "Odeme referansi bulunamadi."));
        }

        // Saglayici da odenenden fazlasini iade etmeyi reddeder.
        //
        // Payment entity'sinde de ayni kural var. Iki yerde olmasi
        // tekrar degil: biri BIZIM tarafimizin butunlugunu korur,
        // digeri saglayicinin davranisini taklit eder. Gercek
        // hayatta ikisi ayri sistemlerdir ve ikisi de kontrol eder.
        if (amount > originalAmount)
        {
            return Task.FromResult(PaymentResult.Failure(
                "payment.refund_exceeds_amount",
                "Iade tutari odenen tutari asamaz."));
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
    /// Islem referansi uretir. Ornek: MOCK-20260827-A7B3C9D2
    ///
    /// RandomNumberGenerator kullaniyorum, Random degil.
    /// Simulasyon bile olsa aliskanligi dogru kurmak onemli: gercek
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
/// Her zaman BASARISIZ donen saglayici. PDF Sprint 8.
///
/// Ne ise yarar? "Odeme basarisiz olursa ne olur?" akisini test
/// etmek icin. Gercek bir karti reddettirmek zor ve guvenilmezdir;
/// bu saglayici o senaryoyu deterministik hale getiriyor.
///
/// Yapilandirmadan secilebiliyor (Payment:Provider = "Failed"),
/// boylece gelistirme ortaminda basarisiz odeme akisi kod
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
            "Odeme reddedildi. Kart limitiniz yetersiz olabilir."));

    public Task<PaymentResult> VerifyPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Failure(
            "payment.declined",
            "Odeme dogrulanamadi."));

    public Task<PaymentResult> RefundPaymentAsync(
        string providerReference,
        decimal amount,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Failure(
            "payment.refund_failed",
            "Iade islemi gerceklestirilemedi."));

    public Task<PaymentResult> CancelPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PaymentResult.Success(providerReference));
}
