using System.Threading.Tasks;
using OpenMod.API.Eventing;
using OpenMod.Unturned.Players.Life.Events;
using well404.Essentials.Data;

namespace well404.Essentials.Back
{
    /// <summary>
    /// Records each player's death point so <c>/back</c> can return them to it. Auto-discovered
    /// and subscribed by OpenMod (like the Economy kill-reward listeners).
    /// </summary>
    public class DeathLocationListener : IEventListener<UnturnedPlayerDeathEvent>
    {
        private readonly PlayerDataStore m_PlayerData;

        public DeathLocationListener(PlayerDataStore playerData)
        {
            m_PlayerData = playerData;
        }

        public async Task HandleEventAsync(object? sender, UnturnedPlayerDeathEvent @event)
        {
            var pos = @event.DeathPosition; // System.Numerics.Vector3
            var location = new PlayerLocation(pos.X, pos.Y, pos.Z, 0f);
            await m_PlayerData.SetLastDeathAsync(@event.Player.SteamId.ToString(), location);
        }
    }

    /// <summary>
    /// Cancels incoming damage while a player is under post-<c>/back</c> immunity.
    /// </summary>
    public class InvincibilityDamageListener : IEventListener<UnturnedPlayerDamagingEvent>
    {
        private readonly InvincibilityService m_Invincibility;

        public InvincibilityDamageListener(InvincibilityService invincibility)
        {
            m_Invincibility = invincibility;
        }

        public Task HandleEventAsync(object? sender, UnturnedPlayerDamagingEvent @event)
        {
            if (m_Invincibility.IsProtected(@event.Player.SteamId.m_SteamID))
            {
                @event.IsCancelled = true;
            }

            return Task.CompletedTask;
        }
    }
}
