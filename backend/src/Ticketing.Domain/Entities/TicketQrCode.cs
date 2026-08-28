using System.Security.Cryptography;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Biletin QR kodu. PDF: "QR kod degeri benzersiz olmalidir."
///
/// ------------------------------------------------------------------
/// NEDEN AYRI TABLO? Ticket'a bir sutun eklesek olmaz miydi?
/// ------------------------------------------------------------------
/// PDF'in ER diyagraminda ayri bir tablo olarak isteniyor ve bunun
/// pratik gerekceleri var:
///
/// 1) QR uretimi bir OUTBOX isidir (PDF Sprint 9: "QR bilet olusturma
///    islemi"). Odeme transaction'i icinde QR gorseli uretmek istemeyiz;
///    bu is yavastir ve kullaniciyi bekletir. Bilet hemen olusur,
///    QR arkadan gelir. Ayri tablo bu gecikmeyi dogal kilar --
///    Ticket var ama QrCode henuz null olabilir.
///
/// 2) QR yeniden uretilebilir olmali (kullanici "QR'im calismiyor" derse).
///    Ayri kayit, eski QR'i gecersiz kilip yenisini uretmeyi kolaylastirir.
///
/// 3) Guvenlik: QR degeri hassas bir bilgidir (bilete erisim saglar).
///    Ayri tabloda olmasi, bilet listesi sorgularinda kazara
///    donmesini engellememizi kolaylastirir.
/// </summary>
public class TicketQrCode : Entity
{
    private TicketQrCode() => QrValue = string.Empty;

    public Guid TicketId { get; private set; }

    /// <summary>
    /// QR icine gomulecek benzersiz deger.
    ///
    /// KRIPTOGRAFIK olarak guvenli uretiliyor (RandomNumberGenerator).
    /// Bu sart: tahmin edilebilir bir QR degeri, saldirganin gecerli
    /// bilet uretebilmesi demektir. Girist gorevlisi sahte QR'i
    /// gercekten ayirt edemez.
    ///
    /// 32 byte = 256 bit entropi. Kaba kuvvetle tahmin edilmesi
    /// pratikte imkansiz.
    /// </summary>
    public string QrValue { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>
    /// QR gorselinin depolama yolu. Uretim arka planda yapilacagi icin
    /// bir sure null kalabilir.
    /// </summary>
    public string? ImagePath { get; private set; }

    /// <summary>
    /// QR gecersiz kilindi mi? Yeniden uretim durumunda eskisi iptal edilir.
    /// </summary>
    public bool IsRevoked { get; private set; }

    public Ticket Ticket { get; private set; } = null!;

    public static TicketQrCode Create(Guid ticketId, DateTimeOffset now)
        => new()
        {
            TicketId = ticketId,
            QrValue = GenerateSecureValue(),
            GeneratedAt = now,
        };

    /// <summary>
    /// Base64Url kullaniyorum, duz Base64 degil.
    ///
    /// Duz Base64'te '+', '/' ve '=' karakterleri bulunur. Bunlar URL'de
    /// ozel anlam tasir ve kacis (escaping) gerektirir. QR degerini bir
    /// dogrulama linkine koyacaksak ("/api/tickets/verify?code=...")
    /// bu karakterler sorun cikarir. Base64Url bu uc karakteri kullanmaz.
    /// </summary>
    private static string GenerateSecureValue()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
                  .TrimEnd('=')
                  .Replace('+', '-')
                  .Replace('/', '_');

    public void SetImagePath(string path) => ImagePath = path;

    /// <summary>
    /// QR'i gecersiz kilar. Yeni bir QR uretilmeden once cagrilir.
    /// </summary>
    public void Revoke() => IsRevoked = true;

    /// <summary>
    /// Bu QR giriste kabul edilebilir mi?
    /// Sadece QR'in kendi durumuna bakar; biletin durumu ayrica
    /// kontrol edilmelidir (Ticket.Status == Active).
    /// </summary>
    public bool IsValid() => !IsRevoked;
}
