using Hangfire.Dashboard;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

/// <summary>
/// Hangfire izleme ekrani erisim kontrolu
///
/// Hangfire'in /hangfire ekrani varsayilan olarak yalnizca
/// localhost'tan erişilebilir. Yani uretime cikildiginda ekran
/// çalışır görünür ama uzaktan acilmaz.
///
/// Bu iyi bir varsayılan gibi duruyor ama TEHLIKELI bir yanilsama
/// yaratiyor: bir yetkilendirme filtresi tanimladiginiz anda
/// localhost kisiti KALKAR. Yani filtreyi yanlış yazmak, ekrani
/// tüm internete acmak demektir.
///
/// Bu ekran ne gosteriyor? Calisan tüm isleri, PARAMETRELERINI
/// (rezervasyon kimlikleri, kullanıcı kimlikleri), hata yiginlarini
/// ve veritabani bağlantı hatalarini. Ustelik ekrandan is SILINEBILIR
/// ve yeniden calistirilabilir.
///
/// Yani burasi salt okunur bir gosterge paneli değil, bir YONETIM
/// arayuzu. Yanlis yapilandirilirsa saldirgan istedigi is'i istedigi
/// zaman tetikleyebilir.
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.GetHttpContext();

        // Iki kosul da SAGLANMALI:
        //
        // 1) Kimlik dogrulanmis olmalı
        // 2) Admin rolunde olmalı
        //
        // Yalnızca ikincisine baksaydim ve IsInRole kimliksiz bir
        // kullanıcı için beklenmedik bir sey donduseydi, ekran acilirdi.
        // Iki kosulu da acikca yazmak, tek satirlik bir hatanin
        // tüm paneli acmasini engelliyor.
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return httpContext.User.IsInRole(Role.Names.Admin);
    }
}
