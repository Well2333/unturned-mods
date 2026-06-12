using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;
using well404.Essentials.Data;
using well404.Essentials.Teleport;
using well404.Essentials.Warps;

namespace well404.Essentials
{
    /// <summary>
    /// The player-facing convenience surface for the web panel: lets a player teleport home, back
    /// to their last death point, and to any warp they have access to. Each destination is a card
    /// with a single teleport button; the teleport runs through the same <see cref="TeleportService"/>
    /// pipeline as the in-game commands (cooldown, optional fee, warmup). Registered optionally via
    /// <see cref="IPlayerMenuRegistry"/>.
    /// </summary>
    public sealed class EssentialsPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "essentials";

        private const string WarpPrefix = "warp:";

        private readonly PlayerDataStore m_PlayerData;
        private readonly WarpService m_Warps;
        private readonly TeleportService m_Teleport;
        private readonly IUserManager m_UserManager;

        public EssentialsPlayerMenu(
            PlayerDataStore playerData,
            WarpService warps,
            TeleportService teleport,
            IUserManager userManager)
        {
            m_PlayerData = playerData;
            m_Warps = warps;
            m_Teleport = teleport;
            m_UserManager = userManager;
        }

        public string Id => MenuId;

        public string Title => "便民";

        public string? Icon => "🧭";

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return new PlayerMenuView(Title, null, Array.Empty<PlayerCard>(), "你需要在线才能使用传送。");
            }

            var cards = new List<PlayerCard>();

            var home = await m_PlayerData.GetHomeAsync(context.SteamId);
            cards.Add(home != null
                ? new PlayerCard("home", "🏠 家", null, null,
                    new[] { new PlayerButton("go", "回家", "primary") })
                : new PlayerCard("home", "🏠 家", new[] { "尚未设置家，用 /sethome 设置。" }));

            var death = await m_PlayerData.GetLastDeathAsync(context.SteamId);
            if (death != null)
            {
                cards.Add(new PlayerCard("back", "↩ 返回死亡点", null, null,
                    new[] { new PlayerButton("go", "返回") }));
            }

            foreach (var warp in m_Warps.All)
            {
                if (await m_Warps.HasAccessAsync(user, warp.Name))
                {
                    cards.Add(new PlayerCard(WarpPrefix + warp.Name, "📍 " + warp.Name, null, null,
                        new[] { new PlayerButton("go", "传送") }));
                }
            }

            return new PlayerMenuView(Title, "点击即可传送（可能需要短暂站立读条）", cards);
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            if (actionId != "go")
            {
                return PlayerActionResult.Fail("未知操作。");
            }

            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return PlayerActionResult.Fail("你需要在线才能传送。");
            }

            PlayerLocation destination;
            TeleportKind kind;
            string cooldownKey;
            int? cooldownOverride = null;

            if (cardKey == "home")
            {
                var home = await m_PlayerData.GetHomeAsync(context.SteamId);
                if (home == null)
                {
                    return PlayerActionResult.Fail("你还没有设置家。");
                }

                destination = home;
                kind = TeleportKind.Home;
                cooldownKey = "home";
            }
            else if (cardKey == "back")
            {
                var death = await m_PlayerData.GetLastDeathAsync(context.SteamId);
                if (death == null)
                {
                    return PlayerActionResult.Fail("没有可返回的死亡点。");
                }

                destination = death;
                kind = TeleportKind.Back;
                cooldownKey = "back";
            }
            else if (cardKey.StartsWith(WarpPrefix, StringComparison.Ordinal))
            {
                var name = cardKey.Substring(WarpPrefix.Length);
                var warp = m_Warps.Find(name);
                if (warp == null)
                {
                    return PlayerActionResult.Fail("找不到该传送点。");
                }

                if (!await m_Warps.HasAccessAsync(user, warp.Name))
                {
                    return PlayerActionResult.Fail("你没有权限使用该传送点。");
                }

                destination = WarpService.ToLocation(warp);
                kind = TeleportKind.Warp;
                cooldownKey = WarpPrefix + warp.Name.ToLowerInvariant();
                cooldownOverride = warp.CooldownSeconds;
            }
            else
            {
                return PlayerActionResult.Fail("未知目的地。");
            }

            // TryTeleportAsync prints the specific reason (cooldown, fee, moved) to the player
            // in-game and returns false; we surface a short outcome to the web client.
            var ok = await m_Teleport.TryTeleportAsync(user, destination, kind, cooldownKey, cooldownOverride);
            return ok
                ? PlayerActionResult.Ok("已传送。")
                : PlayerActionResult.Fail("传送未完成，请查看游戏内提示。", refresh: true);
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;
    }
}
