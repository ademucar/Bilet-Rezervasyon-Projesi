using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ticketing.WebApi.Hubs;

/// <summary>
/// Gerçek zamanlı koltuk guncellemeleri. PDF Sprint 10.
///
/// NEDEN [AllowAnonymous]?
///
/// Bu karari uzun dusundum, çünkü ilk refleks "her seyi kilitle" oluyor.
///
/// 1) TUTARLILIK: GET /event-sessions/{id}/seat-availability zaten
///    [AllowAnonymous]. Gerekcesi Sprint 7'de yazilmisti: kullanıcı
///    bilet almadan önce hangi koltuklarin boş olduğunu gorebilmeli.
///
///    Bu hub'in yaydigi olaylar o uc noktanin dondugu bilginin
///    AYNISINI tasiyor -- koltuk kimliği ve durumu, başka hiçbir sey.
///    Sorguya açık olan bilgiyi canlı yayında kapatmak tutarsiz
///    olurdu ve hiçbir sey korumazdi.
///
/// 2) TOKEN'I ADRESE KOYMAK ISTEMEDIM: SignalR WebSocket kullaninca
///    tarayıcı Authorization BASLIGI GONDEREMEZ. Standart çözüm
///    token'i sorgu dizesine koymaktir:
///
///        /hubs/seats?access_token=eyJhbGciOi...
///
///    Ama URL'ler her yere yazilir: sunucu erişim loglari, ters vekil
///    sunucu loglari, tarayıcı gecmisi, Referer başlığı. Yani token
///    onlarca yerde duz metin olarak birikir.
///
///    Korunacak bir sey olsaydı bu bedeli oderdik. Burada yok.
///
/// KIMLIK GEREKSEYDI NE YAPARDIK? Sprint 15'te bildirim hub'i
/// eklendiginde (kullanıcıya OZEL veri tasiyacak) orada kimlik ŞART
/// olacak ve token sorgu dizesi cozumunu, loglardan token'i maskeleyen
/// bir yapilandirmayla birlikte kuracagiz.
/// </summary>
[AllowAnonymous]
public sealed class SeatHub : Hub
{
    /// <summary>Bir oturumun grup adı. Tek yerde uretiliyor.</summary>
    /// <remarks>
    /// Grup adını hem hub hem de bildirim gonderen sinif uretiyor.
    /// Iki yerde elle yazsaydım birinde yazım hatası yapmak,
    /// mesajlarin HİÇ ULASMAMASINA yol acardi -- ve hiçbir hata
    /// vermezdi, çünkü SignalR var olmayan bir gruba gondermeyi
    /// hata saymaz. Sessizce çalışmayan bir sistem, patlayan
    /// sistemden çok daha zor teshis edilir.
    /// </remarks>
    public static string GroupNameFor(Guid eventSessionId)
        => $"session-{eventSessionId}";

    /// <summary>
    /// Istemciyi bir oturumun grubuna alır.
    /// </summary>
    /// <remarks>
    /// PDF IS KURALI: "Kullanıcı yalnızca goruntuledigi etkinlik
    /// oturumunun grubuna katilmalidir."
    ///
    /// Neden bu kural var? Grup olmasaydı tek seçenek TÜM istemcilere
    /// yayin yapmak olurdu (Clients.All).
    ///
    /// Somut sonucu: 50 farklı etkinlik satışta ve 10.000 kişi
    /// bagliyken, bir koltuğun kilitlenmesi 10.000 kisiye mesaj
    /// gonderirdi. Bunlarin 9.800'u o etkinlige bakmiyor bile.
    ///
    /// Grup ile yalnızca o oturumu izleyen 200 kisiye gidiyor.
    /// Fark 50 kat.
    ///
    /// ONCE ESKİ GRUPTAN CIKIYORUZ: kullanıcı oturumlar arasında
    /// gezinirse (A oturumu -> B oturumu) eski gruptan cikmazsa
    /// artık bakmadigi oturumun mesajlarini almaya devam ederdi.
    /// Zamanla bir istemci onlarca gruba uye olurdu.
    /// </remarks>
    public async Task JoinSession(Guid eventSessionId)
    {
        // Boş Guid'i reddediyorum.
        //
        // Olmasaydı "session-00000000-0000-0000-0000-000000000000"
        // diye bir cop grup olusur ve hiçbir mesaj almazdi.
        // Istemcideki bir hatanin sessizce yutulmasi yerine
        // acikca soylenmesi daha iyi.
        if (eventSessionId == Guid.Empty)
        {
            throw new HubException("Geçerli bir oturum kimliği gonderilmelidir.");
        }

        var previous = CurrentSession;

        if (previous.HasValue && previous.Value != eventSessionId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId, GroupNameFor(previous.Value)).ConfigureAwait(false);
        }

        // Bagli olduğu oturumu bağlantı uzerinde sakliyorum.
        //
        // Context.Items, bu BAGLANTIYA ozel bir sozluk. Statik bir
        // sozluk kullansaydım iki sorun çıkardı: es zamanlı erişim
        // kilitleme gerektirirdi ve bağlantı kapandiginda kayıt
        // silinmezse bellek sizardi.
        Context.Items["EventSessionId"] = eventSessionId;

        await Groups.AddToGroupAsync(
            Context.ConnectionId, GroupNameFor(eventSessionId)).ConfigureAwait(false);
    }

    /// <summary>Gruptan çıkar. Istemci sayfadan ayrilirken cagirir.</summary>
    public async Task LeaveSession(Guid eventSessionId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId, GroupNameFor(eventSessionId)).ConfigureAwait(false);

        Context.Items.Remove("EventSessionId");
    }

    /// <summary>
    /// Bağlantı kesildiginde temizlik.
    /// </summary>
    /// <remarks>
    /// SignalR bağlantı kopunca grup uyeliklerini ZATEN temizler;
    /// bu metot grubu elle silmiyor.
    ///
    /// Burada Context.Items'i temizliyorum. Aslinda o da baglantiyla
    /// birlikte gidiyor -- ama acikca yazmak, ilerde bu sozluge
    /// başka bir sey konuldugunda temizligin unutulmamasi için
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
