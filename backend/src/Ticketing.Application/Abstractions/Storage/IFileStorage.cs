namespace Ticketing.Application.Abstractions.Storage;

/// <summary>
/// Dosya depolama soyutlamasi. PDF Sprint 15.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN SOYUTLAMA? DISKE YAZMAK ZATEN BASIT
/// ==================================================================
/// File.WriteAllBytes cagirmak bir satir. O zaman bu arayuz niye?
///
/// 1) Application katmani File API'sini BILMEMELI. Onion mimarisinde
///    is mantigi altyapiya bagimli olamaz -- mimari testimiz bunu
///    zaten zorluyor.
///
/// 2) Uretimde diske yazmak CALISMAZ. Birden fazla sunucu olunca
///    kullanici A sunucusuna yukler, B sunucusundan indirmeye
///    calisir ve dosya bulunamaz. Uretimde S3/Azure Blob gerekiyor.
///    O gun yalnizca bu arayuzun yeni bir uygulamasi yazilacak.
///
/// 3) Test edilebilirlik: handler'i gercek disk olmadan test
///    edebiliyoruz.
/// ==================================================================
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Dosyayi kaydeder ve depolama yolunu doner.
    /// </summary>
    /// <param name="storedFileName">
    /// URETILMIS guvenli dosya adi. Kullanicidan gelen ad ASLA
    /// buraya gecmemeli -- cagiran taraf bunu garanti etmeli.
    /// </param>
    Task<string> SaveAsync(
        string storedFileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Dosyayi siler. Dosya yoksa sessizce gecer.</summary>
    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
}
