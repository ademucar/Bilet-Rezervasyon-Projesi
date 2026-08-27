using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Organizers;

internal static class OrganizerErrors
{
    public static readonly Error ApplicationNotFound = Error.NotFound(
        "organizer.application_not_found", "Basvuru bulunamadi.");

    public static readonly Error AlreadyOrganizer = Error.Conflict(
        "organizer.already_organizer", "Zaten organizator yetkiniz var.");

    public static readonly Error PendingApplicationExists = Error.Conflict(
        "organizer.pending_application_exists",
        "Degerlendirilmeyi bekleyen bir basvurunuz zaten var.");

    public static readonly Error ProfileNotFound = Error.NotFound(
        "organizer.profile_not_found",
        "Organizator profiliniz bulunamadi.");
}

// ===================================================================
// BASVURU -- kullanici organizator olmak istiyor
// ===================================================================

/// <summary>PDF sayfa 5: kullanici organizator basvurusu yapar.</summary>
public sealed record ApplyForOrganizerCommand(
    string CompanyName,
    string ContactEmail,
    string? TaxNumber,
    string? ContactPhone,
    string? Description) : IRequest<Result<Guid>>;

public sealed class ApplyForOrganizerCommandValidator : AbstractValidator<ApplyForOrganizerCommand>
{
    public ApplyForOrganizerCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.TaxNumber).MaximumLength(20).When(x => x.TaxNumber is not null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

internal sealed class ApplyForOrganizerCommandHandler
    : IRequestHandler<ApplyForOrganizerCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ApplyForOrganizerCommandHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        ApplyForOrganizerCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<Guid>(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // Zaten organizator mu?
        var isOrganizer = await _context.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == Role.Ids.Organizer, cancellationToken)
            .ConfigureAwait(false);

        if (isOrganizer)
        {
            return Result.Failure<Guid>(OrganizerErrors.AlreadyOrganizer);
        }

        // Bekleyen basvuru varsa ikincisine izin verme.
        //
        // Neden? Kullanici sabirsizlanip 10 basvuru gonderirse admin
        // panelinde ayni kisiden 10 kayit birikir ve degerlendirme
        // zorlasir. Ayrica hangisini onaylayacagi belirsizlesir.
        //
        // REDDEDILMIS basvuru varsa yenisine IZIN VERIYORUZ -- kullanici
        // eksiklerini giderip tekrar basvurabilmeli.
        var hasPending = await _context.OrganizerApplications
            .AsNoTracking()
            .AnyAsync(
                a => a.UserId == userId && a.Status == OrganizerApplicationStatus.Pending,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasPending)
        {
            return Result.Failure<Guid>(OrganizerErrors.PendingApplicationExists);
        }

        var application = OrganizerApplication.Create(
            userId, request.CompanyName, request.ContactEmail, request.TaxNumber, request.Description);

        _context.OrganizerApplications.Add(application);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(application.Id);
    }
}

// ===================================================================
// ONAY -- admin basvuruyu onayliyor
// ===================================================================

public sealed record ApproveOrganizerApplicationCommand(Guid ApplicationId) : IRequest<Result>;

