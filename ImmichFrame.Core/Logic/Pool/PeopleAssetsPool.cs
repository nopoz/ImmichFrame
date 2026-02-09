using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class PersonAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        // Load only included people's assets in parallel
        var includedTasks = accountSettings.People
            .Select(personId => LoadAssetsForPerson(personId, ct));

        var results = await Task.WhenAll(includedTasks);
        var personAssets = results.SelectMany(x => x);

        // Filter out assets that contain any excluded person
        // Each asset has a People collection (since we fetch with WithPeople=true)
        if (accountSettings.ExcludedPeople.Count > 0)
        {
            var excludedPersonIds = accountSettings.ExcludedPeople
                .Select(id => id.ToString().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            personAssets = personAssets.Where(asset =>
                asset.People == null ||
                asset.People.Count == 0 ||
                !asset.People.Any(person => excludedPersonIds.Contains(person.Id)));
        }

        return personAssets;
    }

    private async Task<List<AssetResponseDto>> LoadAssetsForPerson(Guid personId, CancellationToken ct)
    {
        var assets = new List<AssetResponseDto>();
        int page = 1;
        int batchSize = 1000;
        int lastPageCount;

        do
        {
            var metadataBody = new MetadataSearchDto
            {
                Page = page,
                Size = batchSize,
                PersonIds = [personId],
                Type = AssetTypeEnum.IMAGE,
                WithExif = true,
                WithPeople = true
            };

            var personInfo = await immichApi.SearchAssetsAsync(metadataBody, ct);

            lastPageCount = personInfo.Assets.Items.Count;

            assets.AddRange(personInfo.Assets.Items);
            page++;
        } while (lastPageCount == batchSize);

        return assets;
    }
}