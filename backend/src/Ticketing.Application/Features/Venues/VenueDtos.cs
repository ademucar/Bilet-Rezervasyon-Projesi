namespace Ticketing.Application.Features.Venues;

/// <summary>
/// Mekan listesi ogesi.
///
/// Listede DETAY alanlari (adres, koordinat) YOK -- yalnizca detay
/// sayfasinda var. Neden? Liste sorgusu 100 kayit donebilir; her birine
/// 500 karakterlik adres eklemek yaniti gereksiz sisirir. Kullanici
/// zaten listede adresi okumuyor, isme bakip tikliyor.
/// </summary>
public sealed record VenueListItem(
    Guid Id,
    string Name,
    string CityName,
    int HallCount);

public sealed record VenueDetail(
    Guid Id,
    string Name,
    string Address,
    Guid CityId,
    string CityName,
    decimal? Latitude,
    decimal? Longitude,
    IReadOnlyList<HallSummary> Halls);

public sealed record HallSummary(
    Guid Id,
    string Name,
    int Capacity,
    int SeatLayoutCount);
