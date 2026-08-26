using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;

namespace Ticketing.Infrastructure.Configuration;

public sealed class AppUrlOptions
{
    public const string SectionName = "AppUrls";

    [Required]
    [Url]
    public string Frontend { get; set; } = string.Empty;

    [Required]
    [Url]
    public string Api { get; set; } = string.Empty;
}

internal sealed class AppUrlProvider : IAppUrlProvider
{
    private readonly AppUrlOptions _options;

    public AppUrlProvider(IOptions<AppUrlOptions> options) => _options = options.Value;

    // TrimEnd('/') KRITIK: yapilandirmada adres "https://x.com/" seklinde
    // yazilirsa, link birlestirmesi "https://x.com//sifre-sifirla" olur.
    // Cift slash bazi sunucularda 404 verir ve hata ayiklamasi
    // sinir bozucudur. Tek yerde temizleyip sorunu kokten cozuyorum.
    public string FrontendUrl => _options.Frontend.TrimEnd('/');

    public string ApiUrl => _options.Api.TrimEnd('/');
}
