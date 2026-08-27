namespace Ticketing.Application.Abstractions.Payments;

/// <summary>
/// Odeme saglayicisina gonderilen istek.
/// </summary>
/// <param name="PaymentId">Bizim tarafimizdaki odeme kaydinin kimligi.</param>
/// <param name="Amount">Tutar.</param>
/// <param name="Currency">ISO 4217 para birimi.</param>
/// <param name="Description">Odeme aciklamasi (ekstre satirinda gorunur).</param>
public sealed record PaymentRequest(
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string Description);

/// <summary>
/// Saglayicidan donen sonuc.
/// </summary>
/// <param name="IsSuccess">Islem basarili mi?</param>
/// <param name="ProviderReference">
/// Saglayicinin verdigi islem numarasi.
///
/// Bu alan MUTABAKAT (reconciliation) icin kritik: ay sonunda
/// saglayicidan gelen ekstreyi kendi kayitlarimizla eslestirirken
/// bu numarayi kullaniyoruz. Ayrica destek talebinde "su islemde
/// ne oldu" diye sorarken saglayiciya bu numarayi veriyoruz.
/// </param>
/// <param name="ErrorCode">Basarisizsa saglayicinin hata kodu.</param>
/// <param name="ErrorMessage">Kullaniciya gosterilebilir hata mesaji.</param>
public sealed record PaymentResult(
    bool IsSuccess,
    string? ProviderReference,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PaymentResult Success(string providerReference)
        => new(true, providerReference, null, null);

    public static PaymentResult Failure(string errorCode, string errorMessage)
        => new(false, null, errorCode, errorMessage);
}

/// <summary>
/// ==================================================================
/// ODEME SAGLAYICI SOYUTLAMASI
/// ==================================================================
/// PDF Sprint 8:
///   "Gercek odeme saglayicisi kullanilmak zorunda degildir. Ancak
///    odeme saglayicisi entegrasyonuna BENZER bir yapi kurulmalidir."
///
/// Istenen metotlar: CreatePayment, VerifyPayment, RefundPayment,
/// CancelPayment.
///
/// ------------------------------------------------------------------
/// NEDEN SOYUTLAMA? Dogrudan Iyzico cagirsak olmaz miydi?
/// ------------------------------------------------------------------
/// Uc sebep:
///
/// 1) TEST EDILEBILIRLIK. Gercek saglayiciyi cagiran bir kod, testte
///    de gercek para hareketi denerdi. Soyutlama sayesinde testte
///    "her zaman basarili" veya "her zaman basarisiz" bir uygulama
///    gecebiliyoruz.
///
/// 2) SAGLAYICI DEGISTIRME. Iyzico'dan Stripe'a gecmek, yalnizca yeni
///    bir uygulama yazip DI kaydini degistirmek demek. Application
///    katmaninda tek satir degismez.
///
/// 3) KATMAN KURALI. Application katmani HTTP istemcisi, API anahtari,
///    imza dogrulama gibi ALTYAPI detaylarini bilmemeli. Bu arayuz
///    o detaylari Infrastructure'da tutuyor.
///
/// PDF'in istedigi iki uygulama:
///   - MockPaymentProvider     -> her zaman basarili
///   - FailedPaymentProvider   -> her zaman basarisiz
/// ==================================================================
/// </summary>
public interface IPaymentService
{
    /// <summary>Saglayicinin adi. Payment kaydina yazilir.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Odemeyi baslatir.
    ///
    /// Gercek bir saglayicida bu, kullanicinin yonlendirilecegi bir
    /// odeme sayfasi URL'i dondururdu (3D Secure akisi).
    /// Simulasyonda dogrudan sonuc donuyor.
    /// </summary>
    Task<PaymentResult> CreatePaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Odemenin gercekten gerceklestigini SAGLAYICIYA SORARAK dogrular.
    ///
    /// ==============================================================
    /// BU METOT NEDEN VAR? -- Guvenligin temel tasi
    /// ==============================================================
    /// Gercek entegrasyonlarda saglayici, odeme sonucunu bize bir
    /// "callback" (webhook) ile bildirir. AMA o callback'e KORU KORUNE
    /// GUVENILMEZ: saldirgan callback adresini bulup "odeme basarili"
    /// diye sahte bir istek gonderebilir ve bedava bilet alabilir.
    ///
    /// Dogru akis: callback geldiginde saglayiciya GERI SORARIZ --
    /// "gercekten bu odeme basarili mi?" Yalnizca saglayici onaylarsa
    /// bileti uretiriz.
    ///
    /// Simulasyonda da bu adimi ISLETIYORUZ ki gercek saglayiciya
    /// gecerken akis degismesin.
    /// ==============================================================
    /// </summary>
    Task<PaymentResult> VerifyPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    /// <summary>Iade. Kismi iade destekler.</summary>
    Task<PaymentResult> RefundPaymentAsync(
        string providerReference,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Henuz tahsil edilmemis bir odemeyi iptal eder.
    ///
    /// Iade'den farki: iade tahsil edilmis parayi geri gonderir
    /// (ve genelde komisyon iade edilmez); iptal ise para hic
    /// hareket etmeden islemi durdurur.
    /// </summary>
    Task<PaymentResult> CancelPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default);
}
