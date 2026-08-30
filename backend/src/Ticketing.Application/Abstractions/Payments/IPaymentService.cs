namespace Ticketing.Application.Abstractions.Payments;

/// <summary>
/// Ödeme saglayicisina gonderilen istek.
/// </summary>
/// <param name="PaymentId">Bizim tarafimizdaki ödeme kaydinin kimliği.</param>
/// <param name="Amount">Tutar.</param>
/// <param name="Currency">ISO 4217 para birimi.</param>
/// <param name="Description">Ödeme açıklaması (ekstre satirinda görünür).</param>
public sealed record PaymentRequest(
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string Description);

/// <summary>
/// Saglayicidan donen sonuç.
/// </summary>
/// <param name="IsSuccess">İşlem başarılı mi?</param>
/// <param name="ProviderReference">
/// Saglayicinin verdiği işlem numarasi.
///
/// Bu alan MUTABAKAT (reconciliation) için kritik: ay sonunda
/// saglayicidan gelen ekstreyi kendi kayitlarimizla eslestirirken
/// bu numarayi kullanıyorum. Ayrıca destek talebinde "su islemde
/// ne oldu" diye sorarken saglayiciya bu numarayi veriyorum.
/// </param>
/// <param name="ErrorCode">Basarisizsa sağlayıcının hata kodu.</param>
/// <param name="ErrorMessage">Kullanıcıya gosterilebilir hata mesaji.</param>
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
/// Ödeme saglayici soyutlamasi
///
/// PDF Sprint 8:
///   "Gerçek ödeme sağlayıcısı kullanilmak zorunda degildir. Ancak
///    ödeme sağlayıcısı entegrasyonuna BENZER bir yapi kurulmalidir."
///
/// Istenen metotlar: CreatePayment, VerifyPayment, RefundPayment,
/// CancelPayment.
///
/// NEDEN SOYUTLAMA? Dogrudan Iyzico cagirsak olmaz miydi?
///
/// Uc sebep:
///
/// 1) TEST EDILEBILIRLIK. Gerçek saglayiciyi cagiran bir kod, testte
///    de gerçek para hareketi denerdi. Soyutlama sayesinde testte
///    "her zaman başarılı" veya "her zaman başarısız" bir uygulama
///    gecebiliyoruz.
///
/// 2) SAGLAYICI DEGISTIRME. Iyzico'dan Stripe'a gecmek, yalnızca yeni
///    bir uygulama yazip DI kaydini degistirmek demek. Application
///    katmaninda tek satır degismez.
///
/// 3) KATMAN KURALI. Application katmani HTTP istemcisi, API anahtari,
///    imza doğrulama gibi ALTYAPI detaylarini bilmemeli. Bu arayüz
///    o detaylari Infrastructure'da tutuyor.
///
/// PDF'in istedigi iki uygulama:
///   - MockPaymentProvider     -> her zaman başarılı
///   - FailedPaymentProvider   -> her zaman başarısız
/// </summary>
public interface IPaymentService
{
    /// <summary>Saglayicinin adı. Payment kaydina yazilir.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Ödemeyi baslatir.
    ///
    /// Gerçek bir saglayicida bu, kullanıcının yonlendirilecegi bir
    /// ödeme sayfası URL'i dondururdu (3D Secure akışı).
    /// Simulasyonda doğrudan sonuç dönüyor.
    /// </summary>
    Task<PaymentResult> CreatePaymentAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Odemenin gerçekten gerceklestigini SAGLAYICIYA SORARAK dogrular.
    ///
    /// BU METOT NEDEN VAR? -- Guvenligin temel tasi
    ///
    /// Gerçek entegrasyonlarda sağlayıcı, ödeme sonucunu bana bir
    /// "callback" (webhook) ile bildirir. AMA o callback'e KORU KORUNE
    /// GUVENILMEZ: saldirgan callback adresini bulup "ödeme başarılı"
    /// diye sahte bir istek gonderebilir ve bedava bilet alabilir.
    ///
    /// Dogru akis: callback geldiğinde saglayiciya GERİ SORARIZ --
    /// "gerçekten bu ödeme başarılı mi?" Yalnızca sağlayıcı onaylarsa
    /// bileti uretiriz.
    ///
    /// Simulasyonda da bu adimi ISLETIYORUZ ki gerçek saglayiciya
    /// gecerken akis degismesin.
    /// </summary>
    Task<PaymentResult> VerifyPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    /// <summary>İade. Kismi iade destekler.</summary>
    Task<PaymentResult> RefundPaymentAsync(
        string providerReference,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Henüz tahsil edilmemis bir ödemeyi iptal eder.
    ///
    /// İade'den farki: iade tahsil edilmiş parayi geri gönderir
    /// (ve genelde komisyon iade edilmez); iptal ise para hiç
    /// hareket etmeden islemi durdurur.
    /// </summary>
    Task<PaymentResult> CancelPaymentAsync(
        string providerReference,
        CancellationToken cancellationToken = default);
}
