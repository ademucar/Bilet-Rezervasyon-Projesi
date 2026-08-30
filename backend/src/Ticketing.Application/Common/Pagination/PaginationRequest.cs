namespace Ticketing.Application.Common.Pagination;

/// <summary>
/// Sayfalama isteği. Tüm listeleme sorgulari bunu miras alacak.
///
/// PDF Sprint 11 ornegi:
///     GET /api/v1/events?pageNumber=1&amp;pageSize=20
/// </summary>
public abstract record PaginationRequest
{
    /// <summary>
    /// Izin verilen en büyük sayfa boyutu.
    ///
    /// Bu sabit neden bir güvenlik onlemi?
    ///
    /// Ust sinir olmasaydı bir kullanıcı
    ///     GET /api/v1/events?pageSize=999999999
    /// isteyebilirdi. Sunucu tüm tabloyu belege yukler, JSON'a cevirir
    /// ve muhtemelen OutOfMemoryException ile coker.
    ///
    /// Bu, kod yazmayi bilen herkesin yapabilecegi en basit servis dışı
    /// birakma (DoS) saldirisidir ve sik atlanir. Ust sınırı sunucu
    /// tarafında ZORLAMAK sart -- frontend'in doğru deger gonderecegine
    /// guvenemem.
    /// </summary>
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 20;

    private readonly int _pageNumber = 1;

    private readonly int _pageSize = DefaultPageSize;

    /// <summary>
    /// Sayfa numarasi. 1'den başlar.
    ///
    /// Geçersiz deger gonderilirse hata firlatmiyor, duzeltiyorum.
    ///
    /// Neden? Sayfalama kullanıcının veri talebinin OZU değil, sunum
    /// detayidir. "pageNumber=0" gonderen bir istemciye 400 donup
    /// akışı kesmek yerine ilk sayfayı göstermek daha iyi bir deneyim.
    ///
    /// Ama bu esneklik SADECE sayfalama için geçerli: is verisinde
    /// (tutar, tarih, koltuk) asla sessizce duzeltme yapmayiz --
    /// orada yanlış veriyi kabul etmek gerçek hasara yol acar.
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
    /// Veritabani sorgusunda Skip() için kullanilacak deger.
    ///
    /// Bu hesabi burada yapmamin sebebi: her sorguda
    /// "(pageNumber - 1) * pageSize" yazarsak birinde mutlaka
    /// -1'i unuturuz ve ilk sayfa atlanir. Tek yerde tutmak
    /// bu klasik hatayi engelliyor.
    /// </summary>
    public int Skip => (PageNumber - 1) * PageSize;
}
