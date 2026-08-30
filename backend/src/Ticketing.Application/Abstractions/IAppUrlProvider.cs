namespace Ticketing.Application.Abstractions;

/// <summary>
/// Uygulama adreslerini saglar.
///
/// Neden ayrı bir soyutlama?
/// E-postalarin icine link koyacagiz ve o linkin adresi ortama göre
/// degisir (localhost / staging / production). Handler'a bu adresi
/// sabit yazmak, uretimde "localhost:5173" iceren e-postalar
/// gondermek demektir -- klasik ve utanc verici bir hata.
///
/// PDF Sprint 2: "Uygulama URL bilgileri environment variable olarak
/// yonetilmelidir."
/// </summary>
public interface IAppUrlProvider
{
    /// <summary>Frontend adresi. Ornek: https://biletim.com</summary>
    string FrontendUrl { get; }

    /// <summary>API adresi. Ornek: https://api.biletim.com</summary>
    string ApiUrl { get; }
}
