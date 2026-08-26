namespace Ticketing.Application.Common.Pagination;

/// <summary>
/// Sayfalama istegi. Tum listeleme sorgulari bunu miras alacak.
///
/// PDF Sprint 11 ornegi:
///     GET /api/v1/events?pageNumber=1&amp;pageSize=20
/// </summary>
public abstract record PaginationRequest
{
    /// <summary>
    /// Izin verilen en buyuk sayfa boyutu.
    ///
    /// ------------------------------------------------------------------
    /// BU SABIT NEDEN BIR GUVENLIK ONLEMI?
    /// ------------------------------------------------------------------
    /// Ust sinir olmasaydi bir kullanici
    ///     GET /api/v1/events?pageSize=999999999
    /// isteyebilirdi. Sunucu tum tabloyu belege yukler, JSON'a cevirir
    /// ve muhtemelen OutOfMemoryException ile coker.
    ///
    /// Bu, kod yazmayi bilen herkesin yapabilecegi en basit servis disi
    /// birakma (DoS) saldirisidir ve sik atlanir. Ust siniri sunucu
    /// tarafinda ZORLAMAK sart -- frontend'in dogru deger gonderecegine
    /// guvenemeyiz.
    /// </summary>
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 20;

    private readonly int _pageNumber = 1;

    private readonly int _pageSize = DefaultPageSize;

    /// <summary>
    /// Sayfa numarasi. 1'den baslar.
    ///
    /// Gecersiz deger gonderilirse HATA FIRLATMIYOR, duzeltiyorum.
    ///
    /// Neden? Sayfalama kullanicinin veri talebinin OZU degil, sunum
    /// detayidir. "pageNumber=0" gonderen bir istemciye 400 donup
    /// akisi kesmek yerine ilk sayfayi gostermek daha iyi bir deneyim.
    ///
    /// Ama bu esneklik SADECE sayfalama icin gecerli: is verisinde
    /// (tutar, tarih, koltuk) asla sessizce duzeltme yapmayiz --
    /// orada yanlis veriyi kabul etmek gercek hasara yol acar.
    /// </summary>
    public int PageNumber
    {
        get => _pageNumber;
        init => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>
    /// Veritabani sorgusunda Skip() icin kullanilacak deger.
    ///
    /// Bu hesabi burada yapmamin sebebi: her sorguda
    /// "(pageNumber - 1) * pageSize" yazarsak birinde mutlaka
    /// -1'i unuturuz ve ilk sayfa atlanir. Tek yerde tutmak
    /// bu klasik hatayi engelliyor.
    /// </summary>
    public int Skip => (PageNumber - 1) * PageSize;
}
