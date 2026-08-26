namespace Ticketing.Persistence;

/// <summary>
/// TicketingDbContext'in IApplicationDbContext arayuzunu uyguladigini
/// belirten kismi (partial) bildirim.
///
/// Neden ayri dosya? TicketingDbContext.cs "veritabani semasi" ile
/// ilgili; bu dosya ise "Application katmanina nasil gorunuyoruz"
/// sorusuyla ilgili. Iki farkli sorumluluk, iki farkli dosya.
///
/// Ayrica: DbContext zaten tum DbSet'leri ve SaveChangesAsync'i
/// sagladigi icin ek bir kod yazmamiza gerek yok. Arayuzu bildirmek
/// yeterli -- C# uyeleri otomatik esler.
/// </summary>
public partial class TicketingDbContext : Application.Abstractions.Persistence.IApplicationDbContext
{
}
