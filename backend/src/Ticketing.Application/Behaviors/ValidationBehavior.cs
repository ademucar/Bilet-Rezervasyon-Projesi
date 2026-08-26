using FluentValidation;
using MediatR;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Behaviors;

/// <summary>
/// Her komut/sorgu MediatR'a ulasmadan ONCE dogrulanir.
///
/// ==================================================================
/// PIPELINE BEHAVIOR NEDIR?
/// ==================================================================
/// MediatR'da bir istek handler'a giderken bir "boru hattindan" gecer.
/// Her behavior bu hattin bir halkasidir ve istegi hem oncesinde hem
/// sonrasinda isleyebilir. Rus matruskasi gibi ic ice gecerler:
///
///   Istek -> [Validation] -> [Logging] -> [Transaction] -> Handler
///
/// NEDEN HANDLER ICINDE DOGRULAMA YAPMIYORUZ?
///
/// Yapabilirdik ama 100 handler'in 100'unde de ayni uc satiri yazmak
/// gerekirdi. Bir gun birinde unutulur ve dogrulanmamis veri sisteme
/// girer -- hem de sessizce.
///
/// Burada merkezi olarak yaptigimizda UNUTMAK IMKANSIZ hale geliyor:
/// validator varsa calisir, yoksa istek gecer.
/// ==================================================================
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        // Bu istek icin validator tanimlanmamissa dogrudan gec.
        // Her komutun dogrulamaya ihtiyaci yok (ornegin parametresiz
        // bir "Logout" komutu).
        if (!_validators.Any())
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);

        // TUM validator'lari calistirip TUM hatalari topluyorum.
        //
        // Ilk hatada durup donseydim, kullanici formu 5 kez gonderip
        // 5 hatayi tek tek gorurdu. Hepsini birden dondugumuzde
        // formdaki tum alanlar ayni anda kirmizi isaretlenebiliyor.
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)))
            .ConfigureAwait(false);

        var failures = validationResults
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        // ------------------------------------------------------------------
        // EXCEPTION FIRLATIYORUM, Result DONMUYORUM. NEDEN?
        // ------------------------------------------------------------------
        // Behavior'in donus tipi TResponse (yani Result veya Result<T>).
        // Result<T> uretmek icin T'yi bilmem ve ona gore nesne olusturmam
        // gerekir -- bu ancak reflection ile yapilabilir ve hem yavas
        // hem kirilgan olur.
        //
        // Bunun yerine ozel bir exception firlatiyorum. GlobalExceptionHandler
        // bunu yakalayip 400 + Problem Details'a ceviriyor.
        //
        // Bu, "beklenen durumlar icin exception kullanma" ilkesine
        // gorunurde aykiri ama burada BILINCLI bir odun: alternatif olan
        // reflection cozumu daha kotu. Ayrica dogrulama hatasi istegin
        // en basinda olur, sicak yolda (hot path) degil.
        throw new Common.Exceptions.ValidationException(
            failures.Select(f => new ValidationError(f.PropertyName, f.ErrorMessage)).ToList());
    }
}

/// <summary>Tek bir alan dogrulama hatasi.</summary>
public sealed record ValidationError(string PropertyName, string ErrorMessage);
