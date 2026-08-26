using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Yuklenen dosya kaydi. PDF Sprint 15'in dosya guvenligi maddelerini
/// (file type kontrolu, MIME type kontrolu, guvenli dosya adi) destekler.
///
/// Neden dosya bilgilerini ayri bir tabloda tutuyoruz?
///
/// 1) Sahipsiz dosyalari (orphan) temizleyebilmek icin. Kullanici afis
///    yukleyip etkinligi kaydetmezse dosya diskte kalir. Bu tablo
///    sayesinde bir background job "hicbir kayda bagli olmayan
///    dosyalari sil" diyebilir.
///
/// 2) Denetim: kim, ne zaman, hangi dosyayi yukledi.
///
/// 3) Depolama saglayicisi degisirse (disk -> S3) sadece bu tablodaki
///    yollar guncellenir.
/// </summary>
public class UploadedFile : AuditableEntity
{
    private UploadedFile()
    {
        FileName = string.Empty;
        StoredFileName = string.Empty;
        ContentType = string.Empty;
        StoragePath = string.Empty;
    }

    /// <summary>Kullanicinin yukledigi ORIJINAL dosya adi. Sadece gosterim icin.</summary>
    public string FileName { get; private set; }

    /// <summary>
    /// Diskte kullandigimiz GUVENLI dosya adi (Guid + uzanti).
    ///
    /// Neden orijinal adi kullanmiyoruz?
    /// Kullanici "../../appsettings.json" veya "afis.jpg.exe" gibi bir ad
    /// gonderebilir. Ilki dizin gecisi (path traversal) saldirisidir,
    /// ikincisi calistirilabilir dosya gizlemedir.
    ///
    /// Uretilen bir ad kullanarak bu sinifin tamamini ortadan kaldiriyoruz.
    /// Kullanicidan gelen HICBIR metin dosya yolunda kullanilmiyor.
    /// </summary>
    public string StoredFileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeInBytes { get; private set; }

    public string StoragePath { get; private set; }

    /// <summary>
    /// Bu dosya hangi kayda ait? Ornek: "Event", "OrganizerProfile".
    /// null ise henuz hicbir kayda baglanmamis (temizlik adayi).
    /// </summary>
    public string? RelatedEntityName { get; private set; }

    public Guid? RelatedEntityId { get; private set; }

    public static UploadedFile Create(
        string fileName,
        string storedFileName,
        string contentType,
        long sizeInBytes,
        string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            throw new DomainException("Saklanan dosya adi bos olamaz.", "uploaded_file.name_required");
        }

        if (sizeInBytes <= 0)
        {
            throw new DomainException("Dosya boyutu sifirdan buyuk olmalidir.", "uploaded_file.invalid_size");
        }

        return new UploadedFile
        {
            FileName = fileName,
            StoredFileName = storedFileName,
            ContentType = contentType,
            SizeInBytes = sizeInBytes,
            StoragePath = storagePath
        };
    }

    /// <summary>
    /// Dosyayi bir kayda baglar. Bundan sonra temizlik job'i silmez.
    /// </summary>
    public void AttachTo(string entityName, Guid entityId)
    {
        RelatedEntityName = entityName;
        RelatedEntityId = entityId;
    }

    /// <summary>Hicbir kayda bagli degil mi? Temizlik job'i bunu sorar.</summary>
    public bool IsOrphan() => RelatedEntityId is null;
}
