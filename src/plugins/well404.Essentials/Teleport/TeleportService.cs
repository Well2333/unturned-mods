using System;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Permissions;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using UnityEngine;
using well404.Essentials.Data;
using well404.Essentials.Util;

namespace well404.Essentials.Teleport
{
    public enum TeleportKind
    {
        Home,
        Tp,
        Warp,
        Back
    }

    /// <summary>
    /// The shared teleport pipeline used by /home, /tp, /warp and /back: shared configured cooldown with one bucket per command type,
    /// optional economy fee, a stand-still warmup that cancels if the player moves, and finally
    /// the teleport itself. All player-facing intermediate messages (warmup hint, cooldown,
    /// insufficient funds, cancellation, fee charged) are printed here; callers only print their
    /// own success line after a <c>true</c> result. Registered as a plugin-scoped singleton.
    /// </summary>
    public sealed class TeleportService
    {
        /// <summary>Permission that exempts a player from teleport cooldowns.</summary>
        public const string CooldownExemptPermission = "well404.essentials.cooldown.exempt";

        private const int PollIntervalMs = 200;

        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILifetimeScope m_LifetimeScope;
        private readonly IPermissionChecker m_PermissionChecker;
        private readonly CooldownManager m_Cooldowns;
        private readonly ILogger<TeleportService> m_Logger;

        public TeleportService(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILifetimeScope lifetimeScope,
            IPermissionChecker permissionChecker,
            CooldownManager cooldowns,
            ILogger<TeleportService> logger)
        {
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
            m_LifetimeScope = lifetimeScope;
            m_PermissionChecker = permissionChecker;
            m_Cooldowns = cooldowns;
            m_Logger = logger;
        }

        private TeleportSettings Settings =>
            (m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings()).Teleport;

        private static decimal FeeFor(TeleportSettings settings, TeleportKind kind)
        {
            switch (kind)
            {
                case TeleportKind.Home: return settings.Costs.Home;
                case TeleportKind.Tp: return settings.Costs.Tp;
                case TeleportKind.Warp: return settings.Costs.Warp;
                case TeleportKind.Back: return settings.Costs.Back;
                default: return 0m;
            }
        }

        /// <summary>
        /// Runs the full pipeline. Returns true only if the player was teleported (and any fee
        /// charged). On any guard failure it prints the reason to the player and returns false.
        /// </summary>
        public async Task<bool> TryTeleportAsync(
            UnturnedUser user,
            PlayerLocation destination,
            TeleportKind kind,
            string cooldownKey)
        {
            var settings = Settings;

            // 1) Cooldown (unless exempt).
            var cooldownSeconds = settings.CooldownSeconds;
            if (cooldownSeconds > 0
                && await m_PermissionChecker.CheckPermissionAsync(user, CooldownExemptPermission) != PermissionGrantResult.Grant)
            {
                var remaining = m_Cooldowns.GetRemainingSeconds(user.Id, cooldownKey, cooldownSeconds);
                if (remaining > 0)
                {
                    await user.PrintMessageAsync(m_StringLocalizer["teleport:cooldown", new { seconds = TimeFormat.Humanize(remaining) }]);
                    return false;
                }
            }

            // 2) Economy fee (optional: only if a provider is present and the fee is positive).
            var fee = FeeFor(settings, kind);
            IEconomyProvider? economy = null;
            if (fee > 0m)
            {
                economy = m_LifetimeScope.ResolveOptional<IEconomyProvider>();
                if (economy != null)
                {
                    var balance = await economy.GetBalanceAsync(user.Id, user.Type);
                    if (balance < fee)
                    {
                        await user.PrintMessageAsync(m_StringLocalizer["teleport:not_enough_money",
                            new { symbol = economy.CurrencySymbol, amount = fee }]);
                        return false;
                    }
                }
            }

            // 3) Warmup: the player must hold still.
            if (!await WarmupAsync(user, settings))
            {
                return false;
            }

            // 4) Charge the fee atomically *before* teleporting. This re-checks affordability
            //    (the balance may have changed during the warmup) — UpdateBalanceAsync throws
            //    NotEnoughBalanceException if it would go negative.
            var charged = false;
            if (economy != null && fee > 0m)
            {
                try
                {
                    await economy.UpdateBalanceAsync(user.Id, user.Type, -fee, "essentials_" + kind.ToString().ToLowerInvariant());
                    charged = true;
                }
                catch (NotEnoughBalanceException)
                {
                    await user.PrintMessageAsync(m_StringLocalizer["teleport:not_enough_money",
                        new { symbol = economy.CurrencySymbol, amount = fee }]);
                    return false;
                }
            }

            // 5) Teleport. If it fails, refund the fee so the player is not charged for nothing.
            if (!await DoTeleportAsync(user, destination))
            {
                if (charged)
                {
                    await RefundAsync(economy!, user, fee, kind);
                }

                return false;
            }

            if (charged)
            {
                await user.PrintMessageAsync(m_StringLocalizer["teleport:fee_charged",
                    new { symbol = economy!.CurrencySymbol, amount = fee }]);
            }

            m_Cooldowns.Mark(user.Id, cooldownKey);
            return true;
        }

        private async Task RefundAsync(IEconomyProvider economy, UnturnedUser user, decimal fee, TeleportKind kind)
        {
            try
            {
                await economy.UpdateBalanceAsync(user.Id, user.Type, fee,
                    "essentials_refund_" + kind.ToString().ToLowerInvariant());
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Failed to refund teleport fee for {SteamId}.", user.Id);
            }
        }

        /// <summary>Waits out the warmup, cancelling if the player moves too far. Returns false if cancelled.</summary>
        private async Task<bool> WarmupAsync(UnturnedUser user, TeleportSettings settings)
        {
            if (settings.WarmupSeconds <= 0)
            {
                return true;
            }

            await UniTask.SwitchToMainThread();
            var player = user.Player.Player;
            if (player == null || player.transform == null)
            {
                return false;
            }

            var origin = player.transform.position;
            await user.PrintMessageAsync(m_StringLocalizer["teleport:warmup", new { seconds = settings.WarmupSeconds }]);

            var totalMs = settings.WarmupSeconds * 1000;
            var threshold = (float)settings.MoveThreshold;
            for (var elapsed = 0; elapsed < totalMs; elapsed += PollIntervalMs)
            {
                await UniTask.Delay(PollIntervalMs);
                await UniTask.SwitchToMainThread();

                player = user.Player.Player;
                if (player == null || player.transform == null)
                {
                    return false; // disconnected mid-warmup.
                }

                if (settings.CancelOnMove && Vector3.Distance(player.transform.position, origin) > threshold)
                {
                    await user.PrintMessageAsync(m_StringLocalizer["teleport:cancelled_move"]);
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> DoTeleportAsync(UnturnedUser user, PlayerLocation destination)
        {
            await UniTask.SwitchToMainThread();
            var player = user.Player.Player;
            if (player == null)
            {
                return false;
            }

            return player.teleportToLocation(
                new Vector3(destination.X, destination.Y, destination.Z), destination.Yaw);
        }
    }
}
