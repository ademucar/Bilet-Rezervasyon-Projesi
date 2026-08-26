namespace Ticketing.Domain.Common;

/// <summary>
/// Tum entity'lerin ortak atasi. Kimlik (Id) ve domain event birikimi saglar.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Neden Guid, neden int degil?
    ///
    /// 1) Bilet ve rezervasyon ID'leri URL'de goruluyor. int olsaydi kullanici
    ///    /tickets/1234 adresini /tickets/1235 yapip baskasinin biletini
    ///    denemeye kalkardi. Yetkilendirme bunu zaten engelleyecek ama
    ///    savunma katmanlarini cogaltmak iyidir.
    ///
    /// 2) Guid'i veritabanina gitmeden UYGULAMA tarafinda uretebiliyorum.
    ///    Outbox pattern'de bu kritik: rezervasyonu kaydetmeden once ID'sini
    ///    bilip outbox mesajinin icine yazabiliyorum. int olsaydi once INSERT
    ///    yapip ID'yi geri okumam gerekirdi.
    ///
    /// Neden Guid.CreateVersion7(), klasik Guid.NewGuid() degil?
    ///
    /// Klasik GUID (v4) tamamen rastgeledir. Primary key olarak kullanildiginda
    /// yeni kayitlar B-tree index'in rastgele yerlerine girer, index surekli
    /// bolunur (page split) ve zamanla parcalanir. Yazma performansi duser.
    ///
    /// UUID v7'nin ilk 48 biti timestamp'tir, yani zaman siralidir. Yeni
    /// kayitlar hep index'in SONUNA eklenir -- tipki auto-increment gibi.
    /// Guid'in dagitik sistemlerde cakismama avantajini korur, performans
    /// sorununu cozer. .NET 9 ile geldi.
    /// </summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Bu entity uzerinde biriken, henuz yayinlanmamis olaylar.
    ///
    /// IReadOnlyCollection donuyorum, List degil. Boylece disaridan
    /// entity.DomainEvents.Add(...) yazilamaz. Olay ekleme yetkisi
    /// sadece entity'nin kendisindedir (asagidaki protected metot).
    /// Kapsullemeyi (encapsulation) bu sekilde koruyorum.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Olaylar yayinlandiktan sonra temizlenir.
    ///
    /// Cagrilma yeri: DbContext.SaveChangesAsync icinde, transaction commit
    /// edildikten SONRA. Once commit, sonra yayin -- cunku commit basarisiz
    /// olursa hic olmamis bir olayi duyurmus oluruz.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
