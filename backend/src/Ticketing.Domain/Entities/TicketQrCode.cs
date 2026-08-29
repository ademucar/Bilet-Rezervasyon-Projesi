using System.Security.Cryptography;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Biletin QR kodu. PDF: "QR kod değeri benzersiz olmalıdır."
///
/// NEDEN AYRI TABLO? Ticket'a bir sutun eklesek olmaz miydi?
///
/// PDF'in ER diyagraminda ayrı bir tablo olarak isteniyor ve bunun
/// pratik gerekceleri var:
///
/// 1) QR üretimi bir OUTBOX isidir (PDF Sprint 9: "QR bilet oluşturma
///    islemi"). Ödeme transaction'i içinde QR gorseli uretmek istemem;
///    bu is yavastir ve kullanıcıyı bekletir. Bilet hemen olusur,
///    QR arkadan gelir. Ayrı tablo bu gecikmeyi dogal kilar --
///    Ticket var ama QrCode henüz null olabilir.
///
/// 2) QR yeniden uretilebilir olmalı (kullanıcı "QR'im calismiyor" derse).
///    Ayrı kayıt, eski QR'i geçersiz kilip yenisini uretmeyi kolaylastirir.
///
/// 3) Güvenlik: QR değeri hassas bir bilgidir (bilete erişim saglar).
///    Ayrı tabloda olmasını, bilet listesi sorgularinda kazara
///    donmesini engellememizi kolaylastirir.
/// </summary>
public class TicketQrCode : Entity
{
    private TicketQrCode() => QrValue = string.Empty;

    public Guid TicketId { get; private set; }

    /// <summary>
    /// QR icine gomulecek benzersiz deger.
    ///
    /// KRIPTOGRAFIK olarak güvenli uretiliyor (RandomNumberGenerator).
    /// Bu sart: tahmin edilebilir bir QR değeri, saldirganin geçerli
    /// bilet uretebilmesi demektir. Girist gorevlisi sahte QR'i
    /// gerçekten ayırt edemez.
    ///
    /// 32 byte = 256 bit entropi. Kaba kuvvetle tahmin edilmesi
    /// pratikte imkansiz.
    /// </summary>
    public string QrValue { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>
    /// QR gorselinin depolama yolu. Üretim arka planda yapilacagi için
    /// bir süre null kalabilir.
    /// </summary>
    public string? ImagePath { get; private set; }

    /// <summary>
    /// QR geçersiz kilindi mi? Yeniden üretim durumunda eskisi iptal edilir.
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
    /// Base64Url kullanıyorum, duz Base64 değil.
    ///
    /// Duz Base64'te '+', '/' ve '=' karakterleri bulunur. Bunlar URL'de
    /// ozel anlam tasir ve kacis (escaping) gerektirir. QR degerini bir
    /// doğrulama linkine koyacaksak ("/api/tickets/verify?code=...")
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
    /// QR'i geçersiz kilar. Yeni bir QR uretilmeden önce cagrilir.
    /// </summary>
    public void Revoke() => IsRevoked = true;

    /// <summary>
    /// Bu QR girişte kabul edilebilir mi?
    /// Sadece QR'in kendi durumuna bakar; biletin durumu ayrıca
    /// kontrol edilmelidir (Ticket.Status == Active).
    /// </summary>
    public bool IsValid() => !IsRevoked;
}
