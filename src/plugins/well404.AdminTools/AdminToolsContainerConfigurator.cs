using Autofac;
using OpenMod.API.Plugins;

namespace well404.AdminTools
{
    /// <summary>Registers the plugin's services as plugin-scoped singletons.</summary>
    public class AdminToolsContainerConfigurator : IPluginContainerConfigurator
    {
        public void ConfigureContainer(IPluginServiceConfigurationContext context)
        {
            context.ContainerBuilder.RegisterType<GodModeService>().AsSelf().SingleInstance();
            context.ContainerBuilder.RegisterType<AdminToolsService>().AsSelf().SingleInstance();
        }
    }
}
