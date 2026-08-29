using FluentValidation;

namespace Ticketing.Application.Features.Auth.Register;

/// <summary>
/// Kayıt girdilerinin dogrulanmasi.
///
/// Bu sinif ValidationBehavior tarafından OTOMATIK bulunur ve calistirilir.
/// Handler'in dogrulamayi cagirmasina gerek yok -- dolayisiyla unutulamaz.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta adresi zorunludur.")
            .MaximumLength(256).WithMessage("E-posta adresi en fazla 256 karakter olabilir.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");

        // ==================================================================
        // SIFRE POLITIKASI
        // ==================================================================
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Şifre zorunludur.")

            // Minimum 8 karakter. Uzunluk, karmasikliktan DAHA etkilidir:
            // her ek karakter olasilik uzayini katlanarak buyutur.
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")

            // ------------------------------------------------------------------
            // UST SINIR NEDEN VAR? -- COK ONEMLI BIR AYRINTI
            // ------------------------------------------------------------------
            // BCrypt, girdinin YALNIZCA ILK 72 BYTE'INI dikkate alır.
            // Gerisi sessizce yok sayilir.
            //
            // Ust sinir koymasaydik su olurdu: 100 karakterlik bir şifre
            // giren kullanıcı, aslında ilk 72 karakteriyle korunuyor olurdu
            // ve bunu bilmezdi. Daha kotusu: ilk 72 karakteri aynı olan iki
            // FARKLI şifre aynı hash'i üretir ve ikisi de çalışır.
            //
            // 72'yi acikca yasaklayarak kullanıcıya doğru geri bildirim
            // veriyoruz. Bu, BCrypt kullanan projelerde en sik atlanan
            // detaylardan biridir.
            .MaximumLength(72).WithMessage("Şifre en fazla 72 karakter olabilir.")

            .Matches("[A-Z]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Ad zorunludur.")
            .MaximumLength(100).WithMessage("Ad en fazla 100 karakter olabilir.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Soyad zorunludur.")
            .MaximumLength(100).WithMessage("Soyad en fazla 100 karakter olabilir.");

        // Telefon opsiyonel; ama girildiyse bicimi doğru olmalı.
        // "When" olmadan boş deger de dogrulamaya girer ve gereksiz
        // hata üretirdi.
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Telefon numarasi en fazla 20 karakter olabilir.")
            .Matches(@"^\+?[0-9\s\-()]+$").WithMessage("Gecerli bir telefon numarasi giriniz.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}
