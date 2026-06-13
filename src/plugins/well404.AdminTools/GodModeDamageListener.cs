using System.Threading.Tasks;
using OpenMod.API.Eventing;
using OpenMod.Unturned.Players.Life.Events;

namespace well404.AdminTools
{
    /// <summary>
    /// Cancels incoming damage for players with godmode on. Auto-discovered and registered by
    /// OpenMod (same pattern as Essentials' invincibility listener).
    /// </summary>
    public class GodModeDamageListener : IEventListener<UnturnedPlayerDamagingEvent>
    {
        private readonly GodModeService m_God;

        public GodModeDamageListener(GodModeService god)
        {
            m_God = god;
        }

        public Task HandleEventAsync(object? sender, UnturnedPlayerDamagingEvent @event)
        {
            if (m_God.IsGod(@event.Player.SteamId.m_SteamID))
            {
                @event.IsCancelled = true;
            }

            return Task.CompletedTask;
        }
    }
}
