using Ticketing.Domain.Common;

namespace Ticketing.Domain.ValueObjects;

/// <summary>
/// Bilet iade politikasi. PDF Sprint 1, soru 10'un karsiligi.
///
/// Etkinlik basina saklanir (Event.CancellationPolicy) cunku her organizator
/// kendi politikasini belirleyebilmeli. Veritabaninda jsonb olarak tutulacak.
///
/// ------------------------------------------------------------------
/// NEDEN AYRI BIR VALUE OBJECT?
/// ------------------------------------------------------------------
/// Alternatif su olurdu: Event tablosuna uc ayri sutun koymak
///     FullRefundHours, PartialRefundHours, PartialRefundPercentage
///
/// Bunu yapmadim cunku:
///
/// 1) Bu uc deger BIRBIRINE BAGLIDIR. "7 gunden fazlaysa %100" kurali
///    tek basina anlamsizdir; digerleriyle birlikte tutarli olmalidir.
///    Ayri sutunlar olsaydi tutarliligi Event sinifinda kontrol etmem
///    gerekirdi ve Event zaten yeterince kalabalik.
///
/// 2) Iade orani hesaplama mantigi (CalculateRefundRate) burada yasiyor.
///    Ayri sutun olsaydi bu hesap ya Event'e ya da bir servise dagilirdi.
///    Veri ve onu kullanan davranis ayni yerde durmali.
///
/// 3) Test etmesi kolay: Event olusturmadan politikayi test edebiliyorum.
/// </summary>
public sealed record CancellationPolicy
{
    /// <summary>
    /// Bu saatten FAZLA sure varsa tam iade. Varsayilan: 168 saat (7 gun).
    /// </summary>
    public int FullRefundThresholdHours { get; init; }

    /// <summary>
    /// Bu saatten FAZLA sure varsa kismi iade. Varsayilan: 48 saat.
    /// Bunun altinda iade yok.
    /// </summary>
    public int PartialRefundThresholdHours { get; init; }

    /// <summary>Kismi iade orani (0-100 arasi yuzde). Varsayilan: 50.</summary>
    public int PartialRefundPercentage { get; init; }

    private CancellationPolicy(int fullHours, int partialHours, int partialPercentage)
    {
        FullRefundThresholdHours = fullHours;
        PartialRefundThresholdHours = partialHours;
        PartialRefundPercentage = partialPercentage;
    }

    /// <summary>
    /// Varsayilan politika (docs/01-is-analizi.md soru 10):
    ///   7 gunden fazla  -> %100
    ///   48 saat - 7 gun -> %50
    ///   48 saatten az   -> iade yok
    /// </summary>
    public static CancellationPolicy Default { get; } = new(168, 48, 50);

    public static CancellationPolicy Create(int fullHours, int partialHours, int partialPercentage)
    {
        if (fullHours < 0 || partialHours < 0)
        {
            throw new DomainException("Iade esikleri negatif olamaz.", "cancellation_policy.negative_threshold");
        }

        // Tam iade esigi, kismi iade esiginden BUYUK olmali.
        // Tersi mantiksiz olurdu: "48 saatten fazlaysa tam iade,
        // 168 saatten fazlaysa yarim iade" -- kullanici erken iptal
        // ettigi icin CEZALANDIRILMIS olurdu.
        //
        // Bu kontrolu koymasaydim organizator yanlislikla ters degerler
        // girebilir ve kimse fark etmezdi; sadece musteriler sikayet ederdi.
        if (fullHours <= partialHours)
        {
            throw new DomainException(
                "Tam iade esigi, kismi iade esiginden buyuk olmalidir.",
                "cancellation_policy.invalid_thresholds");
        }

        if (partialPercentage is < 0 or > 100)
        {
            throw new DomainException(
                "Kismi iade orani 0-100 arasinda olmalidir.",
                "cancellation_policy.invalid_percentage");
        }

        return new CancellationPolicy(fullHours, partialHours, partialPercentage);
    }

    /// <summary>
    /// Iptal aninda uygulanacak iade oranini yuzde olarak dondurur (0-100).
    /// </summary>
    /// <param name="eventStartsAt">Etkinligin baslangic zamani (UTC).</param>
    /// <param name="cancelledAt">Iptal talebinin yapildigi an (UTC).</param>
    public int CalculateRefundPercentage(DateTimeOffset eventStartsAt, DateTimeOffset cancelledAt)
    {
        // "Simdi" degerini PARAMETRE olarak aliyorum, DateTimeOffset.UtcNow
        // cagirmiyorum.
        //
        // Sebep: zamana bagli mantigi test edebilmek. Iceride UtcNow
        // cagirsaydim "etkinlige 3 gun kala iptal" senaryosunu test etmek
        // icin sistem saatini degistirmem gerekirdi. Parametre olarak
        // alinca istedigim ani verebiliyorum.
        //
        // Bu kalibin adi "dependency injection of time" veya kisaca
        // zamani disaridan almak. Sprint 3'te bunu tum sisteme yaymak icin
        // bir ITimeProvider arayuzu ekleyecegiz.
        var kalanSaat = (eventStartsAt - cancelledAt).TotalHours;

        if (kalanSaat > FullRefundThresholdHours)
        {
            return 100;
        }

        if (kalanSaat > PartialRefundThresholdHours)
        {
            return PartialRefundPercentage;
        }

        return 0;
    }
}
