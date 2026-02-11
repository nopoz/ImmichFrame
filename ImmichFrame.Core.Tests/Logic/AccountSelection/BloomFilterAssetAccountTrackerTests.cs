using Moq;
using NUnit.Framework;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.AccountSelection;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.Core.Tests.Logic.AccountSelection;

[TestFixture]
public class BloomFilterAssetAccountTrackerTests
{
    private Mock<IAccountImmichFrameLogic> _mockAccount1;
    private Mock<IAccountImmichFrameLogic> _mockAccount2;
    private BloomFilterAssetAccountTracker _tracker;

    [SetUp]
    public void Setup()
    {
        var logger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug))
            .CreateLogger<BloomFilterAssetAccountTracker>();
        _tracker = new BloomFilterAssetAccountTracker(logger);

        _mockAccount1 = new Mock<IAccountImmichFrameLogic>();
        _mockAccount1.Setup(a => a.GetTotalAssets()).ReturnsAsync(1000L);
        _mockAccount1.Setup(a => a.ToString()).Returns("Account1");

        _mockAccount2 = new Mock<IAccountImmichFrameLogic>();
        _mockAccount2.Setup(a => a.GetTotalAssets()).ReturnsAsync(1000L);
        _mockAccount2.Setup(a => a.ToString()).Returns("Account2");
    }

    [Test]
    public void ForAsset_SyncException_TriesNextAccount()
    {
        // Arrange: Record asset in both accounts
        var assetId = Guid.NewGuid().ToString();
        _tracker.RecordAssetLocation(_mockAccount1.Object, assetId).AsTask().Wait();
        _tracker.RecordAssetLocation(_mockAccount2.Object, assetId).AsTask().Wait();

        // Account1 throws synchronously, Account2 succeeds
        var callCount = 0;
        string ForAssetFunc(IAccountImmichFrameLogic account)
        {
            callCount++;
            if (account == _mockAccount1.Object)
                throw new AssetNotFoundException("Not found in Account1");
            return "success from Account2";
        }

        // Act
        var result = _tracker.ForAsset(assetId, ForAssetFunc);

        // Assert: Should catch exception and try Account2
        Assert.That(result, Is.EqualTo("success from Account2"));
        Assert.That(callCount, Is.EqualTo(2), "Should have tried both accounts");
    }

    [Test]
    public void ForAsset_WithAsyncFunc_DoesNotCatchExceptions_UseForAssetAsyncInstead()
    {
        // This test documents that ForAsset (sync) does NOT handle async exceptions.
        // Use ForAssetAsync for async operations instead.

        // Arrange
        var assetId = Guid.NewGuid().ToString();
        _tracker.RecordAssetLocation(_mockAccount1.Object, assetId).AsTask().Wait();
        _tracker.RecordAssetLocation(_mockAccount2.Object, assetId).AsTask().Wait();

        Task<string> ForAssetFunc(IAccountImmichFrameLogic account)
        {
            if (account == _mockAccount1.Object)
                return Task.FromException<string>(new ApiException("Not found", 400, "", null, null));
            return Task.FromResult("success");
        }

        // Act - ForAsset returns the faulted Task without throwing
        var resultTask = _tracker.ForAsset(assetId, ForAssetFunc);

        // Assert - Exception surfaces when awaited, proving sync method doesn't catch async exceptions
        Assert.ThrowsAsync<ApiException>(async () => await resultTask);
    }

    [Test]
    public async Task ForAssetAsync_Exception_TriesNextAccount()
    {
        // Test the new ForAssetAsync method properly catches exceptions and tries next account

        // Arrange
        var assetId = Guid.NewGuid().ToString();
        await _tracker.RecordAssetLocation(_mockAccount1.Object, assetId);
        await _tracker.RecordAssetLocation(_mockAccount2.Object, assetId);

        var account1Called = false;
        var account2Called = false;

        Task<string> ForAssetFunc(IAccountImmichFrameLogic account)
        {
            if (account == _mockAccount1.Object)
            {
                account1Called = true;
                return Task.FromException<string>(new ApiException("Not found or no asset.view access", 400, "", null, null));
            }
            account2Called = true;
            return Task.FromResult("success from Account2");
        }

        // Act
        var result = await _tracker.ForAssetAsync(assetId, ForAssetFunc);

        // Assert
        Assert.That(account1Called, Is.True, "Account1 should have been tried first");
        Assert.That(account2Called, Is.True, "Account2 should have been tried after Account1 failed");
        Assert.That(result, Is.EqualTo("success from Account2"));
    }
}
