using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Yuklenen dosya kaydı. PDF Sprint 15'in dosya guvenligi maddelerini
/// (file type kontrolü, MIME type kontrolü, güvenli dosya adı) destekler.
///
/// Neden dosya bilgilerini ayrı bir tabloda tutuyorum?
///
/// 1) Sahipsiz dosyalari (orphan) temizleyebilmek için. Kullanıcı afis
///    yukleyip etkinligi kaydetmezse dosya diskte kalır. Bu tablo
///    sayesinde bir background job "hiçbir kayda bağlı olmayan
///    dosyalari sil" diyebilir.
///
/// 2) Denetim: kim, ne zaman, hangi dosyayı yukledi.
///
/// 3) Depolama sağlayıcısı degisirse (disk -> S3) sadece bu tablodaki
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

    /// <summary>Kullanıcının yukledigi ORIJINAL dosya adı. Sadece gosterim için.</summary>
    public string FileName { get; private set; }

    /// <summary>
    /// Diskte kullandigim GUVENLI dosya adı (Guid + uzanti).
    ///
    /// Neden orijinal adı kullanmiyorum?
    /// Kullanıcı "../../appsettings.json" veya "afis.jpg.exe" gibi bir ad
    /// gonderebilir. Ilki dizin gecisi (path traversal) saldirisidir,
    /// ikincisi calistirilabilir dosya gizlemedir.
    ///
    /// Uretilen bir ad kullanarak bu sinifin tamamini ortadan kaldiriyorum.
    /// Kullanicidan gelen HICBIR metin dosya yolunda kullanılmıyor.
    /// </summary>
    public string StoredFileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeInBytes { get; private set; }

    public string StoragePath { get; private set; }

    /// <summary>
    /// Bu dosya hangi kayda ait? Ornek: "Event", "OrganizerProfile".
    /// null ise henüz hiçbir kayda baglanmamis (temizlik adayi).
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
            throw new DomainException("Saklanan dosya adı boş olamaz.", "uploaded_file.name_required");
        }

        if (sizeInBytes <= 0)
        {
            throw new DomainException("Dosya boyutu sıfırdan büyük olmalıdır.", "uploaded_file.invalid_size");
        }

        return new UploadedFile
        {
            FileName = fileName,
            StoredFileName = storedFileName,
            ContentType = contentType,
            SizeInBytes = sizeInBytes,
            StoragePath = storagePath,
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

    /// <summary>Hicbir kayda bağlı değil mi? Temizlik job'i bunu sorar.</summary>
    public bool IsOrphan() => RelatedEntityId is null;
}
