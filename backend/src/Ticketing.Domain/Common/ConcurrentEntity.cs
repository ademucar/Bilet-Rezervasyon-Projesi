namespace Ticketing.Domain.Common;

/// <summary>
/// Es zamanli guncelleme kontrolu gereken entity'ler bundan turer.
/// Bundan turecekler: EventSeat (en kritigi), Reservation, Payment, Event.
///
/// ------------------------------------------------------------------
/// OPTIMISTIC CONCURRENCY NASIL CALISIR?
/// ------------------------------------------------------------------
/// Senaryo: Ayse ve Mehmet ayni anda B-5-12 koltugunu almak istiyor.
///
///   t0  Ayse   satiri okur   -> Status=Available, RowVersion=100
///   t1  Mehmet satiri okur   -> Status=Available, RowVersion=100
///   t2  Ayse   UPDATE gonderir:
///           UPDATE "EventSeats" SET "Status"=Locked
///           WHERE "Id"=@id AND xmin=100          --> 1 satir etkilendi, BASARILI
///           (PostgreSQL satiri gunceller, xmin otomatik 101 olur)
///   t3  Mehmet UPDATE gonderir:
///           UPDATE "EventSeats" SET "Status"=Locked
///           WHERE "Id"=@id AND xmin=100          --> 0 satir etkilendi
///
/// EF Core "1 satir bekliyordum, 0 geldi" der ve DbUpdateConcurrencyException
/// firlatir. Mehmet'e 409 Conflict doneriz.
///
/// KILIT NOKTA: Mehmet'in istegi ASLA veri bozmadi. Kaybetti ama sessizce
/// Ayse'nin uzerine yazmadi. "Last write wins" davranisinin tam tersi.
///
/// ------------------------------------------------------------------
/// PostgreSQL'de "xmin" NEDIR?
/// ------------------------------------------------------------------
/// SQL Server'da "rowversion" diye bir veri tipi vardir. PostgreSQL'de yoktur.
/// AMA PostgreSQL'de her tablonun gizli bir "xmin" sistem sutunu vardir:
/// satiri en son degistiren transaction'in ID'sini tutar ve her UPDATE'te
/// otomatik olarak degisir. Yani bize bedava bir surum numarasi veriyor.
///
/// Persistence katmanindaki EF konfigurasyonunda soyle eslestirecegiz:
///
///     builder.Property(x =&gt; x.RowVersion)
///            .HasColumnName("xmin")
///            .HasColumnType("xid")
///            .ValueGeneratedOnAddOrUpdate()
///            .IsConcurrencyToken();
///
/// MALIYETI SIFIR: ekstra sutun yok, ekstra index yok, ekstra yazma yok.
/// PostgreSQL bu bilgiyi zaten tutuyor; biz sadece ondan faydalaniyoruz.
///
/// Neden uint? xmin 32 bit isaretsiz bir tamsayidir (xid tipi).
/// int kullansaydim 2 milyardan sonra negatife dusup eslesme bozulurdu.
/// </summary>
public abstract class ConcurrentEntity : AuditableEntity
{
    public uint RowVersion { get; set; }
}
