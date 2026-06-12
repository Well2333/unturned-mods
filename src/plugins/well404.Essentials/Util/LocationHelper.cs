using OpenMod.Unturned.Players;
using SDG.Unturned;
using well404.Essentials.Data;

namespace well404.Essentials.Util
{
    /// <summary>Captures a player's current world position + facing. Call on the main thread.</summary>
    public static class LocationHelper
    {
        public static PlayerLocation FromPlayer(Player player)
        {
            var pos = player.transform.position;
            var yaw = player.transform.eulerAngles.y;
            return new PlayerLocation(pos.x, pos.y, pos.z, yaw);
        }

        public static PlayerLocation FromPlayer(UnturnedPlayer player) => FromPlayer(player.Player);
    }
}
