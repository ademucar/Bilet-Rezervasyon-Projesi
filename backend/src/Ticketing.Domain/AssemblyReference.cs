using System.Reflection;

namespace Ticketing.Domain;

/// <summary>
/// Bu sinif hicbir is yapmaz. Tek gorevi, bu projenin assembly'sine
/// derleme zamaninda guvenli bir sekilde erisebilmemizi saglamak.
///
/// Neden gerekli?
/// Architecture testlerinde ve DI kayitlarinda "su assembly'deki tum
/// handler'lari bul" gibi islemler yapacagiz. Bunun icin Assembly nesnesine
/// ihtiyacimiz var. Assembly.Load("Ticketing.Domain") gibi metin tabanli bir
/// yontem kullanabilirdim ama proje adi degistiginde derleme hatasi vermez,
/// calisma zamaninda patlar.
///
/// typeof(AssemblyReference).Assembly ise derleyici tarafindan kontrol edilir.
/// Proje adi degisirse kod derlenmez, hatayi aninda goruruz.
/// </summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
