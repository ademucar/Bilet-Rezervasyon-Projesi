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
        "seat_layout.not_found", "Oturma planı bulunamadı.");

    public static readonly Error HallNotFound = Error.NotFound(
        "seat_layout.hall_not_found", "Salon bulunamadı.");

    public static readonly Error SectionNotFound = Error.NotFound(
        "seat_layout.section_not_found", "Bölüm bulunamadı.");

    public static readonly Error InUse = Error.Conflict(
        "seat_layout.in_use",
        "Bu oturma planı bir etkinlik oturumunda kullanılıyor. Degistirilemez veya silinemez.");
}

// PLAN OLUSTURMA -- PDF: POST /api/v1/halls/{hallId}/seat-layouts

public sealed record CreateSeatLayoutCommand(Guid HallId, string Name, string? Description)
    : IRequest<Result<Guid>>;

public sealed class CreateSeatLayoutCommandValidator : AbstractValidator<CreateSeatLayoutCommand>
{
    public CreateSeatLayoutCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Plan adı zorunludur.")
            .MaximumLength(150).WithMessage("Plan adı en fazla 150 karakter olabilir.");

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
            // BURADA ONCEDEN KONTROL YAPMIYORUM, EXCEPTION YAKALIYORUM
            //
            // Venue ve Hall'da "önce sorgula, sonra ekle" yaptım.
            // Burada bilerek farklı davraniyorum ve sebebini yazıyorum:
            //
            // "Önce sorgula sonra ekle" YARISA ACIKTIR. Iki istek aynı
            // anda gelirse ikisi de "yok" görür, ikisi de eklemeye
            // çalışır; biri unique index'e takilir ve kullanıcı
            // anlamsiz bir "Veri çakışması" hatası alır.
            //
            // Veritabani kisitina GUVENIP exception'i yakalamak hem
            // yarissiz hem de bir sorgu daha az. Buna "iyimser ekleme"
            // (optimistic insert) denir.
            //
            // PDF is kuralı: "Aynı salonda aynı isimde iki oturma planı
            // bulunmamalidir." -> UNIQUE (HallId, Name) index'i.
            //
            // NOT: DbUpdateException'in unique ihlali OLDUGUNU varsayiyorum.
            // Baska bir kisit ihlali de aynı tipe duser. Daha kesin
            // ayrim için Npgsql'in PostgresException.SqlState değerine
            // (23505 = unique_violation) bakilabilir; Sprint 15'te
            // bunu bir yardimci metoda tasiyacagim.
            return Result.Failure<Guid>(Error.Conflict(
                "seat_layout.duplicate_name",
                "Bu salonda aynı isimde bir oturma planı zaten var."));
        }

        return Result.Success(layout.Id);
    }
}

// BOLUM EKLEME -- PDF: POST /api/v1/seat-layouts/{id}/sections

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
            .NotEmpty().WithMessage("Bölüm adı zorunludur.")
            .MaximumLength(100).WithMessage("Bölüm adı en fazla 100 karakter olabilir.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Gosterim sırası negatif olamaz.");

        // #RRGGBB bicimi. Frontend renk secicisi zaten bu bicimi uretiyor
        // ama API'ye doğrudan istek gonderilebilecegi için dogruluyorum.
        RuleFor(x => x.ColorHex)
            .Matches("^#[0-9A-Fa-f]{6}$")
            .WithMessage("Renk #RRGGBB biciminde olmalıdır. Ornek: #E63946")
            .When(x => !string.IsNullOrWhiteSpace(x.ColorHex));
    }
}

