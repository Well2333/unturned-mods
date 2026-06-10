using System.Threading.Tasks;
using OpenMod.API.Permissions;

namespace well404.Shop
{
    /// <summary>
    /// Resolves the buy-price multiplier for an actor based on the permission
    /// tiers in config. Disabled by default; when enabled, the best (lowest)
    /// multiplier among the actor's granted tiers applies. Registered as a
    /// plugin-scoped singleton in <see cref="ShopContainerConfigurator"/>.
    /// </summary>
    public class DiscountService
    {
        private readonly ShopCatalog m_Catalog;
        private readonly IPermissionChecker m_PermissionChecker;

        public DiscountService(ShopCatalog catalog, IPermissionChecker permissionChecker)
        {
            m_Catalog = catalog;
            m_PermissionChecker = permissionChecker;
        }

        /// <summary>Returns the multiplier in (0, 1] to apply to buy prices (1 = no discount).</summary>
        public async Task<decimal> GetMultiplierAsync(IPermissionActor actor)
        {
            var discounts = m_Catalog.Discounts;
            if (!discounts.Enabled || discounts.Tiers.Count == 0)
            {
                return 1m;
            }

            var best = 1m;
            foreach (var tier in discounts.Tiers)
            {
                if (tier.Value <= 0m || tier.Value >= best)
                {
                    continue;
                }

                if (await m_PermissionChecker.CheckPermissionAsync(actor, tier.Key) == PermissionGrantResult.Grant)
                {
                    best = tier.Value;
                }
            }

            return best;
        }

        public static decimal ApplyDiscount(decimal price, decimal multiplier)
            => decimal.Round(price * multiplier, 2, System.MidpointRounding.AwayFromZero);
    }
}
