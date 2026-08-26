using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.Profile;

/// <summary>PDF: GET /api/v1/auth/me</summary>
public sealed record GetCurrentUserQuery : IRequest<Result<UserSummary>>;

internal sealed class GetCurrentUserQueryHandler
    : IRequestHandler<GetCurrentUserQuery, Result<UserSummary>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetCurrentUserQueryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<UserSummary>> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<UserSummary>(AuthErrors.UserNotFound);
        }

        // ------------------------------------------------------------------
        // TOKEN'DAKI BILGIYI DEGIL VERITABANINI OKUYORUM
        // ------------------------------------------------------------------
        // ICurrentUser'da Email ve Roles zaten var (token'dan geliyor).
        // Onlari dondurmek daha hizli olurdu -- veritabanina hic gitmezdik.
        //
        // Ama YANLIS olurdu: token 15 dakika omurlu ve icindeki bilgi
        // uretildigi ANI yansitir. Bu 15 dakika icinde:
        //   - Admin kullaniciya Organizator rolu vermis olabilir
        //   - Kullanici adini degistirmis olabilir
        //   - E-postasini dogrulamis olabilir
        //
        // "/me" endpoint'i frontend'in profil ekranini besliyor ve
        // GUNCEL veriyi gostermeli. Bayat veri gostermek, kullanicinin
        // "rolum verilmedi mi?" diye destek acmasina yol acar.
        //
        // Projeksiyon (Select) kullaniyorum: EF yalnizca ihtiyacimiz olan
        // sutunlari cekiyor. Entity'nin tamamini yukleyip sonra donusturmek
        // gereksiz veri transferi olurdu (PasswordHash dahil!).
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.IsEmailConfirmed,
                u.UserRoles.Select(ur => ur.Role.Name).ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return user is null
            ? Result.Failure<UserSummary>(AuthErrors.UserNotFound)
            : Result.Success(user);
    }
}
