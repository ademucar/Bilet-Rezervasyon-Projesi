using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.SeatLayouts;

internal static class SeatLayoutErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "seat_layout.not_found", "Oturma plani bulunamadi.");

    public static readonly Error HallNotFound = Error.NotFound(
        "seat_layout.hall_not_found", "Salon bulunamadi.");

    public static readonly Error SectionNotFound = Error.NotFound(
        "seat_layout.section_not_found", "Bolum bulunamadi.");

    public static readonly Error InUse = Error.Conflict(
        "seat_layout.in_use",
        "Bu oturma plani bir etkinlik oturumunda kullaniliyor. Degistirilemez veya silinemez.");
}

// ===================================================================
// PLAN OLUSTURMA -- PDF: POST /api/v1/halls/{hallId}/seat-layouts
// ===================================================================

public sealed record CreateSeatLayoutCommand(Guid HallId, string Name, string? Description)
    : IRequest<Result<Guid>>;

public sealed class CreateSeatLayoutCommandValidator : AbstractValidator<CreateSeatLayoutCommand>
{
    public CreateSeatLayoutCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Plan adi zorunludur.")
            .MaximumLength(150).WithMessage("Plan adi en fazla 150 karakter olabilir.");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description is not null);
    }
}

internal sealed class CreateSeatLayoutCommandHandler
    : IRequestHandler<CreateSeatLayoutCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateSeatLayoutCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(
        CreateSeatLayoutCommand request,
        CancellationToken cancellationToken)
    {
        var hallExists = await _context.Halls
            .AsNoTracking()
            .AnyAsync(h => h.Id == request.HallId, cancellationToken)
            .ConfigureAwait(false);

        if (!hallExists)
        {
            return Result.Failure<Guid>(SeatLayoutErrors.HallNotFound);
        }

        var layout = SeatLayout.Create(request.HallId, request.Name, request.Description);

        _context.SeatLayouts.Add(layout);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // ==========================================================
            // BURADA ONCEDEN KONTROL YAPMIYORUM, EXCEPTION YAKALIYORUM
            // ==========================================================
            // Venue ve Hall'da "once sorgula, sonra ekle" yaptim.
            // Burada bilerek farkli davraniyorum ve sebebini yaziyorum:
            //
            // "Once sorgula sonra ekle" YARISA ACIKTIR. Iki istek ayni
            // anda gelirse ikisi de "yok" gorur, ikisi de eklemeye
            // calisir; biri unique index'e takilir ve kullanici
            // anlamsiz bir "Veri cakismasi" hatasi alir.
            //
            // Veritabani kisitina GUVENIP exception'i yakalamak hem
            // yarissiz hem de bir sorgu daha az. Buna "iyimser ekleme"
            // (optimistic insert) denir.
            //
            // PDF is kurali: "Ayni salonda ayni isimde iki oturma plani
            // bulunmamalidir." -> UNIQUE (HallId, Name) index'i.
            //
            // NOT: DbUpdateException'in unique ihlali OLDUGUNU varsayiyorum.
            // Baska bir kisit ihlali de ayni tipe duser. Daha kesin
            // ayrim icin Npgsql'in PostgresException.SqlState degerine
            // (23505 = unique_violation) bakilabilir; Sprint 15'te
            // bunu bir yardimci metoda tasiyacagim.
            return Result.Failure<Guid>(Error.Conflict(
                "seat_layout.duplicate_name",
                "Bu salonda ayni isimde bir oturma plani zaten var."));
        }

        return Result.Success(layout.Id);
    }
}

// ===================================================================
// BOLUM EKLEME -- PDF: POST /api/v1/seat-layouts/{id}/sections
// ===================================================================

public sealed record AddSectionCommand(
    Guid SeatLayoutId,
    string Name,
    int DisplayOrder,
    string? ColorHex) : IRequest<Result<Guid>>;

public sealed class AddSectionCommandValidator : AbstractValidator<AddSectionCommand>
{
    public AddSectionCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bolum adi zorunludur.")
            .MaximumLength(100).WithMessage("Bolum adi en fazla 100 karakter olabilir.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Gosterim sirasi negatif olamaz.");

