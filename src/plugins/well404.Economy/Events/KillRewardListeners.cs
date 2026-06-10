using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenMod.API.Eventing;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Animals.Events;
using OpenMod.Unturned.Players.Life.Events;
using OpenMod.Unturned.Zombies.Events;

namespace well404.Economy.Events
{
    /// <summary>
    /// Shared helper for the kill-reward listeners: reads config and deposits the
    /// reward into the killer's account, swallowing/logging errors so a reward
    /// failure never disrupts gameplay.
    /// </summary>
    internal static class KillRewardHelper
    {
        public static KillRewardSettings ReadSettings(IConfiguration configuration)
            => (configuration.Get<EconomySettings>() ?? new EconomySettings()).KillRewards;

        public static async Task RewardAsync(
            IEconomyProvider economy, ILogger logger, string steamId, decimal amount, string reason)
        {
            try
            {
                await economy.UpdateBalanceAsync(steamId, KnownActorTypes.Player, amount, reason);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to grant kill reward '{Reason}' to {SteamId}.", reason, steamId);
            }
        }
    }

    public class PlayerKillRewardListener : IEventListener<UnturnedPlayerDeathEvent>
    {
        private readonly IEconomyProvider m_Economy;
        private readonly IConfiguration m_Configuration;
        private readonly ILogger<PlayerKillRewardListener> m_Logger;

        public PlayerKillRewardListener(
            IEconomyProvider economy, IConfiguration configuration, ILogger<PlayerKillRewardListener> logger)
        {
            m_Economy = economy;
            m_Configuration = configuration;
            m_Logger = logger;
        }

        public async Task HandleEventAsync(object? sender, UnturnedPlayerDeathEvent @event)
        {
            var settings = KillRewardHelper.ReadSettings(m_Configuration);
            if (!settings.Enabled || settings.Player <= 0m)
            {
                return;
            }

            var instigator = @event.Instigator;
            // Ignore environment deaths and suicides.
            if (instigator.m_SteamID == 0 || instigator.m_SteamID == @event.Player.SteamId.m_SteamID)
            {
                return;
            }

            await KillRewardHelper.RewardAsync(
                m_Economy, m_Logger, instigator.ToString(), settings.Player, "kill_player");
        }
    }

    public class ZombieKillRewardListener : IEventListener<UnturnedZombieDamagingEvent>
    {
        private readonly IEconomyProvider m_Economy;
        private readonly IConfiguration m_Configuration;
        private readonly ILogger<ZombieKillRewardListener> m_Logger;

        public ZombieKillRewardListener(
            IEconomyProvider economy, IConfiguration configuration, ILogger<ZombieKillRewardListener> logger)
        {
            m_Economy = economy;
            m_Configuration = configuration;
            m_Logger = logger;
        }

        public async Task HandleEventAsync(object? sender, UnturnedZombieDamagingEvent @event)
        {
            // Cheap guards first so the common (non-lethal) hit returns without awaiting.
            if (@event.IsCancelled || @event.Instigator == null)
            {
                return;
            }

            // The damaging event fires before damage is applied, so Health is the pre-hit value.
            if (@event.DamageAmount < @event.Zombie.Health)
            {
                return;
            }

            var settings = KillRewardHelper.ReadSettings(m_Configuration);
            if (!settings.Enabled)
            {
                return;
            }

            var isMega = @event.Zombie.Zombie.isMega;
            var reward = isMega ? settings.MegaZombie : settings.Zombie;
            if (reward <= 0m)
            {
                return;
            }

            await KillRewardHelper.RewardAsync(
                m_Economy, m_Logger, @event.Instigator.SteamId.ToString(), reward,
                isMega ? "kill_megazombie" : "kill_zombie");
        }
    }

    public class AnimalKillRewardListener : IEventListener<UnturnedAnimalDamagingEvent>
    {
        private readonly IEconomyProvider m_Economy;
        private readonly IConfiguration m_Configuration;
        private readonly ILogger<AnimalKillRewardListener> m_Logger;

        public AnimalKillRewardListener(
            IEconomyProvider economy, IConfiguration configuration, ILogger<AnimalKillRewardListener> logger)
        {
            m_Economy = economy;
            m_Configuration = configuration;
            m_Logger = logger;
        }

        public async Task HandleEventAsync(object? sender, UnturnedAnimalDamagingEvent @event)
        {
            if (@event.IsCancelled || @event.Instigator.m_SteamID == 0)
            {
                return;
            }

            if (@event.DamageAmount < @event.Animal.Health)
            {
                return;
            }

            var settings = KillRewardHelper.ReadSettings(m_Configuration);
            if (!settings.Enabled || settings.Animal <= 0m)
            {
                return;
            }

            await KillRewardHelper.RewardAsync(
                m_Economy, m_Logger, @event.Instigator.ToString(), settings.Animal, "kill_animal");
        }
    }
}
