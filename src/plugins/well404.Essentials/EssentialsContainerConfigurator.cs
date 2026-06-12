using Autofac;
using OpenMod.API.Plugins;
using well404.Essentials.Back;
using well404.Essentials.Data;
using well404.Essentials.Gift;
using well404.Essentials.Sleep;
using well404.Essentials.Teleport;
using well404.Essentials.Tp;
using well404.Essentials.Warps;

namespace well404.Essentials
{
    /// <summary>
    /// Registers Essentials' concrete helper services into the plugin's container. These have no
    /// <c>[Service]</c> interface, so they are registered explicitly here (the
    /// <c>[ServiceImplementation]</c> attributes only register types that implement a service
    /// interface). All are plugin-scoped singletons so state (cooldowns, pending requests, votes,
    /// the in-memory config) is shared across commands, listeners and the web panel.
    /// </summary>
    public class EssentialsContainerConfigurator : IPluginContainerConfigurator
    {
        public void ConfigureContainer(IPluginServiceConfigurationContext context)
        {
            var builder = context.ContainerBuilder;
            builder.RegisterType<EssentialsConfigStore>().AsSelf().SingleInstance();
            builder.RegisterType<PlayerDataStore>().AsSelf().SingleInstance();
            builder.RegisterType<CooldownManager>().AsSelf().SingleInstance();
            builder.RegisterType<TeleportService>().AsSelf().SingleInstance();
            builder.RegisterType<TeleportRequestManager>().AsSelf().SingleInstance();
            builder.RegisterType<WarpService>().AsSelf().SingleInstance();
            builder.RegisterType<GiftService>().AsSelf().SingleInstance();
            builder.RegisterType<SleepVoteService>().AsSelf().SingleInstance();
            builder.RegisterType<InvincibilityService>().AsSelf().SingleInstance();
        }
    }
}
