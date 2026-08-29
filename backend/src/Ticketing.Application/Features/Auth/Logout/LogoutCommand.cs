using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.Logout;

/// <summary>
/// PDF: POST /api/v1/auth/logout
///
/// RefreshToken opsiyonel: gonderilirse yalnızca O oturum kapatilir,
/// gonderilmezse kullanıcının TÜM oturumlari kapatilir
/// ("tüm cihazlardan çıkış yap").
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

        // TOKEN BULUNAMASA BILE BASARILI DONUYORUM
        //
        // "Çıkış yapamadiniz" demek anlamsiz olurdu: kullanıcının niyeti
        // oturumu kapatmak ve sonuç olarak oturum ZATEN kapalı.
        //
        // Ayrıca bu, çıkış endpoint'ini idempotent yapiyor: iki kez
        // cagirmak hata uretmiyor. Aglayan bir istemci isteği tekrar
        // gonderse bile sorun cikmaz.
        return Result.Success();
    }
}