        // #RRGGBB bicimi. Frontend renk secicisi zaten bu bicimi uretiyor
        // ama API'ye dogrudan istek gonderilebilecegi icin dogruluyoruz.
        RuleFor(x => x.ColorHex)
            .Matches("^#[0-9A-Fa-f]{6}$")
            .WithMessage("Renk #RRGGBB biciminde olmalidir. Ornek: #E63946")
            .When(x => !string.IsNullOrWhiteSpace(x.ColorHex));
    }
}

internal sealed class AddSectionCommandHandler : IRequestHandler<AddSectionCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public AddSectionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(AddSectionCommand request, CancellationToken cancellationToken)
    {
        // Sections'i INCLUDE ediyorum -- bu SART.
        //
        // SeatLayout.AddSection metodu, ayni isimde bolum var mi diye
        // BELLEKTEKI _sections koleksiyonuna bakiyor. Include etmezsem
        // koleksiyon BOS gelir, cakisma kontrolu hicbir sey yapmaz ve
        // ayni isimde ikinci bolum eklenir.
        //
        // Bu, EF ile calisirken en sik yapilan sessiz hatalardan biri:
        // kod dogru gorunur, test bile gecebilir (bellekte olusturulmus
        // nesnede koleksiyon doludur), ama veritabanindan yuklenen
        // nesnede calismaz.
        var layout = await _context.SeatLayouts
            .Include(sl => sl.Sections)
            .FirstOrDefaultAsync(sl => sl.Id == request.SeatLayoutId, cancellationToken)
            .ConfigureAwait(false);

        if (layout is null)
        {
            return Result.Failure<Guid>(SeatLayoutErrors.NotFound);
        }

        // Plan bir oturumda kullaniliyorsa yapisi degistirilemez.
        if (await IsLayoutInUseAsync(_context, request.SeatLayoutId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(SeatLayoutErrors.InUse);
        }

        // AddSection cakisma kontrolunu kendi icinde yapiyor ve
        // gerekirse DomainException firlatiyor. Global exception
        // handler bunu 422'ye ceviriyor.
        var section = layout.AddSection(request.Name, request.DisplayOrder, request.ColorHex);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(section.Id);
    }

    /// <summary>
    /// Bu plan bir etkinlik oturumunda kullaniliyor mu?
    ///
    /// PDF is kurali: "Kullanilmis oturma plani fiziksel olarak
    /// silinmemelidir." Biz bir adim ileri gidip DEGISTIRILMESINI de
    /// engelliyoruz.
    ///
    /// Neden? Plan degisirse o oturumun EventSeat kayitlari artik var
    /// olmayan koltuklara isaret eder. Bilet almis kullanicinin koltugu
    /// ortadan kalkar. Silmek kadar yikici bir sonuc.
    /// </summary>
    internal static Task<bool> IsLayoutInUseAsync(
        IApplicationDbContext context,
        Guid seatLayoutId,
        CancellationToken cancellationToken)
        => context.EventSessions
            .AsNoTracking()
            .AnyAsync(s => s.SeatLayoutId == seatLayoutId, cancellationToken);
}

// ===================================================================
// KOLTUK URETIMI
// PDF: POST /api/v1/seat-layouts/{id}/generate-seats
// ===================================================================

/// <summary>
/// Bir bolume toplu koltuk uretir.
/// </summary>
/// <param name="RowLabels">
/// Sira etiketleri. null ise "1, 2, 3..." kullanilir.
/// Gercek salonlarda siralar genelde "A, B, C" diye adlandirilir.
/// </param>
public sealed record GenerateSeatsCommand(
    Guid SeatLayoutId,
    Guid SectionId,
    int RowCount,
    int SeatsPerRow,
    IReadOnlyList<string>? RowLabels) : IRequest<Result<int>>;

public sealed class GenerateSeatsCommandValidator : AbstractValidator<GenerateSeatsCommand>
{
    /// <summary>
    /// Tek seferde uretilebilecek maksimum koltuk sayisi.
    ///
    /// ==================================================================
    /// BU SINIR NEDEN VAR? -- Bir DoS korumasi
    /// ==================================================================
    /// Sinir olmasaydi:
    ///     { "rowCount": 100000, "seatsPerRow": 100000 }
    /// istegi 10 MILYAR koltuk uretmeye calisirdi. Sunucu bellegi
    /// tuketir, veritabani kilitlenir, sistem coker.
    ///
    /// 20.000, dunyanin en buyuk kapali salonlarindan bile buyuk --
    /// mesru hicbir kullanimi engellemiyor.
    /// ==================================================================
    /// </summary>
    public const int MaxSeatsPerOperation = 20_000;

