namespace Ticketing.Application.Abstractions.Time;

/// <summary>
/// Su anki zamani saglar.
///
/// NEDEN DateTimeOffset.UtcNow'i DOGRUDAN CAGIRMIYORUZ?
///
/// Çünkü o zaman zamana bağlı mantığı TEST EDEMEYIZ.
///
/// Ornek: "Rezervasyon süresi 10 dakika sonra dolar" kuralini test
/// etmek istiyorum. Handler içinde DateTimeOffset.UtcNow cagriliyorsa
/// testte 10 dakika BEKLEMEM gerekir. Bu kabul edilemez.
///
/// Bu arayuzle testte "su an 12:00" veya "su an 12:11" diyebiliyorum
/// ve süre asimi senaryosunu milisaniyeler içinde test edebiliyorum.
///
/// Domain entity'lerinde zamani PARAMETRE olarak aliyorum
/// (örneğin reservation.StartPayment(now)). Bu arayüz ise Application
/// katmaninda o parametreyi saglamak için.
///
/// Not: .NET 8 ile gelen System.TimeProvider da aynı isi yapar.
/// Kendi arayuzumu tercih ettim çünkü yalnızca ihtiyacimiz olan tek
/// uyeyi açık ediyor; TimeProvider zamanlayici (timer) API'leri de
/// içerir ve bunlari yanlislikla kullanma ihtimalini dogurur.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
