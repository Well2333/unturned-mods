using Autofac;
using OpenMod.API.Plugins;

namespace well404.Shop
{
    /// <summary>
    /// Registers the Shop's concrete helper services into the plugin's container.
    /// These have no <c>[Service]</c> interface, so they are registered explicitly
    /// here (the <c>[ServiceImplementation]</c> attributes only register types that
    /// implement a service interface).
    /// </summary>
    public class ShopContainerConfigurator : IPluginContainerConfigurator
    {
        public void ConfigureContainer(IPluginServiceConfigurationContext context)
        {
            context.ContainerBuilder.RegisterType<ShopCatalog>().AsSelf().SingleInstance();
            context.ContainerBuilder.RegisterType<DiscountService>().AsSelf().SingleInstance();
            context.ContainerBuilder.RegisterType<ShopService>().AsSelf().SingleInstance();
            context.ContainerBuilder.RegisterType<ShopTradeCoordinator>().AsSelf().SingleInstance();
        }
    }
}
