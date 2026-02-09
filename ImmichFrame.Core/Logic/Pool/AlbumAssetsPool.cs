using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AlbumAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        // Load excluded and included albums in parallel
        var excludedTasks = accountSettings.ExcludedAlbums
            .Select(albumId => immichApi.GetAlbumInfoAsync(albumId, null, null, ct));
        var includedTasks = accountSettings.Albums
            .Select(albumId => immichApi.GetAlbumInfoAsync(albumId, null, null, ct));

        var allTasks = excludedTasks.Concat(includedTasks).ToList();
        var results = await Task.WhenAll(allTasks);

        var excludedCount = accountSettings.ExcludedAlbums.Count;
        var excludedIds = results.Take(excludedCount)
            .SelectMany(album => album.Assets)
            .Select(a => a.Id)
            .ToHashSet();

        var albumAssets = results.Skip(excludedCount).SelectMany(album => album.Assets);

        return albumAssets.Where(a => !excludedIds.Contains(a.Id));
    }
}