internal sealed class ApproveOrganizerApplicationCommandHandler
    : IRequestHandler<ApproveOrganizerApplicationCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ApproveOrganizerApplicationCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        ApproveOrganizerApplicationCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _context.OrganizerApplications
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            .ConfigureAwait(false);

        if (application is null)
        {
            return Result.Failure(OrganizerErrors.ApplicationNotFound);
        }

        // Kullaniciyi rolleriyle birlikte yukluyorum: AssignRole
        // BELLEKTEKI koleksiyona bakip "zaten var mi" kontrolu yapiyor.
        // Include etmezsem koleksiyon bos gelir ve ayni rol iki kez
        // eklenmeye calisilir -> composite key ihlali.
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == application.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(OrganizerErrors.ApplicationNotFound);
        }

        // Durum gecisi entity'de dogrulaniyor: zaten degerlendirilmis
        // bir basvuru tekrar onaylanamaz (DomainException -> 422).
        application.Approve(_currentUser.UserId ?? Guid.Empty, _clock.UtcNow);

        // ==============================================================
        // ONAY = UC ISLEM, TEK SaveChanges
        // ==============================================================
        //   1. Basvuruyu onayla
        //   2. Organizator profilini olustur
        //   3. Organizator rolunu ata
        //
        // Ucu de AYNI SaveChangesAsync cagrisinda kaydediliyor. EF bunu
        // tek bir transaction icinde calistirir.
        //
        // Ayri ayri kaydetseydik su risk olusurdu: basvuru onaylandi
        // ama rol atanamadi (baglanti koptu). Kullanici "onaylandiniz"
        // bildirimi alir ama hicbir sey yapamaz -- ve bu durumu
        // duzeltmek icin elle mudahale gerekir.
        // ==============================================================
        var profile = OrganizerProfile.Create(
            application.UserId, application.CompanyName, application.ContactEmail);

        profile.Update(
            application.CompanyName, application.ContactEmail,
            application.ContactPhone, application.Description);

        // Admin onayladigi icin dogrulanmis sayiyoruz.
        profile.Verify();

        _context.OrganizerProfiles.Add(profile);

        user.AssignRole(Role.Create(Role.Ids.Organizer, Role.Names.Organizer));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // NOT: Kullanicinin MEVCUT access token'inda hala eski roller var.
        // Yeni rolu gorebilmesi icin token'ini yenilemesi gerekiyor --
        // en gec 15 dakika icinde otomatik olacak.
        //
        // Frontend'e "rolunuz guncellendi, sayfayi yenileyin" bildirimi
        // gondermek Sprint 10'da SignalR ile yapilacak.
        return Result.Success();
    }
}

// ===================================================================
// RED
// ===================================================================

public sealed record RejectOrganizerApplicationCommand(Guid ApplicationId, string Reason)
    : IRequest<Result>;

public sealed class RejectOrganizerApplicationCommandValidator
    : AbstractValidator<RejectOrganizerApplicationCommand>
{
    public RejectOrganizerApplicationCommandValidator()
        => RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Red gerekcesi zorunludur.")
            .MaximumLength(1000);
}

internal sealed class RejectOrganizerApplicationCommandHandler
    : IRequestHandler<RejectOrganizerApplicationCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RejectOrganizerApplicationCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        RejectOrganizerApplicationCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _context.OrganizerApplications
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            .ConfigureAwait(false);

        if (application is null)
        {
            return Result.Failure(OrganizerErrors.ApplicationNotFound);
        }

        // Entity, gerekcenin bos olmasini reddediyor.
        // Gerekcesiz red, kullanicinin ne duzeltecegini bilmemesi demek.
        application.Reject(_currentUser.UserId ?? Guid.Empty, request.Reason, _clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// LISTELEME -- admin paneli
// ===================================================================

public sealed record OrganizerApplicationDto(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string CompanyName,
    string ContactEmail,
    string? TaxNumber,
    string? Description,
    OrganizerApplicationStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt);

public sealed record GetOrganizerApplicationsQuery(OrganizerApplicationStatus? Status)
    : IRequest<Result<IReadOnlyList<OrganizerApplicationDto>>>;

internal sealed class GetOrganizerApplicationsQueryHandler
    : IRequestHandler<GetOrganizerApplicationsQuery, Result<IReadOnlyList<OrganizerApplicationDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetOrganizerApplicationsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<IReadOnlyList<OrganizerApplicationDto>>> Handle(
        GetOrganizerApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.OrganizerApplications.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(a => a.Status == request.Status.Value);
        }

        var applications = await query
            // En eski basvuru en ustte: adil sira (FIFO).
            // Yeniden eskiye siralasaydik eski basvurular listenin
            // dibinde kalir ve surekli beklerdi.
            .OrderBy(a => a.CreatedAt)
            .Select(a => new OrganizerApplicationDto(
                a.Id, a.UserId, a.User.Email, a.CompanyName, a.ContactEmail,
                a.TaxNumber, a.Description, a.Status, a.RejectionReason, a.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<OrganizerApplicationDto>>(applications);
    }
}
