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

/// <summary>Disa aktarilabilen rapor türleri. PDF Sprint 13.</summary>
public enum ReportType
{
    SalesSummary = 1,
    EventOccupancy = 2,
    RevenueByEvent = 3,
    TicketTypeSales = 4,
    PaymentStatuses = 5,
}

// ===================================================================
// 1) TALEP: POST /api/v1/reports/export
// ===================================================================

/// <summary>
/// Rapor disa aktarimi TALEP EDER. Üretim arka planda yapilir.
/// </summary>
/// <remarks>
/// ==================================================================
/// PDF: "Rapor üretimi background job olarak calistirilmali ve
/// tamamlandiginda kullanıcıya bildirim gonderilmelidir."
/// ==================================================================
/// Bu kural neden var? Çünkü rapor üretimi UZUN SUREBILIR:
/// on binlerce satirlik bir Excel dosyasi olusturmak saniyeler alır.
///
/// Senkron yapsaydik:
///   - Kullanıcının tarayicisi dakikalarca beklerdi
///   - Ters vekil sunucu (nginx) zaman asimina ugratirdi
///   - İstek yarida kesilse bile sunucu uretmeye devam ederdi
///
/// Bu uc, yalnızca "talebi kuyruga aldim" der ve HEMEN döner.
/// Kullanıcı başka isine bakar, rapor hazır olunca bildirim alır.
///
/// ------------------------------------------------------------------
/// KUYRUGA ALMA YOLU: OUTBOX
/// ------------------------------------------------------------------
/// Hangfire'in BackgroundJob.Enqueue metodu da kullanilabilirdi. Ama
/// Sprint 9'da kurdugumuz Outbox altyapisi zaten tam olarak bu isi
/// yapiyor ve UC ONEMLI USTUNLUGU var:
///
///   1) Talep, VERITABANI TRANSACTION'I içinde kaydediliyor. Sunucu
///      tam o anda coksa bile talep kaybolmuyor.
///   2) Başarısız üretim ustel geri cekilme ile yeniden deneniyor.
///   3) Bes denemeden sonra dead letter oluyor ve izleme ekraninda
///      görünüyor.
///
/// Hangfire.Enqueue ile bunlarin hepsini ayrıca kurmak gerekirdi.
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
        // Enum'un TANIMLI bir değeri mi?
        //
        // IsInEnum ŞART: istemci Type=99 gonderirse C# bunu sessizce
        // kabul eder (enum'lar aslında int'tir) ve aşağıdaki switch
        // varsayılan dala duserdi. Erken ve net hata daha iyi.
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Format).IsInEnum();

        // Tarih aralığı mantikli mi?
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("Bitiş tarihi baslangictan önce olamaz.");
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
        // YETKI KONTROLU TALEP ANINDA -- ISLEME ANINDA DEĞİL
        // ==============================================================
        // Bu çok önemli bir ayrim. Rapor arka planda uretilecek ve o
        // sırada HTTP baglami OLMAYACAK: ICurrentUser boş donecek.
        //
        // Yetkiyi burada dogruluyor ve kullanıcı kimligini payload'a
        // YAZIYORUZ. Isleyici o kimlikle üretim yapiyor.
        //
        // Kontrolu isleyiciye biraksaydik ya yetkisiz rapor uretilirdi
        // ya da hiçbir rapor uretilemezdi.
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
        // Kullanıcı bildirimi aldiginda bu kimlikle dosyaya
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
/// Rapor dosyasini üretir ve kullanıcıya bildirim yazar.
/// PDF Sprint 13: "tamamlandiginda kullanıcıya bildirim gonderilmelidir."
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
        // Outbox "en az bir kez" garantisi veriyor. Kontrol olmasaydı
        // aynı rapor iki kez üretilir ve kullanıcı IKI bildirim alırdı.
        //
        // Dosyanin varligi, isin tamamlandiginin en doğrudan kaniti.
        // ==============================================================
        if (await _fileStore.ExistsAsync(data.ExportId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // ==============================================================
        // KULLANICI KIMLIGINI TASIYAN OZEL BIR BAGLAM
        // ==============================================================
        // Rapor sorgulari ICurrentUser üzerinden kapsam belirliyor.
        // Arka planda HTTP baglami yok -> ICurrentUser boş.
        //
        // Cozum: sorguyu, talebi yapan kullanıcının kimligiyle
        // calistirmak. Bunu IReportDataProvider üzerinden yapıyorum
        // (bkz. ReportDataProvider). Boylece kapsam kurallari
        // AYNEN korunuyor -- arka planda "her seyi gor" gibi bir
        // ayricalik YOK.
        // ==============================================================
        // Kapsami PAYLOAD'daki kullanıcı kimliginden cozuyoruz.
        //
        // ICurrentUser burada boş -- arka planda HTTP baglami yok.
        // Talep anında dogrulanmis kimliği tasidigimiz için yetki
        // kurallari aynen uygulanabiliyor.
        var scope = await ReportScopeResolver
            .ResolveForUserAsync(_context, data.UserId, cancellationToken)
            .ConfigureAwait(false);

        var table = await ReportTableBuilder
            .BuildAsync(_context, scope, data, cancellationToken)
            .ConfigureAwait(false);

        var file = _exporter.Export(table, data.Format);

        await _fileStore.SaveAsync(data.ExportId, file, cancellationToken).ConfigureAwait(false);

        // PDF: "tamamlandiginda kullanıcıya bildirim gonderilmelidir."
        _context.Notifications.Add(Notification.Create(
            data.UserId,
            NotificationType.ReportReady,
            "Raporunuz hazır",
            $"{table.Title} raporu {data.Format} biciminde oluşturuldu. " +
            $"{table.Rows.Count} satır iceriyor.",
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
/// mi saklandigini is mantığı bilmiyor.
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
/// Bu rapor dosyasi isteği yapan kullanıcıya mi ait?
/// </summary>
/// <remarks>
/// ==================================================================
/// TAHMIN EDILEMEZ KIMLIK, YETKI DEĞİLDİR
/// ==================================================================
/// exportId bir Guid v7 ve tahmin edilmesi pratikte imkansiz. Ama
/// buna guvenip yetki kontrolunu atlamak "gizlilik yoluyla güvenlik"
/// (security through obscurity) olurdu.
///
/// Kimlik bir yerden sizabilir: sunucu erişim loglari, tarayıcı
/// gecmisi, paylasilan bir ekran goruntusu, Referer başlığı. Sizan
/// kimlikle baskasinin GELIR RAPORU indirilebilirdi.
///
/// ------------------------------------------------------------------
/// SAHIPLIGI NEREDEN BILIYORUZ?
/// ------------------------------------------------------------------
/// Ayrı bir "raporlar" tablosu acmadim. Çünkü bilgi ZATEN duruyor:
/// rapor hazır olduğunda SAHIBINE bir bildirim yaziliyor ve o
/// bildirimin RelatedEntityId alanı exportId.
///
/// Yani "bu raporun bildirimi bu kullanıcıya mi yazilmis?" sorusu,
/// sahiplik sorusunun ta kendisi. Var olan veriyi kullanmak, aynı
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
