using Hangfire.Dashboard;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

/// <summary>
/// ==================================================================
/// HANGFIRE IZLEME EKRANI ERISIM KONTROLU
/// ==================================================================
/// Hangfire'in /hangfire ekrani VARSAYILAN OLARAK YALNIZCA
/// localhost'tan erisilebilir. Yani uretime cikildiginda ekran
/// calisir gorunur ama uzaktan acilmaz.
///
/// Bu iyi bir varsayilan gibi duruyor ama TEHLIKELI bir yanilsama
/// yaratiyor: bir yetkilendirme filtresi tanimladiginiz anda
/// localhost kisiti KALKAR. Yani filtreyi yanlis yazmak, ekrani
/// tum internete acmak demektir.
///
/// Bu ekran ne gosteriyor? Calisan tum isleri, PARAMETRELERINI
/// (rezervasyon kimlikleri, kullanici kimlikleri), hata yiginlarini
/// ve veritabani baglanti hatalarini. Ustelik ekrandan is SILINEBILIR
/// ve YENIDEN CALISTIRILABILIR.
///
/// Yani burasi salt okunur bir gosterge paneli degil, bir YONETIM
/// arayuzu. Yanlis yapilandirilirsa saldirgan istedigi is'i istedigi
/// zaman tetikleyebilir.
/// ==================================================================
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.GetHttpContext();

        // Iki kosul da SAGLANMALI:
        //
        // 1) Kimlik dogrulanmis olmali
        // 2) Admin rolunde olmali
        //
        // Yalnizca ikincisine baksaydik ve IsInRole kimliksiz bir
        // kullanici icin beklenmedik bir sey donduseydi, ekran acilirdi.
        // Iki kosulu da acikca yazmak, tek satirlik bir hatanin
        // tum paneli acmasini engelliyor.
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return httpContext.User.IsInRole(Role.Names.Admin);
    }
}