internal sealed class AddSectionCommandHandler : IRequestHandler<AddSectionCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public AddSectionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(AddSectionCommand request, CancellationToken cancellationToken)
    {
        // Sections'i INCLUDE ediyorum -- bu ŞART.
        //
        // SeatLayout.AddSection metodu, aynı isimde bölüm var mi diye
        // BELLEKTEKI _sections koleksiyonuna bakiyor. Include etmezsem
        // koleksiyon BOŞ gelir, çakışma kontrolü hiçbir sey yapmaz ve
        // aynı isimde ikinci bölüm eklenir.
        //
        // Bu, EF ile calisirken en sik yapilan sessiz hatalardan biri:
        // kod doğru görünür, test bile gecebilir (bellekte olusturulmus
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

        // Plan bir oturumda kullaniliyorsa yapısı degistirilemez.
        if (await IsLayoutInUseAsync(_context, request.SeatLayoutId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(SeatLayoutErrors.InUse);
        }

        // AddSection çakışma kontrolunu kendi içinde yapiyor ve
        // gerekirse DomainException firlatiyor. Global exception
        // handler bunu 422'ye ceviriyor.
        var section = layout.AddSection(request.Name, request.DisplayOrder, request.ColorHex);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(section.Id);
    }

    /// <summary>
    /// Bu plan bir etkinlik oturumunda kullanılıyor mu?
    ///
    /// PDF is kuralı: "Kullanılmış oturma planı fiziksel olarak
    /// silinmemelidir." Biz bir adim ileri gidip DEGISTIRILMESINI de
    /// engelliyorum.
    ///
    /// Neden? Plan degisirse o oturumun EventSeat kayitlari artık var
    /// olmayan koltuklara isaret eder. Bilet almis kullanıcının koltuğu
    /// ortadan kalkar. Silmek kadar yikici bir sonuç.
    /// </summary>
    internal static Task<bool> IsLayoutInUseAsync(
        IApplicationDbContext context,
        Guid seatLayoutId,
        CancellationToken cancellationToken)
        => context.EventSessions
            .AsNoTracking()
            .AnyAsync(s => s.SeatLayoutId == seatLayoutId, cancellationToken);
}

// KOLTUK URETIMI
// PDF: POST /api/v1/seat-layouts/{id}/generate-seats

/// <summary>
/// Bir bolume toplu koltuk üretir.
/// </summary>
/// <param name="RowLabels">
/// Sıra etiketleri. null ise "1, 2, 3..." kullanilir.
/// Gerçek salonlarda siralar genelde "A, B, C" diye adlandirilir.
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
    /// Tek seferde uretilebilecek maksimum koltuk sayısı.
    ///
    /// BU SINIR NEDEN VAR? -- Bir DoS korumasi
    ///
    /// Sinir olmasaydı:
    ///     { "rowCount": 100000, "seatsPerRow": 100000 }
    /// isteği 10 MILYAR koltuk uretmeye calisirdi. Sunucu bellegi
    /// tuketir, veritabani kilitlenir, sistem coker.
    ///
    /// 20.000, dunyanin en büyük kapalı salonlarindan bile büyük --
    /// mesru hiçbir kullanimi engellemiyor.
    /// </summary>
    public const int MaxSeatsPerOperation = 20_000;

    public GenerateSeatsCommandValidator()
    {
        RuleFor(x => x.RowCount)
            .GreaterThan(0).WithMessage("Sıra sayısı sıfırdan büyük olmalıdır.")
            .LessThanOrEqualTo(500).WithMessage("Sıra sayısı 500'u aşamaz.");

        RuleFor(x => x.SeatsPerRow)
            .GreaterThan(0).WithMessage("Sıra başına koltuk sayısı sıfırdan büyük olmalıdır.")
            .LessThanOrEqualTo(500).WithMessage("Sıra başına koltuk sayısı 500'u aşamaz.");

        RuleFor(x => x)
            .Must(x => (long)x.RowCount * x.SeatsPerRow <= MaxSeatsPerOperation)
            .WithMessage($"Tek seferde en fazla {MaxSeatsPerOperation} koltuk uretilebilir.")
            // Alan adını acikca veriyorum; yoksa hata boş anahtar altinda
            // döner ve frontend hangi alanı isaretleyecegini bilemez.
            .WithName("SeatCount");

        RuleFor(x => x.RowLabels)
            .Must((cmd, labels) => labels is null || labels.Count == cmd.RowCount)
            .WithMessage("Sıra etiketi sayısı, sıra sayısıyla eşleşmelidir.");
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
        // Sections VE Seats birlikte yükleniyor.
        //
        // Seats'e neden ihtiyacim var? SeatSection.GenerateSeats,
        // "bu bolumde zaten koltuk var mi?" diye BELLEKTEKI koleksiyona
        // bakiyor. Yuklemeseydim boş görür ve aynı bolume ikinci kez
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

        // Koltukları üret. Kural ihlallerinde DomainException firlar.
        section.GenerateSeats(request.RowCount, request.SeatsPerRow, request.RowLabels);

        // KAPASITE KONTROLU -- URETIMDEN SONRA, KAYITTAN ONCE
        //
        // PDF: "Koltuk kapasitesi salon kapasitesini asmamalidir."
        //
        // Sirayi dikkatle sectim: koltuklar BELLEKTE üretildi ama henüz
        // KAYDEDILMEDI. ValidateCapacity burada patlarsa hiçbir sey
        // veritabanina yazilmaz -- SaveChangesAsync'e hiç gelmeyiz.
        //
        // Tersini yapsaydim (önce kaydet sonra kontrol et), geçersiz
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
