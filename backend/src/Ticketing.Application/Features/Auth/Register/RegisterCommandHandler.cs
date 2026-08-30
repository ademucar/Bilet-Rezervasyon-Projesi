using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Auth.Register;

/// <summary>
/// Kayıt akışı:
///   1. E-posta zaten kullanılıyor mu?
///   2. Şifreyi hash'le
///   3. Kullaniciyi oluştur ve varsayılan "User" rolunu ata
///   4. Token üret
///
/// sealed: architecture testim handler'larin sealed olmasini zorunlu
/// kiliyor. Handler'dan miras almak için bir sebep yok; sealed hem niyeti
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
        // E-postayi entity ile AYNI şekilde normalize ediyorum.
        //
        // User.Create içinde de ToLowerInvariant var. Burada tekrar
        // yapmam gerekiyor çünkü ARAMA yapıyorum: veritabanindaki
        // kayitlar küçük harfle saklandi, aradigim deger de küçük
        // harf olmalı. Aksi halde "Ahmet@X.com" ile arayinca kayıt
        // bulunamaz ve unique index ihlaline duseriz.
        var email = request.Email.Trim().ToLowerInvariant();

        var emailInUse = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (emailInUse)
        {
            // burada "kullanıcı numaralandirma" riski var ama kabul ediyorum
            //
            // Login'de bilerek belirsiz mesaj donuyorum. Kayitta ise
            // "bu e-posta kullanılıyor" demek zorundayız -- aksi halde
            // kullanıcı neden kayıt olamadigini anlayamaz.
            //
            // Bu, güvenlik ile kullanilabilirlik arasında BILINCLI bir
            // odundur ve sektor standardidir. Riski Sprint 15'te register
            // endpoint'ine rate limit koyarak sinirlayacagim: saldirgan
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

        // Varsayılan rol: User.
        //
        // Role.Ids.User SABIT bir GUID olduğu için veritabanindan rol
        // kaydini CEKMEME gerek yok -- bir sorgu tasarruf ediyorum.
        // Rastgele ID kullansaydım önce "User rolunu bul" sorgusu
        // yapmak zorunda kalirdim.
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

            // Kullanıcıya token'in kendisi gidiyor; veritabaninda hash'i var.
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
