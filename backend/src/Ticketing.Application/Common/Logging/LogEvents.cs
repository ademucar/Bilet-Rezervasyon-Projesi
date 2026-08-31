namespace Ticketing.Application.Common.Logging;

/// <summary>
/// Tüm log olay kimliklerinin (EventId) tek kaynagi.
/// PDF Sprint 16.
/// </summary>
/// <remarks>
/// Neden merkezi bir kayit?
///
/// EventId'leri her dosyada elle yazsaydım iki sey olurdu:
///
///   1) CAKISMA. Iki farklı olay aynı numarayi alır ve izleme
///      sisteminde "9101 alarmi" dedigimde hangisi olduğu belli
///      olmaz. Bu, alarm kurallarini sessizce yanlış yapar.
///
///   2) BOSLUK. "Hangi numaralar boş?" sorusunu cevaplamak için tüm
///      kod tabanini taramak gerekir; kimse yapmaz ve numaralar
///      rastgele secilir.
///
/// Numara BLOKLARI kullanıyorum ki bir alarm kuralini aralık olarak
/// yazabileyim: "1000-1999 arasi = kimlik olaylari".
///
/// Blok haritasi
///
///   1000-1099   Kimlik doğrulama    (bu dosya)
///   1100-1199   Etkinlik yasam dongusu
///   1200-1299   Rezervasyon ve koltuk
///   1300-1399   Ödeme ve iade
///   4000-4999   Istemci hatalari    (GlobalExceptionHandler)
///   5000-5999   Sunucu hatalari     (GlobalExceptionHandler)
///   9001-9099   Outbox
///   9101-9199   Arka plan isleri
///   9201-9299   SignalR
///   9301-9399   Onbellek
///   9401-9499   E-posta
///
/// 4000/5000 ve 9xxx bloklari Sprint 9-15 arasında zaten kullaniliyordu;
/// onlari TASIMADIM. Calisan bir sistemde EventId degistirmek, o
/// numaraya bağlı her alarm kuralini ve kayıtlı her sorguyu sessizce
/// bozar. Yeni olaylar için boş bloklari kullanıyorum.
/// </remarks>
public static class LogEvents
{
    // kimlik dogrulama -- PDF: "Login", "Başarısız login"

    /// <summary>Başarılı giriş.</summary>
    public const int LoginBasarili = 1001;

    /// <summary>
    /// Başarısız giriş.
    /// </summary>
    /// <remarks>
    /// Bu, güvenlik acisindan en degerli log satirim: brute force
    /// ve credential stuffing saldirilari burada görünür.
    ///
    /// Warning seviyesinde logluyorum, Information değil. Sebep:
    /// izleme sisteminde "son 5 dakikada 100 kez 1002" gibi bir alarm
    /// kuralı kurulabilsin. Information seviyesi çoğu üretim
    /// ortaminda filtrelenir ve alarm hiç tetiklenmez.
    /// </remarks>
    public const int LoginBasarisiz = 1002;

    /// <summary>Hesap kilitlendi (arka arkaya yanlış deneme).</summary>
    public const int HesapKilitlendi = 1003;

    /// <summary>Yeni kullanıcı kaydı.</summary>
    public const int KullaniciKaydi = 1004;

    // ETKİNLİK -- PDF: "Etkinlik oluşturma", "Etkinlik yayinlama"

    /// <summary>Etkinlik oluşturuldu (taslak).</summary>
    public const int EtkinlikOlusturuldu = 1101;

    /// <summary>
    /// Etkinlik yayinlandi.
    /// </summary>
    /// <remarks>
    /// Bu olay AYRI loglaniyor çünkü is acisindan donusu olmayan bir
    /// esik: yayinlandigi anda etkinlik herkese görünür olur ve bilet
    /// satışı başlar. "Kim, ne zaman yayinladi?" sorusu bir denetim
    /// sorusudur.
    /// </remarks>
    public const int EtkinlikYayinlandi = 1102;

    /// <summary>Etkinlik iptal edildi.</summary>
    public const int EtkinlikIptalEdildi = 1103;

    // TUZAK: mesaj sablonunda {EventId} yer tutucusu KULLANMAYIN.
    //
    // Askiya alma ozelligini yazarken fark ettim: bu dosyadaki
    // etkinlik olaylarinin HICBIRI loglara dusmuyordu. Ne
    // "yayinlandi" ne "iptal edildi" -- 19 sprint boyunca tek satir
    // yazilmamis.
    //
    // Sebep: sablonlar "Id: {EventId}" diye yaziliyordu. Serilog'un
    // Microsoft.Extensions.Logging koprusu, her kayda MEL'in kendi
    // olay kimligini "EventId" adiyla zaten ekliyor. Ayni adda ikinci
    // bir alan gelince cakisma oluyor ve Serilog kaydi sessizce
    // dusuruyor -- hata vermeden, uyarmadan.
    //
    // Sessiz olmasi isin en kotu tarafi: kod dogru gorunuyor, derleme
    // temiz, testler yesil, uc 204 donuyor. Yalnizca log dosyasina
    // bakip "burada bir sey olmasi gerekmiyor muydu?" diye soran biri
    // fark edebilir.
    //
    // Cozum: yer tutucuyu {EtkinlikId} yaptim. Ayni sebeple
    // {SourceContext}, {Timestamp}, {Level}, {Message}, {Exception}
    // adlari da kullanilmamali -- hepsi Serilog'un ayirdigi adlar.

