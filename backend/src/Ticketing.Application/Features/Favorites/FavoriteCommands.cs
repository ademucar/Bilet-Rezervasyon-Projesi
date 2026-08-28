using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Features.Events;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Favorites;

internal static class FavoriteErrors
{
    public static readonly Error EventNotFound = Error.NotFound(
        "favorite.event_not_found", "Etkinlik bulunamadi.");
}

// ===================================================================
// EKLE -- PDF: POST /api/v1/events/{eventId}/favorite
// ===================================================================

public sealed record AddFavoriteCommand(Guid EventId) : IRequest<Result>;

internal sealed class AddFavoriteCommandHandler : IRequestHandler<AddFavoriteCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public AddFavoriteCommandHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(AddFavoriteCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // ==============================================================
        // ETKINLIK VAR MI VE GORULEBILIR MI?
        // ==============================================================
        // Yalnizca "var mi" diye bakmak YETMEZ. Gorunurluk filtresi de
        // sart: aksi halde kullanici bir Id tahmin edip TASLAK bir
        // etkinligi favorileyebilirdi.
        //
        // Tek basina zararsiz gorunuyor ama "favorilerim" listesi o
        // etkinligin BASLIGINI gosteriyor. Yani yayinlanmamis bir
        // etkinligin adini sizdirmis olurduk.
        //
        // Bu, Sprint 11'de etkinlik detayinda kapattigimiz IDOR
        // acigina giden BASKA bir kapi. Ayni kontrolu burada da
        // uygulamak sart.
        // ==============================================================
        var eventExists = await _context.Events
            .AsNoTracking()
            .AnyAsync(
                e => e.Id == request.EventId
                  && EventVisibility.PublicStatuses.Contains(e.Status),
                cancellationToken)
            .ConfigureAwait(false);

        if (!eventExists)
        {
            return Result.Failure(FavoriteErrors.EventNotFound);
        }

        // ==============================================================
        // IDEMPOTENT: ZATEN FAVORIDEYSE HATA DEGIL
        // ==============================================================
        // Kullanici kalp ikonuna iki kez basmis olabilir; ag istegi
        // tekrarlanmis olabilir.
        //
        // "Zaten favoride" diye 409 donmek teknik olarak dogru ama
        // kullanici acisindan anlamsiz: istedigi sey zaten olmus
        // durumda. Sessizce basarili donuyoruz.
        //
        // Ayni yaklasimi Sprint 8'de odeme callback'inde de
        // uygulamistik.
        // ==============================================================
        var alreadyFavorite = await _context.Favorites
            .AsNoTracking()
            .AnyAsync(f => f.UserId == userId && f.EventId == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyFavorite)
        {
            return Result.Success();
        }

        _context.Favorites.Add(Favorite.Create(userId, request.EventId));

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Composite primary key (UserId, EventId) ihlali.
            //
            // Yukaridaki kontrol yarisa acik: iki istek ayni anda
            // gelirse ikisi de "yok" gorebilir. Veritabani ikincisini
            // reddediyor ve biz bunu BASARI sayiyoruz -- cunku
            // kullanicinin istedigi sonuc gerceklesti.
            return Result.Success();
        }

        return Result.Success();
    }
}

// ===================================================================
// CIKAR -- PDF: DELETE /api/v1/events/{eventId}/favorite
// ===================================================================

public sealed record RemoveFavoriteCommand(Guid EventId) : IRequest<Result>;

internal sealed class RemoveFavoriteCommandHandler
    : IRequestHandler<RemoveFavoriteCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public RemoveFavoriteCommandHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        RemoveFavoriteCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var favorite = await _context.Favorites
            .FirstOrDefaultAsync(
                f => f.UserId == userId && f.EventId == request.EventId,
                cancellationToken)
            .ConfigureAwait(false);

        // Favoride degilse de BASARILI donuyorum.
        //
        // Silme islemleri dogasi geregi idempotent olmali: "bu kayit
        // olmasin" istegi, kayit zaten yoksa da yerine gelmis demektir.
        // 404 donmek kullaniciya cozemeyecegi bir sorun bildirmek olurdu.
        if (favorite is null)
        {
            return Result.Success();
        }

        // Favori icin SOFT DELETE YOK -- gercekten siliniyor.
        //
        // Sebep: Favorite bir AuditableEntity degil, sade bir baglanti
        // kaydi. Denetim degeri yok ve kullanici "favorilerimi
        // temizledim" dediginde verinin gercekten gitmesini bekler.
        _context.Favorites.Remove(favorite);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// LISTELE -- PDF: GET /api/v1/users/me/favorites
// ===================================================================

public sealed record GetMyFavoritesQuery : IRequest<Result<IReadOnlyList<EventListItem>>>;

internal sealed class GetMyFavoritesQueryHandler
    : IRequestHandler<GetMyFavoritesQuery, Result<IReadOnlyList<EventListItem>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetMyFavoritesQueryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<EventListItem>>> Handle(
        GetMyFavoritesQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<IReadOnlyList<EventListItem>>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // ==============================================================
        // BU SORGU ASLA ONBELLEKLENMEZ
        // ==============================================================
        // PDF Sprint 11 kurali: "Kullaniciya ozel hassas veriler ortak
        // cache icinde tutulmamalidir."
        //
        // Favori listesi tanim geregi kullaniciya OZEL. Ortak onbellege
        // koysaydik bir kullanicinin favorileri baskasina gorunurdu.
        //
        // "Anahtara userId eklerim" demek de cozum degil: her kullanici
        // icin ayri anahtar demek, milyonlarca anahtar ve neredeyse
        // sifir isabet orani. Onbellegin faydasi paylasilan veridedir.
        // ==============================================================
        var favorites = await _context.Favorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)

            // En son eklenen once: kullanici az once favoriledigini
            // en ustte gormek ister.
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new EventListItem(
                f.Event.Id,
                f.Event.Title,
                f.Event.Category.Name,
                f.Event.City.Name,
                f.Event.Venue.Name,
                f.Event.PosterImagePath,
                f.Event.EventDate,
                f.Event.Status,
                f.Event.MinimumAge,
                f.Event.Sessions.Count(s => s.Status != EventSessionStatus.Cancelled)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // IPTAL EDILMIS ETKINLIKLER LISTEDE KALIYOR -- BILINCLI
        // ==============================================================
        // Filtrelemeyi dusundum ama vazgectim: kullanici favoriledigi
        // etkinligin IPTAL EDILDIGINI gormeli. Sessizce listeden
        // kaldirsaydik "favorim nereye gitti?" diye sorardi.
        //
        // Durum bilgisi zaten donuyor (Status alani); arayuz iptal
        // rozetini gosteriyor.
        // ==============================================================
        return Result.Success<IReadOnlyList<EventListItem>>(favorites);
    }
}
