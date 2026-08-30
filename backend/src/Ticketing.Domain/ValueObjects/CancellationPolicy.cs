using Ticketing.Domain.Common;

namespace Ticketing.Domain.ValueObjects;

/// <summary>
/// Bilet iade politikasi. PDF Sprint 1, soru 10'un karşılığı.
///
/// Etkinlik başına saklanir (Event.CancellationPolicy) çünkü her organizatör
/// kendi politikasini belirleyebilmeli. Veritabaninda jsonb olarak tutulacak.
///
/// Neden ayri bir value object?
///
/// Alternatif su olurdu: Event tablosuna uc ayrı sutun koymak
///     FullRefundHours, PartialRefundHours, PartialRefundPercentage
///
/// Bunu yapmadim çünkü:
///
/// 1) Bu uc deger birbirine baglidir. "7 gunden fazlaysa %100" kuralı
///    tek başına anlamsizdir; digerleriyle birlikte tutarli olmalıdır.
///    Ayrı sutunlar olsaydı tutarliligi Event sinifinda kontrol etmem
///    gerekirdi ve Event zaten yeterince kalabalik.
///
/// 2) İade oranı hesaplama mantığı (CalculateRefundRate) burada yasiyor.
///    Ayrı sutun olsaydı bu hesap ya Event'e ya da bir servise dagilirdi.
///    Veri ve önü kullanan davranis aynı yerde durmali.
///
/// 3) Test etmesi kolay: Event olusturmadan politikayi test edebiliyorum.
/// </summary>
public sealed record CancellationPolicy
{
    /// <summary>
    /// Bu saatten FAZLA süre varsa tam iade. Varsayılan: 168 saat (7 gün).
    /// </summary>
    public int FullRefundThresholdHours { get; init; }

    /// <summary>
    /// Bu saatten FAZLA süre varsa kismi iade. Varsayılan: 48 saat.
    /// Bunun altinda iade yok.
    /// </summary>
    public int PartialRefundThresholdHours { get; init; }

    /// <summary>Kismi iade oranı (0-100 arasi yüzde). Varsayılan: 50.</summary>
    public int PartialRefundPercentage { get; init; }

    /// <summary>
    /// Parametre adları, property adlarinin camelCase halidir. Bu ONEMLI:
    ///
    /// EF Core bir nesneyi veritabanindan olustururken uygun bir constructor
    /// arar ve parametreleri property adlarina göre eslestirir. Parametre adı
    /// "fullHours" olsaydı EF önü "FullRefundThresholdHours" property'siyle
    /// eslestiremez ve su hatayi verirdi:
    ///     "No suitable constructor was found for entity type"
    ///
    /// (İlk yazisimda kisa adlar kullanmistim; migration uretirken tam da
    /// bu hatayi aldim. Kisa adlar okunakli gorunuyordu ama EF'in
    /// eslestirme kuralini bozuyordu.)
    /// </summary>
    private CancellationPolicy(
        int fullRefundThresholdHours,
        int partialRefundThresholdHours,
        int partialRefundPercentage)
    {
        FullRefundThresholdHours = fullRefundThresholdHours;
        PartialRefundThresholdHours = partialRefundThresholdHours;
        PartialRefundPercentage = partialRefundPercentage;
    }

    /// <summary>
    /// Varsayılan politika (docs/01-is-analizi.md soru 10):
    ///   7 gunden fazla  -> %100
    ///   48 saat - 7 gün -> %50
    ///   48 saatten az   -> iade yok
    /// </summary>
    public static CancellationPolicy Default { get; } = new(168, 48, 50);

    public static CancellationPolicy Create(int fullHours, int partialHours, int partialPercentage)
    {
        if (fullHours < 0 || partialHours < 0)
        {
            throw new DomainException("İade esikleri negatif olamaz.", "cancellation_policy.negative_threshold");
        }

        // Tam iade esigi, kismi iade esiginden BUYUK olmalı.
        // Tersi mantiksiz olurdu: "48 saatten fazlaysa tam iade,
        // 168 saatten fazlaysa yarim iade" -- kullanıcı erken iptal
        // ettigi için CEZALANDIRILMIS olurdu.
        //
        // Bu kontrolü koymasaydim organizatör yanlislikla ters degerler
        // girebilir ve kimse fark etmezdi; sadece musteriler sikayet ederdi.
        if (fullHours <= partialHours)
        {
            throw new DomainException(
                "Tam iade esigi, kismi iade esiginden büyük olmalıdır.",
                "cancellation_policy.invalid_thresholds");
        }

        if (partialPercentage is < 0 or > 100)
        {
            throw new DomainException(
                "Kismi iade oranı 0-100 arasında olmalıdır.",
                "cancellation_policy.invalid_percentage");
        }

        return new CancellationPolicy(fullHours, partialHours, partialPercentage);
    }

    /// <summary>
    /// İptal anında uygulanacak iade oranini yüzde olarak döndürür (0-100).
    /// </summary>
    /// <param name="eventStartsAt">Etkinligin başlangıç zamani (UTC).</param>
    /// <param name="cancelledAt">İptal talebinin yapildigi an (UTC).</param>
    public int CalculateRefundPercentage(DateTimeOffset eventStartsAt, DateTimeOffset cancelledAt)
    {
        // "Simdi" degerini PARAMETRE olarak alıyorum, DateTimeOffset.UtcNow
        // cagirmiyorum.
        //
        // Sebep: zamana bağlı mantığı test edebilmek. Iceride UtcNow
        // cagirsaydim "etkinlige 3 gün kala iptal" senaryosunu test etmek
        // için sistem saatini degistirmem gerekirdi. Parametre olarak
        // alınca istedigim ani verebiliyorum.
        //
        // Bu kalibin adı "dependency injection of time" veya kisaca
        // zamani disaridan almak. Sprint 3'te bunu tüm sisteme yaymak için
        // bir ITimeProvider arayuzu ekleyecegim.
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
