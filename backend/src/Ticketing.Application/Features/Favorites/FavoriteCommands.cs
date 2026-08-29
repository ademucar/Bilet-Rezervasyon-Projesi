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
        "favorite.event_not_found", "Etkinlik bulunamadı.");
}

// EKLE -- PDF: POST /api/v1/events/{eventId}/favorite

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
            return Result.Failure(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // ETKİNLİK VAR MI VE GORULEBILIR MI?
        //
        // Yalnızca "var mi" diye bakmak YETMEZ. Gorunurluk filtresi de
        // sart: aksi halde kullanıcı bir Id tahmin edip TASLAK bir
        // etkinligi favorileyebilirdi.
        //
        // Tek başına zararsiz görünüyor ama "favorilerim" listesi o
        // etkinliğin BASLIGINI gosteriyor. Yani yayinlanmamis bir
        // etkinliğin adını sizdirmis olurdum.
        //
        // Bu, Sprint 11'de etkinlik detayinda kapattigimiz IDOR
        // acigina giden BASKA bir kapi. Aynı kontrolü burada da
        // uygulamak sart.
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

        // IDEMPOTENT: ZATEN FAVORIDEYSE HATA DEĞİL
        //
        // Kullanıcı kalp ikonuna iki kez basmis olabilir; ag isteği
        // tekrarlanmis olabilir.
        //
        // "Zaten favoride" diye 409 donmek teknik olarak doğru ama
        // kullanıcı acisindan anlamsiz: istedigi sey zaten olmuş
        // durumda. Sessizce başarılı donuyorum.
        //
        // Aynı yaklasimi Sprint 8'de ödeme callback'inde de
        // uygulamıştım.
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
            // Yukaridaki kontrol yarisa açık: iki istek aynı anda
            // gelirse ikisi de "yok" görebilir. Veritabani ikincisini
            // reddediyor ve biz bunu BASARI sayiyorum -- çünkü
            // kullanıcının istedigi sonuç gerceklesti.
            return Result.Success();
        }

        return Result.Success();
    }
}

// CIKAR -- PDF: DELETE /api/v1/events/{eventId}/favorite

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
            return Result.Failure(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var favorite = await _context.Favorites
            .FirstOrDefaultAsync(
                f => f.UserId == userId && f.EventId == request.EventId,
                cancellationToken)
            .ConfigureAwait(false);

        // Favoride degilse de BASARILI donuyorum.
        //
        // Silme islemleri dogasi geregi idempotent olmalı: "bu kayıt
        // olmasın" isteği, kayıt zaten yoksa da yerine gelmis demektir.
        // 404 donmek kullanıcıya cozemeyecegi bir sorun bildirmek olurdu.
        if (favorite is null)
        {
            return Result.Success();
        }

        // Favori için SOFT DELETE YOK -- gerçekten siliniyor.
        //
        // Sebep: Favorite bir AuditableEntity değil, sade bir bağlantı
        // kaydı. Denetim değeri yok ve kullanıcı "favorilerimi
        // temizledim" dediginde verinin gerçekten gitmesini bekler.
        _context.Favorites.Remove(favorite);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// LISTELE -- PDF: GET /api/v1/users/me/favorites

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
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // BU SORGU ASLA ONBELLEKLENMEZ
        //
        // PDF Sprint 11 kuralı: "Kullanıcıya ozel hassas veriler ortak
        // cache içinde tutulmamalidir."
        //
        // Favori listesi tanim geregi kullanıcıya OZEL. Ortak onbellege
        // koysaydım bir kullanıcının favorileri baskasina görünürdü.
        //
        // "Anahtara userId eklerim" demek de çözüm değil: her kullanıcı
        // için ayrı anahtar demek, milyonlarca anahtar ve neredeyse
        // sifir isabet oranı. Onbellegin faydasi paylasilan veridedir.
        var favorites = await _context.Favorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)

            // En son eklenen önce: kullanıcı az önce favoriledigini
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

        // İPTAL EDILMIS ETKINLIKLER LISTEDE KALIYOR -- BILINCLI
        //
        // Filtrelemeyi dusundum ama vazgectim: kullanıcı favoriledigi
        // etkinliğin İPTAL EDILDIGINI gormeli. Sessizce listeden
        // kaldirsaydim "favorim nereye gitti?" diye sorardi.
        //
        // Durum bilgisi zaten dönüyor (Status alanı); arayüz iptal
        // rozetini gosteriyor.
        return Result.Success<IReadOnlyList<EventListItem>>(favorites);
    }
}
