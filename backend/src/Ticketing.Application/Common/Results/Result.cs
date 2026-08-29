using System.Diagnostics.CodeAnalysis;

namespace Ticketing.Application.Common.Results;

/// <summary>
/// Bir islemin sonucunu temsil eder: ya başarılı ya da hatalı.
///
/// PDF Sprint 2: "Ortak result modeli hazırlanmalıdır."
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // BU KONTROL NEDEN VAR?
        //
        // Iki tutarsiz durum mumkun:
        //   - Başarılı ama hata dolu   -> "Basardim ama hata var" (celiski)
        //   - Başarısız ama hata boş   -> "Basaramadim ama sebebi yok" (ise yaramaz)
        //
        // Ikisi de PROGRAMLAMA hatasidir, kullanıcı hatası değil. Bu yüzden
        // Result donmuyor, doğrudan patlatiyorum: hatalı kullanim uretime
        // cikmadan önce, ilk testte ortaya ciksin.
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Başarılı bir sonuç hata icseremez.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Başarısız bir sonuç hata içermelidir.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// Deger dondurun islemler için Result.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification =
        "CA1000, PagedResult<Event>.Create() gibi cagrilarda tip parametresini " +
        "yazmak zorunda kalmayi 'kullanim zorlugu' sayar. " +
        "Ancak bu, factory metot kalibinin dogal sonucudur ve .NET'in kendisi de " +
        "aynı yaklasimi kullanir (örneğin ImmutableArray<T>.Empty). " +
        "Alternatif, ayrı bir static olmayan fabrika sinifi yazmak olurdu; bu, " +
        "hicbir sey kazandirmadan bir tip daha ekler. " +
        "Kural yalnızca bu tip için bastirildi.")]
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// İşlem basariliysa deger.
    ///
    /// Başarısız bir sonucta bu alana erisilirse EXCEPTION firlatiyorum.
    /// null donmuyorum -- çünkü o zaman cagiran kişi null'i geçerli bir
    /// deger sanip devam edebilir ve hata çok ilerideki bir noktada,
    /// hiçbir sey anlatmayan bir NullReferenceException olarak patlar.
    ///
    /// Burada patlarsa hata mesaji net: "sonucu kontrol etmeden degere
    /// eristin".
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Başarısız bir sonucun değerine erişilemez. Hata: {Error.Code} - {Error.Message}");

    /// <summary>
    /// Sonucu güvenli şekilde okumak için.
    ///
    /// Kullanim:
    ///     if (result.TryGetValue(out var user)) { ... user'i kullan ... }
    ///
    /// [NotNullWhen(true)] niteligini eklememin sebebi: derleyiciye
    /// "bu metot true dondurdugunde value KESINLIKLE null degildir"
    /// demek. Boylece if bloguunun içinde derleyici null uyarısı vermiyor
    /// ve gereksiz null kontrolü yazmiyorum.
    /// </summary>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = IsSuccess ? _value : default;

        return IsSuccess;
    }

    /// <summary>
    /// Degerden Result'a ortuk (implicit) donusum.
    ///
    /// Bu sayede handler'larda
    ///     return Result.Success(user);
    /// yerine sadece
    ///     return user;
    /// yazabiliyorum. Küçük bir kolaylik ama 100 handler'da fark ediyor.
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>
    /// CA2225: Operator overload'lara adlandirilmis karsilik gerekir.
    /// </summary>
    public static Result<TValue> FromValue(TValue value) => Success(value);
}
