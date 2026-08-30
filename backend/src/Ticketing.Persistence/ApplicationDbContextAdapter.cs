namespace Ticketing.Persistence;

/// <summary>
/// TicketingDbContext'in IApplicationDbContext arayuzunu uyguladigini
/// belirten kismi (partial) bildirim.
///
/// Neden ayrı dosya? TicketingDbContext.cs "veritabani semasi" ile
/// ilgili; bu dosya ise "Application katmanina nasil gorunuyoruz"
/// sorusuyla ilgili. Iki farklı sorumluluk, iki farklı dosya.
///
/// Ayrıca: DbContext zaten tüm DbSet'leri ve SaveChangesAsync'i
/// sagladigi için ek bir kod yazmamiza gerek yok. Arayuzu bildirmek
/// yeterli -- C# uyeleri otomatik esler.
/// </summary>
public partial class TicketingDbContext : Application.Abstractions.Persistence.IApplicationDbContext
{
}
