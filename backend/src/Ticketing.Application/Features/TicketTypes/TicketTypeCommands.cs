using System.Globalization;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Application.Features.TicketTypes;

internal static class TicketTypeErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "ticket_type.not_found", "Bilet türü bulunamadı.");

    public static readonly Error EventNotFound = Error.NotFound(
        "ticket_type.event_not_found", "Etkinlik bulunamadı.");

    public static readonly Error SalesStarted = Error.Conflict(
        "ticket_type.sales_started",
        "Satışı baslamis etkinliğin bilet türleri degistirilemez.");

    public static readonly Error QuotaExceedsCapacity = Error.Conflict(
        "ticket_type.quota_exceeds_capacity",
        "Kontenjan, salon kapasitesini aşamaz.");

    public static readonly Error SectionNotFound = Error.NotFound(
        "ticket_type.section_not_found", "Bölüm bulunamadı.");

    public static readonly Error SectionAlreadyAssigned = Error.Conflict(
        "ticket_type.section_already_assigned",
        "Bu bölüm başka bir bilet turune atanmis. Önce mevcut atamayi kaldirin.");

    public static readonly Error HasSoldTickets = Error.Conflict(
        "ticket_type.has_sold_tickets",
        "Bu bilet turunden satış yapilmis. Silinemez, yalnızca pasife alinabilir.");
}

// OLUSTURMA -- PDF: POST /api/v1/events/{eventId}/ticket-types

public sealed record CreateTicketTypeCommand(
    Guid EventId,
    string Name,
    decimal Price,
    string Currency,
    int? Quota,
    bool RequiresStudentVerification,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate) : IRequest<Result<Guid>>;

public sealed class CreateTicketTypeCommandValidator : AbstractValidator<CreateTicketTypeCommand>
{
    public CreateTicketTypeCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bilet türü adı zorunludur.")
            .MaximumLength(100);

        // PDF is kuralı: "Fiyat sıfırdan küçük olamaz."
        //
        // Money value object'i de bunu reddediyor. Burada da kontrol
        // etmemin sebebi kullanıcıya ALAN BAZINDA anlasilir hata vermek:
        // Money'nin DomainException'i 422 döner ve hangi alanin
        // sorunlu olduğunu soylemez.
        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("Fiyat negatif olamaz.")
            // Ust sinir: 1 milyon TL'lik bilet yazım hatasidir.
            // (Ornegin 250 yerine 250000 yazilmasi.)
            .LessThanOrEqualTo(1_000_000).WithMessage("Fiyat 1.000.000 aşamaz.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3).WithMessage("Para birimi 3 harfli ISO 4217 kodu olmalıdır (TRY, USD, EUR).");

        RuleFor(x => x.Quota)
            .GreaterThan(0).WithMessage("Kontenjan sıfırdan büyük olmalıdır.")
            .When(x => x.Quota.HasValue);

        RuleFor(x => x.SalesStartDate)
            .LessThan(x => x.SalesEndDate)
            .WithMessage("Satış baslangici, bitisinden önce olmalıdır.")
            .When(x => x.SalesStartDate.HasValue && x.SalesEndDate.HasValue);
    }
}

internal sealed class CreateTicketTypeCommandHandler
    : IRequestHandler<CreateTicketTypeCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateTicketTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(
        CreateTicketTypeCommand request,
        CancellationToken cancellationToken)
    {
        // TicketTypes'i Include ediyorum: Event.AddTicketType, aynı
        // isimde tur var mi diye BELLEKTEKI koleksiyona bakiyor.
        var evt = await _context.Events
            .Include(e => e.TicketTypes)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure<Guid>(TicketTypeErrors.EventNotFound);
        }

        // PDF is kuralı: "Kontenjan salon kapasitesini aşamaz."
        if (request.Quota.HasValue)
        {
            var hallCapacity = await _context.Halls
                .AsNoTracking()
                .Where(h => h.Id == evt.HallId)
                .Select(h => h.Capacity)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (request.Quota.Value > hallCapacity)
            {
                return Result.Failure<Guid>(TicketTypeErrors.QuotaExceedsCapacity);
            }
        }

        // Money yapicisi negatif tutarı ve geçersiz para birimini
        // reddediyor; DomainException -> 422.
        var price = new Money(request.Price, request.Currency);

        // Event.AddTicketType, "satışı baslamis etkinlige yeni bilet
        // türü eklenemez" kuralini ve isim cakismasini kontrol ediyor.
        var ticketType = evt.AddTicketType(
            request.Name, price, request.Quota, request.RequiresStudentVerification);

        if (request.SalesStartDate.HasValue || request.SalesEndDate.HasValue)
        {
            ticketType.SetSalesPeriod(request.SalesStartDate, request.SalesEndDate);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ticketType.Id);
    }
}

