using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// Outbox mesaj davranisi. PDF Sprint 9 "Is Kurallari" bolumundeki
/// dort maddenin her biri icin en az bir test var.
/// </summary>
public class OutboxMessageTests
{
    private static readonly DateTimeOffset Simdi =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private const int MaxDeneme = 5;

    private static OutboxMessage YeniMesaj()
        => OutboxMessage.Create("TestMesaji", """{"a":1}""");

    // ===============================================================
    // OLUSTURMA
    // ===============================================================

    [Fact]
    public void Create_YeniMesaj_IslenmemisOlmali()
    {
        var mesaj = YeniMesaj();

        mesaj.ProcessedAt.Should().BeNull();
        mesaj.RetryCount.Should().Be(0);
        mesaj.IsDeadLettered.Should().BeFalse();
        mesaj.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BosTur_Reddedilmeli(string tur)
    {
        var eylem = () => OutboxMessage.Create(tur, "{}");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("outbox.type_required");
    }

    [Fact]
    public void Create_BosPayload_Reddedilmeli()
    {
        var eylem = () => OutboxMessage.Create("Tur", "  ");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("outbox.payload_required");
    }

    // ===============================================================
    // PDF: "Ayni Outbox kaydi iki kez islenmemelidir."
    // ===============================================================

    [Fact]
    public void MarkAsProcessed_IslenmisMesaj_IlkZamaniKorumali()
    {
        var mesaj = YeniMesaj();
        mesaj.MarkAsProcessed(Simdi);

        // Ikinci callback / ikinci job turu.
        mesaj.MarkAsProcessed(Simdi.AddMinutes(5));

        // ILK islenme zamani korunmali.
        //
        // Uzerine yazsaydik denetim izi bozulurdu: "bu mesaj ne zaman
        // islendi?" sorusunun cevabi her tekrar denemede degisirdi.
        mesaj.ProcessedAt.Should().Be(Simdi);
    }

    [Fact]
    public void IsReadyToProcess_IslenmisMesaj_False()
    {
        var mesaj = YeniMesaj();
        mesaj.MarkAsProcessed(Simdi);

        mesaj.IsReadyToProcess(Simdi.AddHours(1)).Should().BeFalse();
    }

    // ===============================================================
    // PDF: "Basarisiz islem yeniden denenmelidir."
    // ===============================================================

    [Fact]
    public void MarkAsFailed_IlkHata_YenidenDenemeIcinPlanlanmali()
    {
        var mesaj = YeniMesaj();

        mesaj.MarkAsFailed("SMTP baglanti hatasi", MaxDeneme, Simdi);

        mesaj.RetryCount.Should().Be(1);
        mesaj.IsDeadLettered.Should().BeFalse();
        mesaj.ErrorMessage.Should().Be("SMTP baglanti hatasi");

        // 2^1 = 2 dakika sonra.
        mesaj.NextRetryAt.Should().Be(Simdi.AddMinutes(2));
    }

    [Fact]
    public void IsReadyToProcess_BeklemeSuresiDolmadan_False()
    {
        var mesaj = YeniMesaj();
        mesaj.MarkAsFailed("hata", MaxDeneme, Simdi);

        // 2 dakika beklemesi gerekiyor; 1 dakika sonra hazir OLMAMALI.
        mesaj.IsReadyToProcess(Simdi.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void IsReadyToProcess_BeklemeSuresiDolunca_True()
    {
        var mesaj = YeniMesaj();
        mesaj.MarkAsFailed("hata", MaxDeneme, Simdi);

        mesaj.IsReadyToProcess(Simdi.AddMinutes(2)).Should().BeTrue();
    }

    [Fact]
    public void MarkAsFailed_ArtanHatalar_BeklemeSuresiUstelArtmali()
    {
        var mesaj = YeniMesaj();

        // 1. hata -> 2 dk
        mesaj.MarkAsFailed("hata", MaxDeneme, Simdi);
        mesaj.NextRetryAt.Should().Be(Simdi.AddMinutes(2));

        // 2. hata -> 4 dk
        mesaj.MarkAsFailed("hata", MaxDeneme, Simdi);
        mesaj.NextRetryAt.Should().Be(Simdi.AddMinutes(4));

        // 3. hata -> 8 dk
        mesaj.MarkAsFailed("hata", MaxDeneme, Simdi);
        mesaj.NextRetryAt.Should().Be(Simdi.AddMinutes(8));

        // Ustel artis, cokmus bir dis servise nefes aldiriyor.
        // Sabit araliklarla denemek servisi daha da yorardi.
    }

    [Fact]
    public void MarkAsFailed_BeklemeSuresi_UstSinirAsmamali()
    {
        var mesaj = YeniMesaj();

        // Yuksek bir esik veriyorum ki dead letter'a dusmeden
        // cok sayida deneme yapabileyim.
        for (var i = 0; i < 10; i++)
        {
            mesaj.MarkAsFailed("hata", maxRetries: 100, Simdi);
        }

        // 2^10 = 1024 dakika (17 saat) olurdu; ust sinir 60 dakika.
        //
        // Sinir olmasaydi gecici bir kesintiden sonra mesajlar
        // saatlerce beklerdi ve sistem duzelmesine ragmen
        // bildirimler gitmezdi.
        mesaj.NextRetryAt.Should().Be(Simdi.AddMinutes(60));
    }

    // ===============================================================
    // PDF: "Belirli deneme sayisindan sonra hata kaydi olusturulmalidir."
    // ===============================================================

    [Fact]
    public void MarkAsFailed_EsigeUlasinca_DeadLetterOlmali()
    {
        var mesaj = YeniMesaj();

        for (var i = 0; i < MaxDeneme; i++)
        {
            mesaj.MarkAsFailed($"hata {i}", MaxDeneme, Simdi);
        }

        mesaj.IsDeadLettered.Should().BeTrue();
        mesaj.RetryCount.Should().Be(MaxDeneme);

        // Artik yeniden deneme PLANLANMAMALI.
        //
        // NextRetryAt dolu kalsaydi mesaj sonsuza kadar kuyrukta
        // gorunur ve her turda bosuna secilirdi.
        mesaj.NextRetryAt.Should().BeNull();

        // Hata mesaji KORUNMALI: dead letter'in tum degeri, neden
        // basarisiz oldugunun kayitli olmasinda.
        mesaj.ErrorMessage.Should().Be($"hata {MaxDeneme - 1}");
    }

    [Fact]
    public void IsReadyToProcess_DeadLetter_ArtikSecilmemeli()
    {
        var mesaj = YeniMesaj();

        for (var i = 0; i < MaxDeneme; i++)
        {
            mesaj.MarkAsFailed("hata", MaxDeneme, Simdi);
        }

        mesaj.IsReadyToProcess(Simdi.AddDays(1)).Should().BeFalse();
    }

    // ===============================================================
    // TOPARLANMA
    // ===============================================================

    [Fact]
    public void MarkAsProcessed_HatadanSonraBasarili_HataTemizlenmeli()
    {
        var mesaj = YeniMesaj();
        mesaj.MarkAsFailed("gecici hata", MaxDeneme, Simdi);

        mesaj.MarkAsProcessed(Simdi.AddMinutes(3));

        mesaj.ProcessedAt.Should().Be(Simdi.AddMinutes(3));

        // Basarili olunca eski hata metni SILINMELI.
        //
        // Kalsaydi izleme ekraninda basariyla islenmis mesajlar
        // hatali gorunur ve gercek sorunlari ararken yaniltirdi.
        mesaj.ErrorMessage.Should().BeNull();
        mesaj.NextRetryAt.Should().BeNull();

        // RetryCount KORUNUYOR: "bu mesaj 1 kez basarisiz oldu ama
        // sonunda gecti" bilgisi degerli.
        mesaj.RetryCount.Should().Be(1);
    }

    [Fact]
    public void IsReadyToProcess_YeniMesaj_HemenHazirOlmali()
    {
        YeniMesaj().IsReadyToProcess(Simdi).Should().BeTrue();
    }
}
