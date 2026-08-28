using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Reporting;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Features.Outbox;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Reports;

/// <summary>Disa aktarilabilen rapor turleri. PDF Sprint 13.</summary>
public enum ReportType
{
    SalesSummary = 1,
    EventOccupancy = 2,
    RevenueByEvent = 3,
    TicketTypeSales = 4,
    PaymentStatuses = 5
}

// ===================================================================
// 1) TALEP: POST /api/v1/reports/export
// ===================================================================

/// <summary>
/// Rapor disa aktarimi TALEP EDER. Uretim arka planda yapilir.
/// </summary>
/// <remarks>
/// ==================================================================
/// PDF: "Rapor uretimi background job olarak calistirilmali ve
/// tamamlandiginda kullaniciya bildirim gonderilmelidir."
/// ==================================================================
/// Bu kural neden var? Cunku rapor uretimi UZUN SUREBILIR:
/// on binlerce satirlik bir Excel dosyasi olusturmak saniyeler alir.
///
/// Senkron yapsaydik:
///   - Kullanicinin tarayicisi dakikalarca beklerdi
///   - Ters vekil sunucu (nginx) zaman asimina ugratirdi
///   - Istek yarida kesilse bile sunucu uretmeye devam ederdi
///
/// Bu uc, yalnizca "talebi kuyruga aldim" der ve HEMEN doner.
/// Kullanici baska isine bakar, rapor hazir olunca bildirim alir.
///
/// ------------------------------------------------------------------
/// KUYRUGA ALMA YOLU: OUTBOX
/// ------------------------------------------------------------------
/// Hangfire'in BackgroundJob.Enqueue metodu da kullanilabilirdi. Ama
/// Sprint 9'da kurdugumuz Outbox altyapisi zaten tam olarak bu isi
/// yapiyor ve UC ONEMLI USTUNLUGU var:
///
///   1) Talep, VERITABANI TRANSACTION'I icinde kaydediliyor. Sunucu
///      tam o anda coksa bile talep kaybolmuyor.
///   2) Basarisiz uretim ustel geri cekilme ile yeniden deneniyor.
///   3) Bes denemeden sonra dead letter oluyor ve izleme ekraninda
///      gorunuyor.
///
/// Hangfire.Enqueue ile bunlarin hepsini ayrica kurmak gerekirdi.
/// ==================================================================
/// </remarks>
public sealed record ExportReportCommand(
    ReportType Type,
    ReportFormat Format,
    DateTimeOffset? From,
    DateTimeOffset? To) : IRequest<Result<Guid>>;

public sealed class ExportReportCommandValidator : AbstractValidator<ExportReportCommand>
{
    public ExportReportCommandValidator()
    {
        // Enum'un TANIMLI bir degeri mi?
        //
        // IsInEnum SART: istemci Type=99 gonderirse C# bunu sessizce
        // kabul eder (enum'lar aslinda int'tir) ve asagidaki switch
        // varsayilan dala duserdi. Erken ve net hata daha iyi.
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Format).IsInEnum();

        // Tarih araligi mantikli mi?
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("Bitis tarihi baslangictan once olamaz.");
    }
}

internal sealed class ExportReportCommandHandler
    : IRequestHandler<ExportReportCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ExportReportCommandHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        ExportReportCommand request,
        CancellationToken cancellationToken)
    {
        // ==============================================================
        // YETKI KONTROLU TALEP ANINDA -- ISLEME ANINDA DEGIL
        // ==============================================================
        // Bu cok onemli bir ayrim. Rapor arka planda uretilecek ve o
        // sirada HTTP baglami OLMAYACAK: ICurrentUser bos donecek.
        //
        // Yetkiyi burada dogruluyor ve kullanici kimligini payload'a
        // YAZIYORUZ. Isleyici o kimlikle uretim yapiyor.
        //
        // Kontrolu isleyiciye biraksaydik ya yetkisiz rapor uretilirdi
        // ya da hicbir rapor uretilemezdi.
        // ==============================================================
        var scopeResult = await ReportScopeResolver
            .ResolveAsync(_context, _currentUser, cancellationToken)
            .ConfigureAwait(false);

        if (!scopeResult.IsSuccess)
        {
            return Result.Failure<Guid>(scopeResult.Error);
        }

        var userId = _currentUser.UserId!.Value;

        var payload = new ReportExportPayload(
            Guid.CreateVersion7(),
            userId,
            request.Type,
            request.Format,
            request.From,
            request.To);

        _context.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.ReportExport,
            System.Text.Json.JsonSerializer.Serialize(payload)));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Talep kimligini donuyoruz.
        //
        // Kullanici bildirimi aldiginda bu kimlikle dosyaya
        // ulasabiliyor (GET /reports/exports/{id}).
        return Result.Success(payload.ExportId);
    }
}

// ===================================================================
// 2) ISLEME: Outbox isleyicisi
// ===================================================================

/// <summary>Outbox payload'i. Alan degistirmek eski mesajlari bozar.</summary>
public sealed record ReportExportPayload(
    Guid ExportId,
    Guid UserId,
    ReportType Type,
    ReportFormat Format,
    DateTimeOffset? From,
    DateTimeOffset? To);

