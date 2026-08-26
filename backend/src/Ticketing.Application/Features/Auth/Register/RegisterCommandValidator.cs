using FluentValidation;

namespace Ticketing.Application.Features.Auth.Register;

/// <summary>
/// Kayit girdilerinin dogrulanmasi.
///
/// Bu sinif ValidationBehavior tarafindan OTOMATIK bulunur ve calistirilir.
/// Handler'in dogrulamayi cagirmasina gerek yok -- dolayisiyla unutulamaz.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta adresi zorunludur.")
            .MaximumLength(256).WithMessage("E-posta adresi en fazla 256 karakter olabilir.")
            .EmailAddress().WithMessage("Gecerli bir e-posta adresi giriniz.");

        // ==================================================================
        // SIFRE POLITIKASI
        // ==================================================================
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Sifre zorunludur.")

            // Minimum 8 karakter. Uzunluk, karmasikliktan DAHA etkilidir:
            // her ek karakter olasilik uzayini katlanarak buyutur.
            .MinimumLength(8).WithMessage("Sifre en az 8 karakter olmalidir.")

            // ------------------------------------------------------------------
            // UST SINIR NEDEN VAR? -- COK ONEMLI BIR AYRINTI
            // ------------------------------------------------------------------
            // BCrypt, girdinin YALNIZCA ILK 72 BYTE'INI dikkate alir.
            // Gerisi sessizce yok sayilir.
            //
            // Ust sinir koymasaydik su olurdu: 100 karakterlik bir sifre
            // giren kullanici, aslinda ilk 72 karakteriyle korunuyor olurdu
            // ve bunu bilmezdi. Daha kotusu: ilk 72 karakteri ayni olan iki
            // FARKLI sifre ayni hash'i uretir ve ikisi de calisir.
            //
            // 72'yi acikca yasaklayarak kullaniciya dogru geri bildirim
            // veriyoruz. Bu, BCrypt kullanan projelerde en sik atlanan
            // detaylardan biridir.
            .MaximumLength(72).WithMessage("Sifre en fazla 72 karakter olabilir.")

            .Matches("[A-Z]").WithMessage("Sifre en az bir buyuk harf icermelidir.")
            .Matches("[a-z]").WithMessage("Sifre en az bir kucuk harf icermelidir.")
            .Matches("[0-9]").WithMessage("Sifre en az bir rakam icermelidir.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Ad zorunludur.")
            .MaximumLength(100).WithMessage("Ad en fazla 100 karakter olabilir.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Soyad zorunludur.")
            .MaximumLength(100).WithMessage("Soyad en fazla 100 karakter olabilir.");

        // Telefon opsiyonel; ama girildiyse bicimi dogru olmali.
        // "When" olmadan bos deger de dogrulamaya girer ve gereksiz
        // hata uretirdi.
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Telefon numarasi en fazla 20 karakter olabilir.")
            .Matches(@"^\+?[0-9\s\-()]+$").WithMessage("Gecerli bir telefon numarasi giriniz.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}
