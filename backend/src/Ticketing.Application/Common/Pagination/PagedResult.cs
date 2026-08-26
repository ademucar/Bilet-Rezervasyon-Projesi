using System.Diagnostics.CodeAnalysis;

namespace Ticketing.Application.Common.Pagination;

/// <summary>
/// Sayfalanmis liste sonucu.
///
/// PDF Sprint 11'in istedigi API yaniti:
///   Items, PageNumber, PageSize, TotalCount, TotalPages,
///   HasPreviousPage, HasNextPage
/// </summary>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification =
        "CA1000, PagedResult<Event>.Create() gibi cagrilarda tip parametresini " +
        "yazmak zorunda kalmayi 'kullanim zorlugu' sayar. " +
        "Ancak bu, factory metot kalibinin dogal sonucudur ve .NET'in kendisi de " +
        "ayni yaklasimi kullanir (ornegin ImmutableArray<T>.Empty). " +
        "Alternatif, ayri bir static olmayan fabrika sinifi yazmak olurdu; bu, " +
        "hicbir sey kazandirmadan bir tip daha ekler. " +
        "Kural yalnizca bu tip icin bastirildi.")]
public sealed class PagedResult<T>
{
    private PagedResult(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalCount)
    {
        Items = items;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    /// <summary>
    /// Toplam sayfa sayisi.
    ///
    /// (int)Math.Ceiling(totalCount / (double)pageSize) yerine
    /// tam sayi aritmetigi kullaniyorum: (a + b - 1) / b
    ///
    /// Neden? double'a cevirmek cok buyuk sayilarda hassasiyet kaybina
    /// yol acabilir ve gereksiz bir donusum. Tam sayi versiyonu hem
    /// daha hizli hem de her zaman kesin dogru.
    ///
    /// Ornek: 25 kayit, sayfa boyutu 10
    ///   (25 + 10 - 1) / 10 = 34 / 10 = 3   (tam sayi bolmesi)
    /// </summary>
    public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Create(
        IReadOnlyList<T> items,
        int pageNumber,
        int pageSize,
        int totalCount)
        => new(items, pageNumber, pageSize, totalCount);

    /// <summary>
    /// Bos sonuc. Arama hicbir kayit dondurmediginde kullanilir.
    ///
    /// null donmek yerine bos bir sayfa donuyorum: frontend'in
    /// "sonuc yok mu yoksa hata mi?" diye ayirt etmesi gerekmesin.
    /// Bos liste, "arama calisti ama eslesme yok" demektir.
    /// </summary>
    public static PagedResult<T> Empty(int pageNumber, int pageSize)
        => new([], pageNumber, pageSize, 0);
}
