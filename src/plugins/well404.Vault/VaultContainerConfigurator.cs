using Autofac;
using OpenMod.API.Plugins;

namespace well404.Vault
{
    /// <summary>
    /// Registers the Vault's concrete service into the plugin's container. It has no
    /// <c>[Service]</c> interface, so it is registered explicitly here (the
    /// <c>[ServiceImplementation]</c> attribute only registers types that implement a service).
    /// </summary>
    public class VaultContainerConfigurator : IPluginContainerConfigurator
    {
        public void ConfigureContainer(IPluginServiceConfigurationContext context)
        {
            context.ContainerBuilder.RegisterType<VaultService>().AsSelf().SingleInstance();
        }
    }
}
