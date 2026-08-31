using System.Text.Json;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Common.Auditing;

/// <summary>
/// Denetim kaydi yazmayi kisaltan yardimci.
/// </summary>
/// <remarks>
/// NEDEN VAR?
///
/// AuditLog.Create sekiz parametre aliyor ve bunlarin ucu (userId,
/// ipAddress, correlationId) HER CAGRIDA ayni yerden geliyor:
/// ICurrentUser. Her handler'da bu ucunu elle yazmak, er ya da gec
/// birinin unutulmasi demek -- ve unutulan correlationId, denetim
/// kaydini onu tetikleyen istekten kopariyor. O zaman "bu degisiklik
/// hangi istekte oldu?" sorusu cevapsiz kaliyor.
///
/// Yardimci uculugu tek yerde topluyor.
///
/// NEDEN AYRI BIR SERVIS DEGIL?
///
/// Extension metodu yazdim, arayuz + DI kaydi yapmadim. Cunku burada
/// SOYUTLANACAK bir sey yok: davranis sabit, degistirilebilir bir
/// yani yok ve test ederken sahtesini yazmak isteyecegim bir bagimlik
/// icermiyor (context ve currentUser zaten disaridan geliyor).
/// Arayuz eklemek, sirf "katmanli gorunsun" diye dosya sayisini
/// artirmak olurdu.
/// </remarks>
internal static class AuditWriter
{
    /// <summary>
    /// Denetim kaydini kuyruga ekler. SaveChangesAsync CAGIRMAZ.
    /// </summary>
    /// <remarks>
    /// Kaydetmeyi bilerek cagirana birakiyorum: denetim kaydi, onu
    /// dogruran degisiklikle AYNI transaction'da yazilmali. Burada
    /// kaydetseydim iki ayri transaction olurdu ve ikincisi
    /// basarisiz olursa degisiklik yapilmis ama izi kalmamis olurdu.
    /// </remarks>
    public static void AddAudit(
        this IApplicationDbContext context,
        ICurrentUser currentUser,
        string entityName,
        Guid entityId,
        string action,
        object? oldValues = null,
        object? newValues = null)
    {
        context.AuditLogs.Add(AuditLog.Create(
            entityName: entityName,
            entityId: entityId,
            action: action,
            userId: currentUser.UserId,

            // Eski/yeni degerler JSON. Duz metin ("aktif -> pasif")
            // yazsaydim sonradan ayristirmak gerekirdi; jsonb ile
            // PostgreSQL icinde sorgulanabilir kaliyor.
            oldValues: oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            newValues: newValues is null ? null : JsonSerializer.Serialize(newValues),

            ipAddress: currentUser.IpAddress,
            correlationId: currentUser.CorrelationId));
    }
}
