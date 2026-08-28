namespace Ticketing.Application.Common.Logging;

/// <summary>
/// Tum log olay kimliklerinin (EventId) tek kaynagi.
/// PDF Sprint 16.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN MERKEZI BIR KAYIT?
/// ==================================================================
/// EventId'leri her dosyada elle yazsaydik iki sey olurdu:
///
///   1) CAKISMA. Iki farkli olay ayni numarayi alir ve izleme
///      sisteminde "9101 alarmi" dedigimizde hangisi oldugu belli
///      olmaz. Bu, alarm kurallarini sessizce yanlis yapar.
///
///   2) BOSLUK. "Hangi numaralar bos?" sorusunu cevaplamak icin tum
///      kod tabanini taramak gerekir; kimse yapmaz ve numaralar
///      rastgele secilir.
///
/// Numara BLOKLARI kullaniyorum ki bir alarm kuralini aralik olarak
/// yazabilelim: "1000-1999 arasi = kimlik olaylari".
///
/// ------------------------------------------------------------------
/// BLOK HARITASI
/// ------------------------------------------------------------------
///   1000-1099   Kimlik dogrulama    (bu dosya)
///   1100-1199   Etkinlik yasam dongusu
///   1200-1299   Rezervasyon ve koltuk
///   1300-1399   Odeme ve iade
///   4000-4999   Istemci hatalari    (GlobalExceptionHandler)
///   5000-5999   Sunucu hatalari     (GlobalExceptionHandler)
///   9001-9099   Outbox
///   9101-9199   Arka plan isleri
///   9201-9299   SignalR
///   9301-9399   Onbellek
///   9401-9499   E-posta
/// ------------------------------------------------------------------
///
/// 4000/5000 ve 9xxx bloklari Sprint 9-15 arasinda zaten kullaniliyordu;
/// onlari TASIMADIM. Calisan bir sistemde EventId degistirmek, o
/// numaraya bagli her alarm kuralini ve kayitli her sorguyu sessizce
/// bozar. Yeni olaylar icin bos bloklari kullaniyorum.
/// </remarks>
public static class LogEvents
{
    // ==============================================================
    // KIMLIK DOGRULAMA -- PDF: "Login", "Basarisiz login"
    // ==============================================================

    /// <summary>Basarili giris.</summary>
    public const int LoginBasarili = 1001;

    /// <summary>
    /// Basarisiz giris.
    /// </summary>
    /// <remarks>
    /// Bu, guvenlik acisindan EN DEGERLI log satirimiz: brute force
    /// ve credential stuffing saldirilari burada gorunur.
    ///
    /// Warning seviyesinde logluyorum, Information degil. Sebep:
    /// izleme sisteminde "son 5 dakikada 100 kez 1002" gibi bir alarm
    /// kurali kurulabilsin. Information seviyesi cogu uretim
    /// ortaminda filtrelenir ve alarm hic tetiklenmez.
    /// </remarks>
    public const int LoginBasarisiz = 1002;

    /// <summary>Hesap kilitlendi (arka arkaya yanlis deneme).</summary>
    public const int HesapKilitlendi = 1003;

    /// <summary>Yeni kullanici kaydi.</summary>
    public const int KullaniciKaydi = 1004;

    // ==============================================================
    // ETKINLIK -- PDF: "Etkinlik olusturma", "Etkinlik yayinlama"
    // ==============================================================

    /// <summary>Etkinlik olusturuldu (taslak).</summary>
    public const int EtkinlikOlusturuldu = 1101;

    /// <summary>
    /// Etkinlik yayinlandi.
    /// </summary>
    /// <remarks>
    /// Bu olay AYRI loglaniyor cunku is acisindan donusu olmayan bir
    /// esik: yayinlandigi anda etkinlik herkese gorunur olur ve bilet
    /// satisi baslar. "Kim, ne zaman yayinladi?" sorusu bir denetim
    /// sorusudur.
    /// </remarks>
    public const int EtkinlikYayinlandi = 1102;

    /// <summary>Etkinlik iptal edildi.</summary>
    public const int EtkinlikIptalEdildi = 1103;

    /// <summary>Etkinlik guncellendi.</summary>
    public const int EtkinlikGuncellendi = 1104;

    /// <summary>
    /// Etkinlik silindi (soft delete).
    /// </summary>
    /// <remarks>
    /// Warning seviyesinde loglaniyor: silme geri alinmasi zor bir
    /// islem ve denetimde gorulmesi gerekiyor. Iade ve iptal icin
    /// verdigimiz kararin aynisi (Sprint 16).
    /// </remarks>
    public const int EtkinlikSilindi = 1105;

    // ==============================================================
    // REZERVASYON -- PDF: "Rezervasyon olusturma", "Koltuk kilitleme"
    // ==============================================================

    /// <summary>Rezervasyon olusturuldu.</summary>
    public const int RezervasyonOlusturuldu = 1201;

    /// <summary>
    /// Koltuklar kilitlendi.
    /// </summary>
    /// <remarks>
    /// PDF bunu rezervasyondan AYRI bir madde olarak istiyor ve
    /// hakli: koltuk kilitleme, projedeki en yogun yaris kosulunun
    /// (race condition) yasandigi nokta.
    ///
    /// Iki kullanici ayni koltugu istediginde biri 409 aliyor. O anin
    /// logu olmadan "kullanici koltugu alamadi" sikayetini
    /// arastirmak imkansiz olurdu.
    /// </remarks>
    public const int KoltuklarKilitlendi = 1202;

    /// <summary>Koltuk kilitleme cakismasi (baskasi once aldi).</summary>
    public const int KoltukCakismasi = 1203;

    /// <summary>Rezervasyon iptal edildi.</summary>
    public const int RezervasyonIptalEdildi = 1204;

    // ==============================================================
    // ODEME -- PDF: "Odeme", "Iade"
    // ==============================================================

    /// <summary>Odeme baslatildi.</summary>
    public const int OdemeBaslatildi = 1301;

    /// <summary>Odeme basarili, biletler uretildi.</summary>
    public const int OdemeBasarili = 1302;

    /// <summary>Odeme basarisiz.</summary>
    public const int OdemeBasarisiz = 1303;

    /// <summary>
    /// Iade yapildi.
    /// </summary>
    /// <remarks>
    /// Para HAREKETI iceren tek islemimiz. Information degil,
    /// Warning seviyesinde logluyorum -- hata oldugu icin degil,
    /// GORULMESI gerektigi icin: iade hacminde ani bir artis ya bir
    /// hata ya da bir kotuye kullanim isaretidir.
    /// </remarks>
    public const int IadeYapildi = 1304;
}