    public GenerateSeatsCommandValidator()
    {
        RuleFor(x => x.RowCount)
            .GreaterThan(0).WithMessage("Sira sayisi sifirdan buyuk olmalidir.")
            .LessThanOrEqualTo(500).WithMessage("Sira sayisi 500'u asamaz.");

        RuleFor(x => x.SeatsPerRow)
            .GreaterThan(0).WithMessage("Sira basina koltuk sayisi sifirdan buyuk olmalidir.")
            .LessThanOrEqualTo(500).WithMessage("Sira basina koltuk sayisi 500'u asamaz.");

        RuleFor(x => x)
            .Must(x => (long)x.RowCount * x.SeatsPerRow <= MaxSeatsPerOperation)
            .WithMessage($"Tek seferde en fazla {MaxSeatsPerOperation} koltuk uretilebilir.")
            // Alan adini acikca veriyorum; yoksa hata bos anahtar altinda
            // doner ve frontend hangi alani isaretleyecegini bilemez.
            .WithName("SeatCount");

        RuleFor(x => x.RowLabels)
            .Must((cmd, labels) => labels is null || labels.Count == cmd.RowCount)
            .WithMessage("Sira etiketi sayisi, sira sayisiyla eslesmelidir.");
    }
}

internal sealed class GenerateSeatsCommandHandler
    : IRequestHandler<GenerateSeatsCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;

    public GenerateSeatsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<int>> Handle(
        GenerateSeatsCommand request,
        CancellationToken cancellationToken)
    {
        // Sections VE Seats birlikte yukleniyor.
        //
        // Seats'e neden ihtiyacim var? SeatSection.GenerateSeats,
        // "bu bolumde zaten koltuk var mi?" diye BELLEKTEKI koleksiyona
        // bakiyor. Yuklemeseydim bos gorur ve ayni bolume ikinci kez
        // koltuk uretilirdi -> unique index ihlali -> anlamsiz 409.
        var layout = await _context.SeatLayouts
            .Include(sl => sl.Sections)
                .ThenInclude(s => s.Seats)
            .FirstOrDefaultAsync(sl => sl.Id == request.SeatLayoutId, cancellationToken)
            .ConfigureAwait(false);

        if (layout is null)
        {
            return Result.Failure<int>(SeatLayoutErrors.NotFound);
        }

        if (await AddSectionCommandHandler
                .IsLayoutInUseAsync(_context, request.SeatLayoutId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure<int>(SeatLayoutErrors.InUse);
        }

        var section = layout.Sections.FirstOrDefault(s => s.Id == request.SectionId);

        if (section is null)
        {
            return Result.Failure<int>(SeatLayoutErrors.SectionNotFound);
        }

        // Koltuklari uret. Kural ihlallerinde DomainException firlar.
        section.GenerateSeats(request.RowCount, request.SeatsPerRow, request.RowLabels);

        // ==============================================================
        // KAPASITE KONTROLU -- URETIMDEN SONRA, KAYITTAN ONCE
        // ==============================================================
        // PDF: "Koltuk kapasitesi salon kapasitesini asmamalidir."
        //
        // Sirayi dikkatle sectim: koltuklar BELLEKTE uretildi ama henuz
        // KAYDEDILMEDI. ValidateCapacity burada patlarsa hicbir sey
        // veritabanina yazilmaz -- SaveChangesAsync'e hic gelmeyiz.
        //
        // Tersini yapsaydim (once kaydet sonra kontrol et), gecersiz
        // veriyi yazip sonra geri almam gerekirdi.
        var hallCapacity = await _context.Halls
            .AsNoTracking()
            .Where(h => h.Id == layout.HallId)
            .Select(h => h.Capacity)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        layout.ValidateCapacity(hallCapacity);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(section.Seats.Count);
    }
}
