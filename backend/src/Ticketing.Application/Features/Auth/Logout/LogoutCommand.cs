using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.Logout;

/// <summary>
/// PDF: POST /api/v1/auth/logout
///
/// RefreshToken opsiyonel: gonderilirse yalnizca O oturum kapatilir,
/// gonderilmezse kullanicinin TUM oturumlari kapatilir
/// ("tum cihazlardan cikis yap").
/// </summary>
public sealed record LogoutCommand(string? RefreshToken) : IRequest<Result>;

internal sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUser _currentUser;

    public LogoutCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        ICurrentUser currentUser)
    {
        _context = context;
        _tokenService = tokenService;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(AuthErrors.InvalidRefreshToken);
        }

        var query = _context.RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null);

        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var hash = _tokenService.HashRefreshToken(request.RefreshToken);
            query = query.Where(rt => rt.TokenHash == hash);
        }

        var tokens = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.Revoke(_currentUser.IpAddress);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // ------------------------------------------------------------------
        // TOKEN BULUNAMASA BILE BASARILI DONUYORUM
        // ------------------------------------------------------------------
        // "Cikis yapamadiniz" demek anlamsiz olurdu: kullanicinin niyeti
        // oturumu kapatmak ve sonuc olarak oturum ZATEN kapali.
        //
        // Ayrica bu, cikis endpoint'ini idempotent yapiyor: iki kez
        // cagirmak hata uretmiyor. Aglayan bir istemci istegi tekrar
        // gonderse bile sorun cikmaz.
        return Result.Success();
    }
}
