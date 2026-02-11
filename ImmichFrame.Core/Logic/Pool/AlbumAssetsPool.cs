using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AlbumAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var albums = accountSettings.Albums ?? Enumerable.Empty<Guid>();

        if (!albums.Any())
            return Enumerable.Empty<AssetResponseDto>();

        var albumTasks = albums
            .Select(albumId => immichApi.GetAlbumInfoAsync(albumId, null, null, ct));

        var results = await Task.WhenAll(albumTasks);

        return results.SelectMany(album => album.Assets);
    }
}