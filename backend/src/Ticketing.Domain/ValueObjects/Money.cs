using System.Globalization;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.ValueObjects;

/// <summary>
/// Para birimi ile birlikte bir tutarı temsil eden value object.
///
/// PDF Sprint 6: "Para değerleri decimal olarak tutulmalidir. Floating point
/// kullanilmamalidir. Currency alanı bulunmalidir."
///
/// NEDEN decimal, double DEĞİL?
///
/// double ikili (binary) tabanda çalışır ve 0.1 sayisini TAM olarak temsil
/// edemez -- tipki benim 1/3'u ondalik olarak tam yazamadigimiz gibi.
///
///     double a = 0.1 + 0.2;   // 0.30000000000000004
///
/// 10.000 biletlik bir etkinlikte bu hatalar birikir. Rapordaki toplam gelir
/// ile odemelerin toplami tutmaz. Muhasebe bunu kabul etmez.
///
/// decimal ONDALIK tabanda çalışır, 28-29 basamak hassasiyeti vardir ve
/// tam olarak bu is için tasarlanmistir. Biraz daha yavastir ama para
/// hesabinda hiz değil DOGRULUK onceliklidir.
///
/// NEDEN readonly record struct?
///
/// record  : Para bir KIMLIK değil, bir DEGERDIR. 100 TL ile 100 TL aynı
///           seydir; hangi nesne oldugunun onemi yoktur. record bana bu
///           deger bazlı esitligi (Equals, GetHashCode, ==) bedavaya verir.
///           Elle yazsaydim mutlaka bir hata yapardim.
///
/// struct  : Her para isleminde heap'te yeni nesne olusturmayi engeller.
///           Bir rezervasyonda 8 koltuk varsa 8 kez toplama yapiyorum;
///           bunlarin cop toplayiciya yuk olmasinin anlami yok.
///
/// readonly: Olusturulduktan sonra DEGISTIRILEMEZ. Paranin sessizce
///           degismesi istemeyecegim son seydir. Yeni bir tutar gerekiyorsa
///           yeni bir Money üretilir.
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }

    /// <summary>ISO 4217 kodu: TRY, USD, EUR.</summary>
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainException("Tutar negatif olamaz.", "money.negative");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new DomainException(
                "Para birimi 3 harfli ISO 4217 kodu olmalıdır (TRY, USD, EUR).",
                "money.invalid_currency");
        }

        // Kurus hassasiyetinde yuvarliyorum.
        //
        // MidpointRounding.ToEven ("banker's rounding") kullanıyorum:
        //     2.125 -> 2.12   (2 cift olduğu için asagi)
        //     2.135 -> 2.14   (4 cift olduğu için yukari)
        //
        // Neden her zaman yukari yuvarlamak yerine bu? Çünkü surekli yukari
        // yuvarlamak çok sayıda islemde SISTEMATIK bir sapma yaratir --
        // binlerce biletin toplaminda kurumun lehine anlamlı bir fark birikir.
        // ToEven yuvarlamalari uzun vadede dengeler. Bankacilik standardidir.
        //
        // Not: .NET'in Math.Round varsayilani zaten ToEven'dir, ama bunu
        // ACIKCA yazıyorum ki okuyan kişi bunun bilinçli bir tercih olduğunu
        // gorsun, kazara olusmus bir davranis sanmasin.
        Amount = Math.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    // Operatorler

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);

        // Sonuç negatifse yapici zaten DomainException firlatacak.
        // Bu kasitli: bir odemeden odenenden fazlasini iade edemezsin.
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, int quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException("Adet negatif olamaz.", "money.negative_quantity");
        }

        return new Money(money.Amount * quantity, money.Currency);
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right) => !(left < right);

    public static bool operator <=(Money left, Money right) => !(left > right);

    // CA2225: Operator overload'larin adlandirilmis karşılığı olmalıdır.
    // Sebep: C# disindaki bazi .NET dilleri (örneğin eski VB.NET surumleri)
    // operator overload'larini cagiramaz. Bu metotlar onlar için kapi acar.
    // Ayrıca LINQ ifadelerinde metot adiyla kullanmak daha okunakli olabilir.
    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Multiply(Money money, int quantity) => money * quantity;

    /// <summary>
    /// Farklı para birimlerini toplamak bir PROGRAMLAMA hatasidir,
    /// kullanıcı hatası değil. Sessizce donusturmek (örneğin kur cekip
    /// cevirmek) burada yapilacak son sey olurdu: hangi kurun, hangi
    /// tarihte kullanildigi belirsiz kalır ve hata gorunmez hale gelir.
    /// Bu yüzden acikca patlatiyorum.
    /// </summary>
    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                $"Farklı para birimleri islenemez: {left.Currency} ve {right.Currency}",
                "money.currency_mismatch");
        }
    }

    /// <summary>
    /// Log ve hata mesajlarinda okunabilir cikti için.
    /// InvariantCulture kullanıyorum: log'larin sunucunun bolge ayarina
    /// göre degismesini istemiyorum. Turkce kulturde ondalik ayraci virgul,
    /// Ingilizce'de noktadir; loglari analiz eden arac bunu ayırt edemez.
    /// </summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