/// <summary>
/// Rapor dosyasini uretir ve kullaniciya bildirim yazar.
/// PDF Sprint 13: "tamamlandiginda kullaniciya bildirim gonderilmelidir."
/// </summary>
internal sealed class ReportExportOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IReportExporter _exporter;
    private readonly IReportFileStore _fileStore;

    public ReportExportOutboxHandler(
        IApplicationDbContext context,
        IReportExporter exporter,
        IReportFileStore fileStore)
    {
        _context = context;
        _exporter = exporter;
        _fileStore = fileStore;
    }

    public string MessageType => OutboxMessageTypes.ReportExport;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<ReportExportPayload>(payload);

        // ==============================================================
        // IDEMPOTENCY: DOSYA ZATEN URETILDIYSE TEKRAR URETME
        // ==============================================================
        // Outbox "en az bir kez" garantisi veriyor. Kontrol olmasaydi
        // ayni rapor iki kez uretilir ve kullanici IKI bildirim alirdi.
        //
        // Dosyanin varligi, isin tamamlandiginin en dogrudan kaniti.
        // ==============================================================
        if (await _fileStore.ExistsAsync(data.ExportId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // ==============================================================
        // KULLANICI KIMLIGINI TASIYAN OZEL BIR BAGLAM
        // ==============================================================
        // Rapor sorgulari ICurrentUser uzerinden kapsam belirliyor.
        // Arka planda HTTP baglami yok -> ICurrentUser bos.
        //
        // Cozum: sorguyu, talebi yapan kullanicinin kimligiyle
        // calistirmak. Bunu IReportDataProvider uzerinden yapiyorum
        // (bkz. ReportDataProvider). Boylece kapsam kurallari
        // AYNEN korunuyor -- arka planda "her seyi gor" gibi bir
        // ayricalik YOK.
        // ==============================================================
        // Kapsami PAYLOAD'daki kullanici kimliginden cozuyoruz.
        //
        // ICurrentUser burada bos -- arka planda HTTP baglami yok.
        // Talep aninda dogrulanmis kimligi tasidigimiz icin yetki
        // kurallari aynen uygulanabiliyor.
        var scope = await ReportScopeResolver
            .ResolveForUserAsync(_context, data.UserId, cancellationToken)
            .ConfigureAwait(false);

        var table = await ReportTableBuilder
            .BuildAsync(_context, scope, data, cancellationToken)
            .ConfigureAwait(false);

        var file = _exporter.Export(table, data.Format);

        await _fileStore.SaveAsync(data.ExportId, file, cancellationToken).ConfigureAwait(false);

        // PDF: "tamamlandiginda kullaniciya bildirim gonderilmelidir."
        _context.Notifications.Add(Notification.Create(
            data.UserId,
            NotificationType.ReportReady,
            "Raporunuz hazir",
            $"{table.Title} raporu {data.Format} biciminde olusturuldu. " +
            $"{table.Rows.Count} satir iceriyor.",
            data.ExportId,
            $"/api/v1/reports/exports/{data.ExportId}"));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Uretilen rapor dosyalarini saklar.
/// </summary>
/// <remarks>
/// Arayuz Application'da; dosya sisteminde mi, S3'te mi, veritabaninda
/// mi saklandigini is mantigi bilmiyor.
/// </remarks>
public interface IReportFileStore
{
    Task SaveAsync(Guid exportId, ExportedReport report, CancellationToken cancellationToken);

    Task<ExportedReport?> GetAsync(Guid exportId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid exportId, CancellationToken cancellationToken);
}

// ===================================================================
// 3) SAHIPLIK DOGRULAMASI
// ===================================================================

/// <summary>
/// Bu rapor dosyasi istegi yapan kullaniciya mi ait?
/// </summary>
/// <remarks>
/// ==================================================================
/// TAHMIN EDILEMEZ KIMLIK, YETKI DEGILDIR
/// ==================================================================
/// exportId bir Guid v7 ve tahmin edilmesi pratikte imkansiz. Ama
/// buna guvenip yetki kontrolunu atlamak "gizlilik yoluyla guvenlik"
/// (security through obscurity) olurdu.
///
/// Kimlik bir yerden sizabilir: sunucu erisim loglari, tarayici
/// gecmisi, paylasilan bir ekran goruntusu, Referer basligi. Sizan
/// kimlikle baskasinin GELIR RAPORU indirilebilirdi.
///
/// ------------------------------------------------------------------
/// SAHIPLIGI NEREDEN BILIYORUZ?
/// ------------------------------------------------------------------
/// Ayri bir "raporlar" tablosu acmadim. Cunku bilgi ZATEN duruyor:
/// rapor hazir oldugunda SAHIBINE bir bildirim yaziliyor ve o
/// bildirimin RelatedEntityId alani exportId.
///
/// Yani "bu raporun bildirimi bu kullaniciya mi yazilmis?" sorusu,
/// sahiplik sorusunun ta kendisi. Var olan veriyi kullanmak, ayni
/// gercegi iki yerde tutmaktan iyi.
/// ==================================================================
/// </remarks>
public sealed record VerifyReportOwnershipQuery(Guid ExportId) : IRequest<Result<bool>>;

internal sealed class VerifyReportOwnershipQueryHandler
    : IRequestHandler<VerifyReportOwnershipQuery, Result<bool>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public VerifyReportOwnershipQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<bool>> Handle(
        VerifyReportOwnershipQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Success(false);
        }

        var sahibi = await _context.Notifications
            .AsNoTracking()
            .AnyAsync(
                n => n.UserId == userId
                  && n.Type == NotificationType.ReportReady
                  && n.RelatedEntityId == request.ExportId,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(sahibi);
    }
}
