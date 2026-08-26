namespace Ticketing.Application.Abstractions.Time;

/// <summary>
/// Su anki zamani saglar.
///
/// ==================================================================
/// NEDEN DateTimeOffset.UtcNow'i DOGRUDAN CAGIRMIYORUZ?
/// ==================================================================
/// Cunku o zaman zamana bagli mantigi TEST EDEMEYIZ.
///
/// Ornek: "Rezervasyon suresi 10 dakika sonra dolar" kuralini test
/// etmek istiyorum. Handler icinde DateTimeOffset.UtcNow cagriliyorsa
/// testte 10 dakika BEKLEMEM gerekir. Bu kabul edilemez.
///
/// Bu arayuzle testte "su an 12:00" veya "su an 12:11" diyebiliyorum
/// ve sure asimi senaryosunu milisaniyeler icinde test edebiliyorum.
///
/// Domain entity'lerinde zamani PARAMETRE olarak aliyoruz
/// (ornegin reservation.StartPayment(now)). Bu arayuz ise Application
/// katmaninda o parametreyi saglamak icin.
///
/// Not: .NET 8 ile gelen System.TimeProvider da ayni isi yapar.
/// Kendi arayuzumu tercih ettim cunku yalnizca ihtiyacimiz olan tek
/// uyeyi acik ediyor; TimeProvider zamanlayici (timer) API'leri de
/// icerir ve bunlari yanlislikla kullanma ihtimalini dogurur.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
