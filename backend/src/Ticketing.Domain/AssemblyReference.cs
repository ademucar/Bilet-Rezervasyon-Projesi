using System.Reflection;

namespace Ticketing.Domain;

/// <summary>
/// Bu sinif hiçbir is yapmaz. Tek gorevi, bu projenin assembly'sine
/// derleme zamaninda güvenli bir şekilde erisebilmemizi saglamak.
///
/// Neden gerekli?
/// Architecture testlerinde ve DI kayitlarinda "su assembly'deki tüm
/// handler'lari bul" gibi islemler yapacagiz. Bunun için Assembly nesnesine
/// ihtiyacimiz var. Assembly.Load("Ticketing.Domain") gibi metin tabanli bir
/// yontem kullanabilirdim ama proje adı degistiginde derleme hatası vermez,
/// calisma zamaninda patlar.
///
/// typeof(AssemblyReference).Assembly ise derleyici tarafından kontrol edilir.
/// Proje adı degisirse kod derlenmez, hatayi anında goruruz.
/// </summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
