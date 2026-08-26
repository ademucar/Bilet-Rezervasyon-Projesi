using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Auth.Register;

/// <summary>
/// Kayit akisi:
///   1. E-posta zaten kullaniliyor mu?
///   2. Sifreyi hash'le
///   3. Kullaniciyi olustur ve varsayilan "User" rolunu ata
///   4. Token uret
///
/// sealed: architecture testimiz handler'larin sealed olmasini zorunlu
/// kiliyor. Handler'dan miras almak icin bir sebep yok; sealed hem niyeti
/// belirtir hem de JIT'in metod cagrilarini devirtualize etmesine izin verir.
/// </summary>
internal sealed class RegisterCommandHandler
    : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public RegisterCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<Result<AuthResponse>> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken)
    {
        // E-postayi entity ile AYNI sekilde normalize ediyorum.
        //
        // User.Create icinde de ToLowerInvariant var. Burada tekrar
        // yapmam gerekiyor cunku ARAMA yapiyorum: veritabanindaki
        // kayitlar kucuk harfle saklandi, aradigim deger de kucuk
        // harf olmali. Aksi halde "Ahmet@X.com" ile arayinca kayit
        // bulunamaz ve unique index ihlaline duseriz.
        var email = request.Email.Trim().ToLowerInvariant();

        var emailInUse = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (emailInUse)
        {
            // ------------------------------------------------------------------
            // BURADA "kullanici numaralandirma" RISKI VAR AMA KABUL EDIYORUZ
            // ------------------------------------------------------------------
            // Login'de bilerek belirsiz mesaj donuyoruz. Kayitta ise
            // "bu e-posta kullaniliyor" demek zorundayiz -- aksi halde
            // kullanici neden kayit olamadigini anlayamaz.
            //
            // Bu, guvenlik ile kullanilabilirlik arasinda BILINCLI bir
            // odundur ve sektor standardidir. Riski Sprint 15'te register
            // endpoint'ine rate limit koyarak sinirlayacagiz: saldirgan
            // e-posta listesi taramasini pratikte yapamayacak.
            return Result.Failure<AuthResponse>(AuthErrors.EmailAlreadyInUse);
        }

        var user = User.Create(
            email,
            _passwordHasher.Hash(request.Password),
            request.FirstName,
            request.LastName);

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            user.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber);
        }

        // Varsayilan rol: User.
        //
        // Role.Ids.User SABIT bir GUID oldugu icin veritabanindan rol
        // kaydini CEKMEME gerek yok -- bir sorgu tasarruf ediyoruz.
        // Rastgele ID kullansaydik once "User rolunu bul" sorgusu
        // yapmak zorunda kalirdik.
        var defaultRole = Role.Create(Role.Ids.User, Role.Names.User);
        user.AssignRole(defaultRole);

        var refreshToken = _tokenService.CreateRefreshToken();

        _context.Users.Add(user);
        _context.RefreshTokens.Add(Domain.Entities.RefreshToken.Create(
            user.Id,
            refreshToken.HashValue,
            refreshToken.ExpiresAt));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accessToken = _tokenService.CreateAccessToken(user.Id, user.Email, [Role.Names.User]);

        return Result.Success(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAt,

            // Kullaniciya token'in KENDISI gidiyor; veritabaninda HASH'i var.
            refreshToken.Value,
            refreshToken.ExpiresAt,
            new UserSummary(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.IsEmailConfirmed,
                [Role.Names.User])));
    }
}