// FIYAT DEGISTIRME -- PDF: denetim kaydı ZORUNLU

public sealed record ChangeTicketTypePriceCommand(Guid Id, decimal Price, string Currency)
    : IRequest<Result>;

public sealed class ChangeTicketTypePriceCommandValidator
    : AbstractValidator<ChangeTicketTypePriceCommand>
{
    public ChangeTicketTypePriceCommandValidator()
    {
        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("Fiyat negatif olamaz.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Fiyat 1.000.000 aşamaz.");

        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

internal sealed class ChangeTicketTypePriceCommandHandler
    : IRequestHandler<ChangeTicketTypePriceCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ChangeTicketTypePriceCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(
        ChangeTicketTypePriceCommand request,
        CancellationToken cancellationToken)
    {
        var ticketType = await _context.TicketTypes
            .Include(t => t.Event)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (ticketType is null)
        {
            return Result.Failure(TicketTypeErrors.NotFound);
        }

        var newPrice = new Money(request.Price, request.Currency);

        // ChangePrice ESKİ fiyati dönüyor -- denetim kaydı için.
        var oldPrice = ticketType.ChangePrice(newPrice);

        // Fiyat gerçekten degismediyse denetim kaydı oluşturma.
        // Kullanıcı formu acip hiçbir sey degistirmeden kaydettiginde
        // audit log'u gereksiz kayitla sisirmeyelim.
        if (oldPrice == newPrice)
        {
            return Result.Success();
        }

        // PDF is kuralı (Sprint 6):
        // "Satış baslamis bilet turunun fiyati degistirilirse
        //  degisiklik loglanmalidir."
        //
        // Neden yalnızca satış baslamissa? Çünkü o noktadan sonra
        // fiyat degisikligi TICARI bir olaydir: bazi musteriler eski
        // fiyattan, bazilari yenisinden almis olur. Sikayet geldiğinde
        // "o gün fiyat neydi, kim degistirdi" sorusuna cevap verebilmek
        // gerekir.
        //
        // Satış baslamadan önce yapilan degisiklikler ise siradan
        // duzenlemedir; her birini loglamak audit tablosunu gereksiz
        // sisirir ve gerçek olaylari gorunmez kilar.
        var salesStarted = ticketType.Event.Status is EventStatus.SalesOpen
                                                   or EventStatus.SalesClosed
                                                   or EventStatus.Completed;

        if (salesStarted)
        {
            _context.AuditLogs.Add(AuditLog.Create(
                entityName: nameof(TicketType),
                entityId: ticketType.Id,
                action: "PriceChanged",
                userId: _currentUser.UserId,

                // Eski ve yeni değerleri JSON olarak sakliyorum.
                // Duz metin ("250 TRY -> 300 TRY") yazsaydım sonradan
                // ayristirmak gerekirdi; jsonb ile PostgreSQL içinde
                // sorgulanabilir kaliyor.
                oldValues: JsonSerializer.Serialize(new
                {
                    Amount = oldPrice.Amount,
                    Currency = oldPrice.Currency,
                }),
                newValues: JsonSerializer.Serialize(new
                {
                    Amount = newPrice.Amount,
                    Currency = newPrice.Currency,
                }),
                ipAddress: _currentUser.IpAddress,
                correlationId: _currentUser.CorrelationId));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// GUNCELLEME (fiyat HARIC)

public sealed record UpdateTicketTypeCommand(
    Guid Id,
    string Name,
    int? Quota,
    bool RequiresStudentVerification,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate) : IRequest<Result>;

public sealed class UpdateTicketTypeCommandValidator : AbstractValidator<UpdateTicketTypeCommand>
{
    public UpdateTicketTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quota).GreaterThan(0).When(x => x.Quota.HasValue);
    }
}

internal sealed class UpdateTicketTypeCommandHandler
    : IRequestHandler<UpdateTicketTypeCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public UpdateTicketTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(UpdateTicketTypeCommand request, CancellationToken cancellationToken)
    {
        var ticketType = await _context.TicketTypes
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (ticketType is null)
        {
            return Result.Failure(TicketTypeErrors.NotFound);
        }

        ticketType.Update(request.Name, request.Quota, request.RequiresStudentVerification);
        ticketType.SetSalesPeriod(request.SalesStartDate, request.SalesEndDate);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// BOLUM ATAMA -- PDF: POST /api/v1/ticket-types/{id}/assign-section

public sealed record AssignSectionCommand(Guid TicketTypeId, Guid SeatSectionId)
    : IRequest<Result>;

internal sealed class AssignSectionCommandHandler : IRequestHandler<AssignSectionCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public AssignSectionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(AssignSectionCommand request, CancellationToken cancellationToken)
    {
        var ticketType = await _context.TicketTypes
            .Include(t => t.Sections)
            .FirstOrDefaultAsync(t => t.Id == request.TicketTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (ticketType is null)
        {
            return Result.Failure(TicketTypeErrors.NotFound);
        }

        var sectionExists = await _context.SeatSections
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SeatSectionId, cancellationToken)
            .ConfigureAwait(false);

        if (!sectionExists)
        {
            return Result.Failure(TicketTypeErrors.SectionNotFound);
        }

        // PDF: "Aynı koltuk birden fazla aktif bilet turune atanamaz."
        //
        // Bölüm BASKA bir bilet turune atanmis mi?
        //
        // Kesin garanti veritabanindaki UNIQUE (SeatSectionId)
        // index'inde. Buradaki kontrol kullanıcıya HANGI durumla
        // karsilastigini anlatan bir mesaj vermek için -- aksi halde
        // ham bir 409 "Veri çakışması" alırdı.
        var assignedElsewhere = await _context.TicketTypeSections
            .AsNoTracking()
            .AnyAsync(
                ts => ts.SeatSectionId == request.SeatSectionId
                   && ts.TicketTypeId != request.TicketTypeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (assignedElsewhere)
        {
            return Result.Failure(TicketTypeErrors.SectionAlreadyAssigned);
        }

        // Idempotent: aynı bölüm ikinci kez atanirsa entity yok sayiyor.
        ticketType.AssignSection(request.SeatSectionId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// SILME

public sealed record DeleteTicketTypeCommand(Guid Id) : IRequest<Result>;

internal sealed class DeleteTicketTypeCommandHandler
    : IRequestHandler<DeleteTicketTypeCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteTicketTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(DeleteTicketTypeCommand request, CancellationToken cancellationToken)
    {
        var ticketType = await _context.TicketTypes
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (ticketType is null)
        {
            return Result.Failure(TicketTypeErrors.NotFound);
        }

        // Bu turden bilet satilmissa SILME.
        //
        // Satılmış biletler bu bilet turune referans veriyor. Silseydim
        // (soft delete bile olsa) kullanıcının biletinde "bilet türü:
        // bilinmiyor" yazardi ve iade hesabi yapilamazdi.
        //
        // Bunun yerine pasife alinabilir: yeni satış olmaz, mevcut
        // biletler geçerli kalır.
        var hasSoldTickets = await _context.EventSeats
            .AsNoTracking()
            .AnyAsync(
                es => es.TicketTypeId == request.Id && es.Status != EventSeatStatus.Available,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasSoldTickets)
        {
            return Result.Failure(TicketTypeErrors.HasSoldTickets);
        }

        ticketType.IsDeleted = true;
        ticketType.DeletedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// LISTELEME -- PDF: GET /api/v1/events/{eventId}/ticket-types

public sealed record TicketTypeDto(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    string PriceDisplay,
    int? Quota,
    bool IsActive,
    bool RequiresStudentVerification,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate,
    IReadOnlyList<Guid> AssignedSectionIds);

public sealed record GetTicketTypesQuery(Guid EventId)
    : IRequest<Result<IReadOnlyList<TicketTypeDto>>>;

internal sealed class GetTicketTypesQueryHandler
    : IRequestHandler<GetTicketTypesQuery, Result<IReadOnlyList<TicketTypeDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetTicketTypesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<IReadOnlyList<TicketTypeDto>>> Handle(
        GetTicketTypesQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _context.TicketTypes
            .AsNoTracking()
            .Where(t => t.EventId == request.EventId)
            .OrderBy(t => t.Price.Amount)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Price,
                t.Quota,
                t.IsActive,
                t.RequiresStudentVerification,
                t.SalesStartDate,
                t.SalesEndDate,
                SectionIds = t.Sections.Select(s => s.SeatSectionId).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Bicimlendirmeyi BELLEKTE yapıyorum, sorguda değil.
        //
        // Money.ToString() bir C# metodu; EF önü SQL'e ceviremez.
        // Sorgu içinde cagirsaydim "could not be translated" hatası
        // alırdım -- Sprint 4'te bu tuzaga dusmustuk.
        var dtos = items
            .Select(t => new TicketTypeDto(
                t.Id,
                t.Name,
                t.Price.Amount,
                t.Price.Currency,

                // Kullanıcıya gösterilecek biçim.
                // Sunucuda uretiyorum ki tüm istemcilerde aynı gorunsun.
                string.Create(CultureInfo.InvariantCulture, $"{t.Price.Amount:N2} {t.Price.Currency}"),

                t.Quota,
                t.IsActive,
                t.RequiresStudentVerification,
                t.SalesStartDate,
                t.SalesEndDate,
                t.SectionIds))
            .ToList();

        return Result.Success<IReadOnlyList<TicketTypeDto>>(dtos);
    }
}
