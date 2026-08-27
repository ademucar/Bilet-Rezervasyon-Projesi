using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ticketing.WebApi.Hubs;

/// <summary>
/// Gercek zamanli koltuk guncellemeleri. PDF Sprint 10.
///
/// ==================================================================
/// NEDEN [AllowAnonymous]?
/// ==================================================================
/// Bu karari uzun dusundum, cunku ilk refleks "her seyi kilitle" oluyor.
///
/// 1) TUTARLILIK: GET /event-sessions/{id}/seat-availability zaten
///    [AllowAnonymous]. Gerekcesi Sprint 7'de yazilmisti: kullanici
///    bilet almadan once hangi koltuklarin bos oldugunu gorebilmeli.
///
///    Bu hub'in yaydigi olaylar o uc noktanin dondugu bilginin
///    AYNISINI tasiyor -- koltuk kimligi ve durumu, baska hicbir sey.
///    Sorguya acik olan bilgiyi canli yayinda kapatmak tutarsiz
///    olurdu ve hicbir sey korumazdi.
///
/// 2) TOKEN'I ADRESE KOYMAK ISTEMEDIM: SignalR WebSocket kullaninca
///    tarayici Authorization BASLIGI GONDEREMEZ. Standart cozum
///    token'i sorgu dizesine koymaktir:
///
///        /hubs/seats?access_token=eyJhbGciOi...
///
///    Ama URL'ler her yere yazilir: sunucu erisim loglari, ters vekil
///    sunucu loglari, tarayici gecmisi, Referer basligi. Yani token
///    onlarca yerde duz metin olarak birikir.
///
///    Korunacak bir sey olsaydi bu bedeli oderdik. Burada yok.
///
/// KIMLIK GEREKSEYDI NE YAPARDIK? Sprint 15'te bildirim hub'i
/// eklendiginde (kullaniciya OZEL veri tasiyacak) orada kimlik SART
/// olacak ve token sorgu dizesi cozumunu, loglardan token'i maskeleyen
/// bir yapilandirmayla birlikte kuracagiz.
/// ==================================================================
/// </summary>
[AllowAnonymous]
public sealed class SeatHub : Hub
{
    /// <summary>Bir oturumun grup adi. Tek yerde uretiliyor.</summary>
    /// <remarks>
    /// Grup adini hem hub hem de bildirim gonderen sinif uretiyor.
    /// Iki yerde elle yazsaydik birinde yazim hatasi yapmak,
    /// mesajlarin HIC ULASMAMASINA yol acardi -- ve hicbir hata
    /// vermezdi, cunku SignalR var olmayan bir gruba gondermeyi
    /// hata saymaz. Sessizce calismayan bir sistem, patlayan
    /// sistemden cok daha zor teshis edilir.
    /// </remarks>
    public static string GroupNameFor(Guid eventSessionId)
        => $"session-{eventSessionId}";

    /// <summary>
    /// Istemciyi bir oturumun grubuna alir.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// PDF IS KURALI: "Kullanici yalnizca goruntuledigi etkinlik
    /// oturumunun grubuna katilmalidir."
    /// ==============================================================
    /// Neden bu kural var? Grup olmasaydi tek secenek TUM istemcilere
    /// yayin yapmak olurdu (Clients.All).
    ///
    /// Somut sonucu: 50 farkli etkinlik satista ve 10.000 kisi
    /// bagliyken, bir koltugun kilitlenmesi 10.000 kisiye mesaj
    /// gonderirdi. Bunlarin 9.800'u o etkinlige bakmiyor bile.
    ///
    /// Grup ile yalnizca o oturumu izleyen 200 kisiye gidiyor.
    /// Fark 50 kat.
    ///
    /// ONCE ESKI GRUPTAN CIKIYORUZ: kullanici oturumlar arasinda
    /// gezinirse (A oturumu -> B oturumu) eski gruptan cikmazsa
    /// artik bakmadigi oturumun mesajlarini almaya devam ederdi.
    /// Zamanla bir istemci onlarca gruba uye olurdu.
    /// ==============================================================
    /// </remarks>
    public async Task JoinSession(Guid eventSessionId)
    {
        // Bos Guid'i reddediyorum.
        //
        // Olmasaydi "session-00000000-0000-0000-0000-000000000000"
        // diye bir cop grup olusur ve hicbir mesaj almazdi.
        // Istemcideki bir hatanin sessizce yutulmasi yerine
        // acikca soylenmesi daha iyi.
        if (eventSessionId == Guid.Empty)
        {
            throw new HubException("Gecerli bir oturum kimligi gonderilmelidir.");
        }

        var previous = CurrentSession;

        if (previous.HasValue && previous.Value != eventSessionId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId, GroupNameFor(previous.Value)).ConfigureAwait(false);
        }

        // Bagli oldugu oturumu baglanti uzerinde sakliyorum.
        //
        // Context.Items, bu BAGLANTIYA ozel bir sozluk. Statik bir
        // sozluk kullansaydik iki sorun cikardi: es zamanli erisim
        // kilitleme gerektirirdi ve baglanti kapandiginda kayit
        // silinmezse bellek sizardi.
        Context.Items["EventSessionId"] = eventSessionId;

        await Groups.AddToGroupAsync(
            Context.ConnectionId, GroupNameFor(eventSessionId)).ConfigureAwait(false);
    }

    /// <summary>Gruptan cikar. Istemci sayfadan ayrilirken cagirir.</summary>
    public async Task LeaveSession(Guid eventSessionId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId, GroupNameFor(eventSessionId)).ConfigureAwait(false);

        Context.Items.Remove("EventSessionId");
    }

    /// <summary>
    /// Baglanti kesildiginde temizlik.
    /// </summary>
    /// <remarks>
    /// SignalR baglanti kopunca grup uyeliklerini ZATEN temizler;
    /// bu metot grubu elle silmiyor.
    ///
    /// Burada Context.Items'i temizliyorum. Aslinda o da baglantiyla
    /// birlikte gidiyor -- ama acikca yazmak, ilerde bu sozluge
    /// baska bir sey konuldugunda temizligin unutulmamasi icin
    /// bir hatirlatma.
    /// </remarks>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Context.Items.Remove("EventSessionId");

        return base.OnDisconnectedAsync(exception);
    }

    private Guid? CurrentSession
        => Context.Items.TryGetValue("EventSessionId", out var value) && value is Guid id
            ? id
            : null;
}
