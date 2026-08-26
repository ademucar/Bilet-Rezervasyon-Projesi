namespace Ticketing.Domain.Enums;

/// <summary>Bilet durumu. PDF sayfa 6.</summary>
public enum TicketStatus
{
    /// <summary>Gecerli bilet.</summary>
    Active = 1,

    /// <summary>Etkinlik girisinde QR okutuldu. Iade EDILEMEZ.</summary>
    Used = 2,

    /// <summary>Iptal edildi, para iadesi yapilmadi.</summary>
    Cancelled = 3,

    /// <summary>Iptal edildi ve para iadesi yapildi.</summary>
    Refunded = 4,

    /// <summary>Etkinlik gecti, bilet kullanilmadi.</summary>
    Expired = 5
}
