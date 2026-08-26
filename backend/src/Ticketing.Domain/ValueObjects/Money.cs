using System.Globalization;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.ValueObjects;

/// <summary>
/// Para birimi ile birlikte bir tutari temsil eden value object.
///
/// PDF Sprint 6: "Para degerleri decimal olarak tutulmalidir. Floating point
/// kullanilmamalidir. Currency alani bulunmalidir."
///
/// ------------------------------------------------------------------
/// NEDEN decimal, double DEGIL?
/// ------------------------------------------------------------------
/// double ikili (binary) tabanda calisir ve 0.1 sayisini TAM olarak temsil
/// edemez -- tipki bizim 1/3'u ondalik olarak tam yazamadigimiz gibi.
///
///     double a = 0.1 + 0.2;   // 0.30000000000000004
///
/// 10.000 biletlik bir etkinlikte bu hatalar birikir. Rapordaki toplam gelir
/// ile odemelerin toplami tutmaz. Muhasebe bunu kabul etmez.
///
/// decimal ONDALIK tabanda calisir, 28-29 basamak hassasiyeti vardir ve
/// tam olarak bu is icin tasarlanmistir. Biraz daha yavastir ama para
/// hesabinda hiz degil DOGRULUK onceliklidir.
///
/// ------------------------------------------------------------------
/// NEDEN readonly record struct?
/// ------------------------------------------------------------------
/// record  : Para bir KIMLIK degil, bir DEGERDIR. 100 TL ile 100 TL ayni
///           seydir; hangi nesne oldugunun onemi yoktur. record bana bu
///           deger bazli esitligi (Equals, GetHashCode, ==) bedavaya verir.
///           Elle yazsaydim mutlaka bir hata yapardim.
///
/// struct  : Her para isleminde heap'te yeni nesne olusturmayi engeller.
///           Bir rezervasyonda 8 koltuk varsa 8 kez toplama yapiyoruz;
///           bunlarin cop toplayiciya yuk olmasinin anlami yok.
///
/// readonly: Olusturulduktan sonra DEGISTIRILEMEZ. Paranin sessizce
///           degismesi istemeyecegim son seydir. Yeni bir tutar gerekiyorsa
///           yeni bir Money uretilir.
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
                "Para birimi 3 harfli ISO 4217 kodu olmalidir (TRY, USD, EUR).",
                "money.invalid_currency");
        }

        // Kurus hassasiyetinde yuvarliyorum.
        //
        // MidpointRounding.ToEven ("banker's rounding") kullaniyorum:
        //     2.125 -> 2.12   (2 cift oldugu icin asagi)
        //     2.135 -> 2.14   (4 cift oldugu icin yukari)
        //
        // Neden her zaman yukari yuvarlamak yerine bu? Cunku surekli yukari
        // yuvarlamak cok sayida islemde SISTEMATIK bir sapma yaratir --
        // binlerce biletin toplaminda kurumun lehine anlamli bir fark birikir.
        // ToEven yuvarlamalari uzun vadede dengeler. Bankacilik standardidir.
        //
        // Not: .NET'in Math.Round varsayilani zaten ToEven'dir, ama bunu
        // ACIKCA yaziyorum ki okuyan kisi bunun bilincli bir tercih oldugunu
        // gorsun, kazara olusmus bir davranis sanmasin.
        Amount = Math.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    // ---------------------------------------------------------------
    // Operatorler
    // ---------------------------------------------------------------

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);

        // Sonuc negatifse yapici zaten DomainException firlatacak.
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

    // CA2225: Operator overload'larin adlandirilmis karsiligi olmalidir.
    // Sebep: C# disindaki bazi .NET dilleri (ornegin eski VB.NET surumleri)
    // operator overload'larini cagiramaz. Bu metotlar onlar icin kapi acar.
    // Ayrica LINQ ifadelerinde metot adiyla kullanmak daha okunakli olabilir.
    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Multiply(Money money, int quantity) => money * quantity;

    /// <summary>
    /// Farkli para birimlerini toplamak bir PROGRAMLAMA hatasidir,
    /// kullanici hatasi degil. Sessizce donusturmek (ornegin kur cekip
    /// cevirmek) burada yapilacak son sey olurdu: hangi kurun, hangi
    /// tarihte kullanildigi belirsiz kalir ve hata gorunmez hale gelir.
    /// Bu yuzden acikca patlatiyorum.
    /// </summary>
    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                $"Farkli para birimleri islenemez: {left.Currency} ve {right.Currency}",
                "money.currency_mismatch");
        }
    }

    /// <summary>
    /// Log ve hata mesajlarinda okunabilir cikti icin.
    /// InvariantCulture kullaniyorum: log'larin sunucunun bolge ayarina
    /// gore degismesini istemiyorum. Turkce kulturde ondalik ayraci virgul,
    /// Ingilizce'de noktadir; loglari analiz eden arac bunu ayirt edemez.
    /// </summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
