namespace Ticketing.Application.Abstractions.Security;

/// <summary>
/// Sifre hash'leme ve dogrulama.
///
/// Bu arayuz Application katmaninda, IMPLEMENTASYONU Infrastructure'da.
///
/// Neden? Hash algoritmasi (BCrypt, Argon2, PBKDF2) bir ALTYAPI
/// tercihidir. Yarin BCrypt'ten Argon2'ye gecmek istersek Application
/// katmanindaki tek bir satiri bile degistirmeyecegiz.
///
/// Bu, Dependency Inversion ilkesinin somut bir orneg: ust seviye
/// mantik (kayit olma akisi) alt seviye detaya (hash algoritmasi)
/// degil, ikisi de bu SOYUTLAMAYA bagimli.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Sifreyi hash ile karsilastirir.
    /// </summary>
    /// <returns>Eslesiyorsa true.</returns>
    bool Verify(string password, string passwordHash);
}
