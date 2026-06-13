using System;
using System.Collections.Generic;
using OpenMod.API.Ioc;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>
    /// One player-facing command shown on the server-intro page. The description and group heading
    /// are i18n keys resolved via <see cref="IWebTranslationRegistry"/>; the intro page filters by
    /// <see cref="Permission"/> so each player sees only the commands they may actually use.
    /// </summary>
    public sealed class PlayerCommandInfo
    {
        public PlayerCommandInfo(string command, string descriptionKey, string? permission = null, string? groupKey = null)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            DescriptionKey = descriptionKey ?? string.Empty;
            Permission = permission;
            GroupKey = groupKey;
        }

        /// <summary>The literal command as the player types it, e.g. <c>/home</c>.</summary>
        public string Command { get; }

        /// <summary>i18n key for the one-line description (resolved per the player's language).</summary>
        public string DescriptionKey { get; }

        /// <summary>OpenMod permission node required to run it; null = always listed.</summary>
        public string? Permission { get; }

        /// <summary>i18n key for the section heading (e.g. the plugin name); null = ungrouped.</summary>
        public string? GroupKey { get; }
    }

    /// <summary>
    /// Cross-plugin registry of player commands surfaced on the intro page. Feature plugins register
    /// their command help on load; <c>well404.WebPanel</c> implements it as a global singleton.
    /// </summary>
    [Service]
    public interface IPlayerCommandRegistry
    {
        /// <summary>Registers (replacing) the command list contributed by <paramref name="sourceId"/>.</summary>
        void Register(string sourceId, IReadOnlyList<PlayerCommandInfo> commands);

        void Unregister(string sourceId);

        /// <summary>All registered commands, in registration order.</summary>
        IReadOnlyList<PlayerCommandInfo> GetAll();
    }
}
