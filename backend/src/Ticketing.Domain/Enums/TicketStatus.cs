namespace Ticketing.Domain.Enums;

/// <summary>Bilet durumu. PDF sayfa 6.</summary>
public enum TicketStatus
{
    /// <summary>Geçerli bilet.</summary>
    Active = 1,

    /// <summary>Etkinlik girisinde QR okutuldu. İade EDILEMEZ.</summary>
    Used = 2,

    /// <summary>İptal edildi, para iadesi yapilmadi.</summary>
    Cancelled = 3,

    /// <summary>İptal edildi ve para iadesi yapıldı.</summary>
    Refunded = 4,

    /// <summary>Etkinlik gecti, bilet kullanilmadi.</summary>
    Expired = 5,
}
