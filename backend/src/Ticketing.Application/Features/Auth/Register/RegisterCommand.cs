using MediatR;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.Register;

/// <summary>
/// Yeni kullanıcı kaydı. PDF: POST /api/v1/auth/register
///
/// Neden "record"? Çünkü bir komut bir veri tasiyicisidir, davranisi yoktur.
/// record bana deger esitligi ve degismezlik (immutability) veriyor --
/// yani bir komut olusturulduktan sonra handler'a giderken degistirilemez.
///
/// IRequest&lt;Result&lt;AuthResponse&gt;&gt;: bu komut calistiginda
/// Result&lt;AuthResponse&gt; donecegini tip seviyesinde belirtiyor.
/// Handler başka bir sey donemez -- derleyici engeller.
/// </summary>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber) : IRequest<Result<AuthResponse>>;
