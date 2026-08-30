namespace Ticketing.Domain.Enums;

/// <summary>
/// Ödeme uzerindeki işlem türü.
/// Bir ödemenin birden fazla islemi olabilir: tahsilat denemesi,
/// sonra iade, sonra ikinci kismi iade...
/// </summary>
public enum PaymentTransactionType
{
    /// <summary>Tahsilat.</summary>
    Charge = 1,

    /// <summary>İade.</summary>
    Refund = 2,

    /// <summary>İptal (henüz tahsil edilmemis islemin geri alinmasi).</summary>
    Void = 3,
}
