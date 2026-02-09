using NUnit.Framework;
using Moq;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace ImmichFrame.Core.Tests.Logic.Pool;

[TestFixture]
public class PersonAssetsPoolTests // Renamed from PeopleAssetsPoolTests to match class name
{
    private Mock<IApiCache> _mockApiCache;
    private Mock<ImmichApi> _mockImmichApi;
    private Mock<IAccountSettings> _mockAccountSettings;
    private TestablePersonAssetsPool _personAssetsPool;

    private class TestablePersonAssetsPool : PersonAssetsPool
    {
        public TestablePersonAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings)
            : base(apiCache, immichApi, accountSettings) { }

        public Task<IEnumerable<AssetResponseDto>> TestLoadAssets(CancellationToken ct = default)
        {
            return base.LoadAssets(ct);
        }
    }

    [SetUp]
    public void Setup()
    {
        _mockApiCache = new Mock<IApiCache>();
        _mockImmichApi = new Mock<ImmichApi>(null, null);
        _mockAccountSettings = new Mock<IAccountSettings>();
        _personAssetsPool = new TestablePersonAssetsPool(_mockApiCache.Object, _mockImmichApi.Object, _mockAccountSettings.Object);

        _mockAccountSettings.SetupGet(s => s.People).Returns(new List<Guid>());
        _mockAccountSettings.SetupGet(s => s.ExcludedPeople).Returns(new List<Guid>());
    }

    private AssetResponseDto CreateAsset(string id, List<PersonWithFacesResponseDto>? people = null) =>
        new AssetResponseDto { Id = id, Type = AssetTypeEnum.IMAGE, People = people };
    private SearchResponseDto CreateSearchResult(List<AssetResponseDto> assets, int total) =>
        new SearchResponseDto { Assets = new SearchAssetResponseDto { Items = assets, Total = total } };

    [Test]
    public async Task LoadAssets_CallsSearchAssetsForEachPerson_AndPaginates()
    {
        // Arrange
        var person1Id = Guid.NewGuid();
        var person2Id = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.People).Returns(new List<Guid> { person1Id, person2Id });

        var batchSize = 1000; // From PersonAssetsPool.cs
        var p1AssetsPage1 = Enumerable.Range(0, batchSize).Select(i => CreateAsset($"p1_p1_{i}")).ToList();
        var p1AssetsPage2 = Enumerable.Range(0, 30).Select(i => CreateAsset($"p1_p2_{i}")).ToList();
        var p2AssetsPage1 = Enumerable.Range(0, 20).Select(i => CreateAsset($"p2_p1_{i}")).ToList();

        // Person 1 - Page 1
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person1Id) && d.Page == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(p1AssetsPage1, batchSize));
        // Person 1 - Page 2
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person1Id) && d.Page == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(p1AssetsPage2, 30));
        // Person 2 - Page 1
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person2Id) && d.Page == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(p2AssetsPage1, 20));

        // Act
        var result = (await _personAssetsPool.TestLoadAssets()).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(batchSize + 30 + 20));
        Assert.That(result.Any(a => a.Id == "p1_p1_0"));
        Assert.That(result.Any(a => a.Id == "p1_p2_29"));
        Assert.That(result.Any(a => a.Id == "p2_p1_19"));

        _mockImmichApi.Verify(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person1Id) && d.Page == 1), It.IsAny<CancellationToken>()), Times.Once);
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person1Id) && d.Page == 2), It.IsAny<CancellationToken>()), Times.Once);
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person2Id) && d.Page == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoadAssets_NoPeopleConfigured_ReturnsEmpty()
    {
        _mockAccountSettings.SetupGet(s => s.People).Returns(new List<Guid>());
        var result = (await _personAssetsPool.TestLoadAssets()).ToList();
        Assert.That(result, Is.Empty);
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task LoadAssets_PersonHasNoAssets_DoesNotAffectOthers()
    {
        var person1Id = Guid.NewGuid(); // Has assets
        var person2Id = Guid.NewGuid(); // No assets
        _mockAccountSettings.SetupGet(s => s.People).Returns(new List<Guid> { person1Id, person2Id });

        var p1Assets = Enumerable.Range(0, 10).Select(i => CreateAsset($"p1_{i}")).ToList();
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person1Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(p1Assets, 10));
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.PersonIds.Contains(person2Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(new List<AssetResponseDto>(), 0));

        var result = (await _personAssetsPool.TestLoadAssets()).ToList();
        Assert.That(result.Count, Is.EqualTo(10));
        Assert.That(result.All(a => a.Id.StartsWith("p1_")));
    }

    [Test]
    public async Task LoadAssets_ExcludesPeopleBasedOnAssetPeopleProperty()
    {
        // Arrange
        var includedPersonId = Guid.NewGuid();
        var excludedPersonId = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.People).Returns(new List<Guid> { includedPersonId });
        _mockAccountSettings.SetupGet(s => s.ExcludedPeople).Returns(new List<Guid> { excludedPersonId });

        var assets = new List<AssetResponseDto>
        {
            // Asset with only included person - should be kept
            CreateAsset("keep_1", new List<PersonWithFacesResponseDto>
            {
                new PersonWithFacesResponseDto { Id = includedPersonId.ToString() }
            }),
            // Asset with both included and excluded person - should be excluded
            CreateAsset("exclude_1", new List<PersonWithFacesResponseDto>
            {
                new PersonWithFacesResponseDto { Id = includedPersonId.ToString() },
                new PersonWithFacesResponseDto { Id = excludedPersonId.ToString() }
            }),
            // Asset with null People - should be kept
            CreateAsset("keep_2", null),
            // Asset with empty People - should be kept
            CreateAsset("keep_3", new List<PersonWithFacesResponseDto>())
        };

        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(assets, assets.Count));

        // Act
        var result = (await _personAssetsPool.TestLoadAssets()).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Any(a => a.Id == "keep_1"));
        Assert.That(result.Any(a => a.Id == "keep_2"));
        Assert.That(result.Any(a => a.Id == "keep_3"));
        Assert.That(result.All(a => a.Id != "exclude_1"));
    }

    [Test]
    public async Task LoadAssets_ExclusionIsCaseInsensitive()
    {
        // Arrange
        var includedPersonId = Guid.NewGuid();
        var excludedPersonId = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.People).Returns(new List<Guid> { includedPersonId });
        _mockAccountSettings.SetupGet(s => s.ExcludedPeople).Returns(new List<Guid> { excludedPersonId });

        var assets = new List<AssetResponseDto>
        {
            // Asset with excluded person ID in uppercase - should still be excluded
            CreateAsset("exclude_upper", new List<PersonWithFacesResponseDto>
            {
                new PersonWithFacesResponseDto { Id = excludedPersonId.ToString().ToUpperInvariant() }
            }),
            CreateAsset("keep_1", new List<PersonWithFacesResponseDto>
            {
                new PersonWithFacesResponseDto { Id = includedPersonId.ToString() }
            })
        };

        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(assets, assets.Count));

        // Act
        var result = (await _personAssetsPool.TestLoadAssets()).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result.Any(a => a.Id == "keep_1"));
        Assert.That(result.All(a => a.Id != "exclude_upper"));
    }
}
