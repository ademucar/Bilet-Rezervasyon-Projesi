namespace Ticketing.Application.Abstractions.Storage;

/// <summary>
/// Dosya depolama soyutlamasi. PDF Sprint 15.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN SOYUTLAMA? DISKE YAZMAK ZATEN BASIT
/// ==================================================================
/// File.WriteAllBytes cagirmak bir satır. O zaman bu arayüz niye?
///
/// 1) Application katmani File API'sini BILMEMELI. Onion mimarisinde
///    is mantığı altyapiya bagimli olamaz -- mimari testimiz bunu
///    zaten zorluyor.
///
/// 2) Uretimde diske yazmak CALISMAZ. Birden fazla sunucu olunca
///    kullanıcı A sunucusuna yukler, B sunucusundan indirmeye
///    çalışır ve dosya bulunamaz. Uretimde S3/Azure Blob gerekiyor.
///    O gün yalnızca bu arayuzun yeni bir uygulamasi yazilacak.
///
/// 3) Test edilebilirlik: handler'i gerçek disk olmadan test
///    edebiliyoruz.
/// ==================================================================
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Dosyayi kaydeder ve depolama yolunu döner.
    /// </summary>
    /// <param name="storedFileName">
    /// URETILMIS güvenli dosya adı. Kullanicidan gelen ad ASLA
    /// buraya gecmemeli -- cagiran taraf bunu garanti etmeli.
    /// </param>
    Task<string> SaveAsync(
        string storedFileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Dosyayi siler. Dosya yoksa sessizce gecer.</summary>
    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
}
