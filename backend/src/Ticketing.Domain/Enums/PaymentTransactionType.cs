namespace Ticketing.Domain.Enums;

/// <summary>
/// Odeme uzerindeki islem turu.
/// Bir odemenin birden fazla islemi olabilir: tahsilat denemesi,
/// sonra iade, sonra ikinci kismi iade...
/// </summary>
public enum PaymentTransactionType
{
    /// <summary>Tahsilat.</summary>
    Charge = 1,

    /// <summary>Iade.</summary>
    Refund = 2,

    /// <summary>Iptal (henuz tahsil edilmemis islemin geri alinmasi).</summary>
    Void = 3
}