    /// <summary>Etkinlik güncellendi.</summary>
    public const int EtkinlikGuncellendi = 1104;

    /// <summary>
    /// Etkinlik silindi (soft delete).
    /// </summary>
    /// <remarks>
    /// Warning seviyesinde loglaniyor: silme geri alinmasi zor bir
    /// işlem ve denetimde gorulmesi gerekiyor. İade ve iptal için
    /// verdigim kararin aynisi (Sprint 16).
    /// </remarks>
    public const int EtkinlikSilindi = 1105;

    /// <summary>
    /// Admin etkinligi uygunsuz bulup askiya aldi.
    /// </summary>
    /// <remarks>
    /// Warning seviyesinde. Bunu bilerek yayinlama (Information) ile
    /// ayni seviyede tutmadim: askiya alma, bir BASKASININ isini
    /// durduran tek tarafli bir mudahale. "Kim, ne zaman, neden
    /// askiya aldi?" sorusunun cevabi loglarda kolay bulunabilmeli --
    /// cunku bu sorunun sorulacagi an, genellikle organizatorun itiraz
    /// ettigi andir.
    /// </remarks>
    public const int EtkinlikAskiyaAlindi = 1106;

    /// <summary>Askiya alinan etkinlik yayina geri alindi.</summary>
    public const int EtkinlikAskidanCikarildi = 1107;

    // REZERVASYON -- PDF: "Rezervasyon oluşturma", "Koltuk kilitleme"

    /// <summary>Rezervasyon oluşturuldu.</summary>
    public const int RezervasyonOlusturuldu = 1201;

    /// <summary>
    /// Koltuklar kilitlendi.
    /// </summary>
    /// <remarks>
    /// PDF bunu rezervasyondan AYRI bir madde olarak istiyor ve
    /// haklı: koltuk kilitleme, projedeki en yogun yaris kosulunun
    /// (race condition) yasandigi nokta.
    ///
    /// Iki kullanıcı aynı koltuğu istediginde biri 409 aliyor. O anin
    /// logu olmadan "kullanıcı koltuğu alamadi" sikayetini
    /// arastirmak imkansiz olurdu.
    /// </remarks>
    public const int KoltuklarKilitlendi = 1202;

    /// <summary>Koltuk kilitleme çakışması (başkası önce aldi).</summary>
    public const int KoltukCakismasi = 1203;

    /// <summary>Rezervasyon iptal edildi.</summary>
    public const int RezervasyonIptalEdildi = 1204;

    /// <summary>
    /// Kullanici kendi biletini iptal etti.
    /// </summary>
    /// <remarks>
    /// Information degil Warning: bu islem para hareketi yaratiyor ve
    /// koltugu tekrar satisa aciyor. Iade ve etkinlik iptali icin
    /// verdigim kararin aynisi -- is etkisi olan olaylar normal
    /// trafigin arasinda kaybolmamali.
    ///
    /// Iade YUZDESINI de logluyorum. Sebebi somut: musteri "ben tam
    /// iade bekliyordum" diye sikayet ettiginde, o an politikanin ne
    /// dedigini gosteren tek kayit bu olacak.
    /// </remarks>
    public const int BiletIptalEdildi = 1205;

    // KULLANICI YONETIMI (admin) -- 1000 blogunda, cunku kimlik ve
    // yetkiyle ilgili. Ikisi de Warning: bir hesabi kapatmak veya rol
    // vermek, o kisinin sistemde yapabileceklerini degistiriyor.

    /// <summary>Admin bir hesabi aktif/pasif yapti.</summary>
    public const int KullaniciDurumuDegisti = 1010;

    /// <summary>Admin bir kullanicinin rolunu degistirdi.</summary>
    public const int KullaniciRoluDegisti = 1011;

    // ÖDEME -- PDF: "Ödeme", "İade"

    /// <summary>Ödeme baslatildi.</summary>
    public const int OdemeBaslatildi = 1301;

    /// <summary>Ödeme başarılı, biletler üretildi.</summary>
    public const int OdemeBasarili = 1302;

    /// <summary>Ödeme başarısız.</summary>
    public const int OdemeBasarisiz = 1303;

    /// <summary>
    /// İade yapıldı.
    /// </summary>
    /// <remarks>
    /// Para HAREKETI iceren tek islemim. Information değil,
    /// Warning seviyesinde logluyorum -- hata olduğu için değil,
    /// GORULMESI gerektigi için: iade hacminde ani bir artis ya bir
    /// hata ya da bir kotuye kullanim isaretidir.
    /// </remarks>
    public const int IadeYapildi = 1304;
}
