using System.ComponentModel.DataAnnotations;

namespace Ticketing.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    [Required]
    [EmailAddress]
    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = "Biletim";

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Yerel gelistirmede Mailpit TLS kullanmiyor.
    /// Uretimde bu MUTLAKA true olmali -- aksi halde e-posta icerigi
    /// (icinde sifre sifirlama linki var!) ag uzerinde duz metin gider.
    /// </summary>
    public bool UseSsl { get; set; }
}
