using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Permissions;
using well404.Shop;
using Xunit;

namespace well404.Economy.Tests
{
    public class DiscountServiceTests
    {
        private sealed class FakeActor : IPermissionActor
        {
            public string Id => "76561190000000000";
            public string Type => "player";
            public string DisplayName => "Tester";
            public string FullActorName => "Tester (76561190000000000)";
        }

        private sealed class FakePermissionChecker : IPermissionChecker
        {
            private readonly HashSet<string> m_Granted;

            public FakePermissionChecker(params string[] granted)
            {
                m_Granted = new HashSet<string>(granted);
            }

            public IReadOnlyCollection<IPermissionCheckProvider> PermissionCheckProviders { get; }
                = new List<IPermissionCheckProvider>();

            public IReadOnlyCollection<IPermissionStore> PermissionStores { get; }
                = new List<IPermissionStore>();

            public Task<PermissionGrantResult> CheckPermissionAsync(IPermissionActor actor, string permission)
                => Task.FromResult(m_Granted.Contains(permission)
                    ? PermissionGrantResult.Grant
                    : PermissionGrantResult.Default);

            public Task InitAsync() => Task.CompletedTask;
        }

        private static ShopCatalog Catalog(bool enabled, params (string perm, string mult)[] tiers)
        {
            var dict = new Dictionary<string, string?>
            {
                ["discounts:enabled"] = enabled ? "true" : "false"
            };
            foreach (var (perm, mult) in tiers)
            {
                dict["discounts:tiers:" + perm] = mult;
            }

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();

            return new ShopCatalog(configuration);
        }

        [Theory]
        [InlineData(100, 0.9, 90)]
        [InlineData(10, 0.5, 5)]
        [InlineData(99, 1.0, 99)]
        public void ApplyDiscount_RoundsToTwoDecimals(decimal price, decimal multiplier, decimal expected)
        {
            Assert.Equal(expected, DiscountService.ApplyDiscount(price, multiplier));
        }

        [Fact]
        public async Task Disabled_ReturnsFullPrice()
        {
            var service = new DiscountService(Catalog(enabled: false, ("well404.shop.vip", "0.9")),
                new FakePermissionChecker("well404.shop.vip"));
            Assert.Equal(1m, await service.GetMultiplierAsync(new FakeActor()));
        }

        [Fact]
        public async Task Enabled_PicksBestGrantedTier()
        {
            var service = new DiscountService(
                Catalog(enabled: true, ("well404.shop.vip", "0.9"), ("well404.shop.mvp", "0.8")),
                new FakePermissionChecker("well404.shop.vip", "well404.shop.mvp"));
            Assert.Equal(0.8m, await service.GetMultiplierAsync(new FakeActor()));
        }

        [Fact]
        public async Task Enabled_ButNoGrantedTier_ReturnsFullPrice()
        {
            var service = new DiscountService(
                Catalog(enabled: true, ("well404.shop.vip", "0.9")),
                new FakePermissionChecker());
            Assert.Equal(1m, await service.GetMultiplierAsync(new FakeActor()));
        }

        [Fact]
        public async Task Enabled_OnlyLowerTierGranted_AppliesThatTier()
        {
            var service = new DiscountService(
                Catalog(enabled: true, ("well404.shop.vip", "0.9"), ("well404.shop.mvp", "0.8")),
                new FakePermissionChecker("well404.shop.vip"));
            Assert.Equal(0.9m, await service.GetMultiplierAsync(new FakeActor()));
        }
    }
}
