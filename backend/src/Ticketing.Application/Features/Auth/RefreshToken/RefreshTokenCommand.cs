using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.RefreshToken;

/// <summary>PDF: POST /api/v1/auth/refresh-token</summary>
public sealed record RefreshTokenCommand(string RefreshToken)
    : IRequest<Result<AuthResponse>>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
        => RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token zorunludur.");
}

/// <summary>
/// ==================================================================
/// REFRESH TOKEN ROTATION VE CALINMA TESPITI
/// ==================================================================
///
/// PDF Sprint 3 uc kural istiyor:
///   - "Refresh Token rotation uygulanmalıdır."
///   - "Eski Refresh Token tekrar kullanilamamalidir."
///   - "Logout isleminde token iptal edilmelidir."
///
/// ROTATION NEDIR?
/// Her yenilemede eski token İPTAL EDILIR ve yeni bir token üretilir.
/// Yani bir refresh token yalnızca BIR KEZ kullanilabilir.
///
/// PEKI NEDEN? Aşağıdaki saldiri senaryosu bunu aciklar:
///
///   1. Saldirgan, kullanıcının token2'sini caldi (XSS, kötü amacli
///      tarayıcı eklentisi, ele gecirilmis cihaz...).
///
///   2. Gerçek kullanıcı token2 ile yenileme yapti -> token3 aldi.
///      token2 artık İPTAL, ama veritabaninda kaydı DURUYOR ve
///      "yerine token3 gecti" bilgisini tasiyor.
///
///   3. Saldirgan da token2 ile yenileme denedi.
///
///   4. Sistem bakiyor: "bu token iptal edilmiş ama biri hâlâ
///      kullanmaya çalışıyor". Bunun iki acikamasi var: ya token
///      calindi ya da ciddi bir hata var. IKISI DE ALARM SEBEBI.
///
///   5. O kullanıcının TÜM aktif token'larini iptal ediyoruz.
///      Hem saldirgan hem gerçek kullanıcı disari atiliyor.
///      Kullanıcı tekrar giriş yapabilir; saldirgan yapamaz
///      (sifreyi bilmiyor).
///
/// Rotation OLMASAYDI: calinan token, süresi dolana kadar (7 gün)
/// sessizce kullanilirdi ve kimse fark etmezdi.
///
/// Bu, RefreshToken entity'sindeki ReplacedByTokenHash alaninin
/// var olma sebebidir.
/// ==================================================================
/// </summary>
internal sealed class RefreshTokenCommandHandler
    : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IDateTimeProvider clock,
        ICurrentUser currentUser)
    {
        _context = context;
        _tokenService = tokenService;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<AuthResponse>> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        // Gelen ham token'i hash'leyip veritabaninda ARIYORUZ.
        //
        // Veritabaninda token'in kendisi değil hash'i saklandigi için
        // doğrudan arayamayiz. Hash fonksiyonu deterministik olduğu
        // (aynı girdi -> aynı cikti) ve salt kullanmadigimiz için bu
        // arama çalışır ve TokenHash uzerindeki unique index'ten
        // faydalanir.
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (storedToken is null)
        {
            return Result.Failure<AuthResponse>(AuthErrors.InvalidRefreshToken);
        }

        // ==============================================================
        // CALINMA TESPITI
        // ==============================================================
        // İptal edilmiş bir token tekrar kullanilmaya calisiliyor.
        //
        // Mesru bir istemci bunu YAPMAZ: yenileme yaptiktan sonra eski
        // token'i atar. Dolayisiyla burasi ya saldiri ya da ciddi bir
        // istemci hatasidir. Ikisinde de en güvenli davranis aynı:
        // tüm oturumlari kapat.
        if (storedToken.IsRevoked())
        {
            await RevokeAllUserTokensAsync(storedToken.UserId, cancellationToken)
                .ConfigureAwait(false);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<AuthResponse>(AuthErrors.RefreshTokenReused);
        }

        if (storedToken.IsExpired())
        {
            return Result.Failure<AuthResponse>(AuthErrors.InvalidRefreshToken);
        }

        var user = storedToken.User;

        // Kullanıcı bu arada pasife alinmis veya kilitlenmis olabilir.
        //
        // Bu kontrol KRITIK: access token 15 dakika omurlu ve iptal
        // edilemez, ama refresh yenilemesi veritabanina gidiyor.
        // Yani bir hesabi kapattigimizda en geç 15 dakika içinde
        // kullanıcı tamamen disari atiliyor. Bu kontrol olmasaydı
        // kapatilan hesap 7 gün boyunca token yenilemeye devam ederdi.
        if (!user.IsActive)
        {
            return Result.Failure<AuthResponse>(AuthErrors.AccountInactive);
        }

        if (user.IsLockedOut())
        {
            return Result.Failure<AuthResponse>(AuthErrors.AccountLocked);
        }

        // ---- ROTATION ----
        var newToken = _tokenService.CreateRefreshToken();

        // Eski token'i iptal et ve YERINE GECENI kaydet.
        // Bu zincir, ileride "hangi token hangisinden turedi" sorusunu
        // cevaplamamizi ve saldiri anini tespit etmemizi saglar.
        storedToken.Revoke(_currentUser.IpAddress, newToken.HashValue);

        _context.RefreshTokens.Add(Domain.Entities.RefreshToken.Create(
            user.Id,
            newToken.HashValue,
            newToken.ExpiresAt,
            _currentUser.IpAddress));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var roles = await _context.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.Role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accessToken = _tokenService.CreateAccessToken(user.Id, user.Email, roles);

        return Result.Success(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            newToken.Value,
            newToken.ExpiresAt,
            new UserSummary(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.IsEmailConfirmed,
                roles)));
    }

    /// <summary>
    /// Kullanıcının TÜM aktif token'larini iptal eder.
    /// Calinma supheli durumunda cagrilir.
    /// </summary>
    private async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // ExecuteUpdateAsync KULLANMIYORUM, bilerek.
        //
        // O metot tek bir UPDATE sorgusu üretir ve daha hizlidir ama
        // entity'leri ATLAR -- yani RefreshToken.Revoke() metodundaki
        // "zaten iptal edilmisse ilk iptal zamanini koru" kuralı
        // calismaz ve denetim izi bozulur.
        //
        // Bu yol nadiren çalışıyor (yalnızca calinma supheli durumda)
        // ve bir kullanıcının aktif token sayısı az. Dogrulugu
        // hizin onune koyuyorum.
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId
                      && rt.RevokedAt == null
                      && rt.ExpiresAt > now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in activeTokens)
        {
            token.Revoke(_currentUser.IpAddress);
        }
    }
}
