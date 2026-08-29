using FluentValidation;
using MediatR;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Behaviors;

/// <summary>
/// Her komut/sorgu MediatR'a ulasmadan ONCE dogrulanir.
///
/// PIPELINE BEHAVIOR NEDIR?
///
/// MediatR'da bir istek handler'a giderken bir "boru hattindan" gecer.
/// Her behavior bu hattin bir halkasidir ve isteği hem oncesinde hem
/// sonrasinda isleyebilir. Rus matruskasi gibi ic ice gecerler:
///
///   İstek -> [Validation] -> [Logging] -> [Transaction] -> Handler
///
/// NEDEN HANDLER ICINDE DOGRULAMA YAPMIYORUM?
///
/// Yapabilirdik ama 100 handler'in 100'unde de aynı uc satiri yazmak
/// gerekirdi. Bir gün birinde unutulur ve dogrulanmamis veri sisteme
/// girer -- hem de sessizce.
///
/// Burada merkezi olarak yaptigimizda UNUTMAK IMKANSIZ hale geliyor:
/// validator varsa çalışır, yoksa istek gecer.
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

        // Bu istek için validator tanimlanmamissa doğrudan geç.
        // Her komutun dogrulamaya ihtiyaci yok (örneğin parametresiz
        // bir "Logout" komutu).
        if (!_validators.Any())
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);

        // TÜM validator'lari calistirip TÜM hatalari topluyorum.
        //
        // İlk hatada durup donseydim, kullanıcı formu 5 kez gonderip
        // 5 hatayi tek tek gorurdu. Hepsini birden dondugumuzde
        // formdaki tüm alanlar aynı anda kırmızı isaretlenebiliyor.
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

        // EXCEPTION FIRLATIYORUM, Result DONMUYORUM. NEDEN?
        //
        // Behavior'in donus tipi TResponse (yani Result veya Result<T>).
        // Result<T> uretmek için T'yi bilmem ve ona göre nesne olusturmam
        // gerekir -- bu ancak reflection ile yapilabilir ve hem yavas
        // hem kirilgan olur.
        //
        // Bunun yerine ozel bir exception firlatiyorum. GlobalExceptionHandler
        // bunu yakalayip 400 + Problem Details'a ceviriyor.
        //
        // Bu, "beklenen durumlar için exception kullanma" ilkesine
        // gorunurde aykiri ama burada BILINCLI bir odun: alternatif olan
        // reflection cozumu daha kötü. Ayrıca doğrulama hatası istegin
        // en basinda olur, sicak yolda (hot path) değil.
        throw new Common.Exceptions.ValidationException(
            failures.Select(f => new ValidationError(f.PropertyName, f.ErrorMessage)).ToList());
    }
}

/// <summary>Tek bir alan doğrulama hatası.</summary>
public sealed record ValidationError(string PropertyName, string ErrorMessage);
