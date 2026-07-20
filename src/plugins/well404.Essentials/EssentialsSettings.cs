using System.Collections.Generic;

namespace well404.Essentials
{
    /// <summary>Strongly-typed view of the Essentials <c>config.yaml</c>.</summary>
    public class EssentialsSettings
    {
        public TeleportSettings Teleport { get; set; } = new TeleportSettings();

        public TpaSettings Tpa { get; set; } = new TpaSettings();

        public PartySettings Party { get; set; } = new PartySettings();

        public BackSettings Back { get; set; } = new BackSettings();

        public SleepSettings Sleep { get; set; } = new SleepSettings();

        public WarpMapSettings WarpMap { get; set; } = new WarpMapSettings();

        public WarpTagSettings WarpTags { get; set; } = new WarpTagSettings();

        public List<WarpEntry> Warps { get; set; } = new List<WarpEntry>();

        public List<GiftEntry> Gifts { get; set; } = new List<GiftEntry>();
    }

    /// <summary>Shared rules for every teleport (home / tp / warp / back).</summary>
    public class TeleportSettings
    {
        /// <summary>Seconds the player must stand still before the teleport fires. 0 = instant.</summary>
        public int WarmupSeconds { get; set; } = 5;

        /// <summary>If true, moving more than <see cref="MoveThreshold"/> during warmup cancels it.</summary>
        public bool CancelOnMove { get; set; } = true;

        /// <summary>How far (metres) the player may drift during warmup before it counts as moving.</summary>
        public decimal MoveThreshold { get; set; } = 0.5m;

        /// <summary>Shared cooldown duration in seconds (0 = no cooldown). Every warp destination uses the same warp cooldown bucket.</summary>
        public int CooldownSeconds { get; set; } = 0;

        /// <summary>
        /// Economy fee charged per teleport, keyed by command (home / tp / warp / back).
        /// All default to 0. Fees are only charged when an <c>IEconomyProvider</c> is present;
        /// without an economy plugin, teleports are always free.
        /// </summary>
        public TeleportCosts Costs { get; set; } = new TeleportCosts();
    }

    public class TeleportCosts
    {
        public decimal Home { get; set; } = 0m;
        public decimal Tp { get; set; } = 0m;
        public decimal Warp { get; set; } = 0m;
        public decimal Back { get; set; } = 0m;
    }

    public class TpaSettings
    {
        /// <summary>How long (seconds) a teleport request stays open before it expires.</summary>
        public int ExpirationSeconds { get; set; } = 30;
    }

    public class PartySettings
    {
        /// <summary>How long (seconds) a party invite stays open before it expires.</summary>
        public int InviteExpirationSeconds { get; set; } = 60;

        /// <summary>
        /// Maximum party size enforced by this plugin (0 = no extra cap). The plugin always
        /// bypasses Unturned's vanilla group-size limit; this is its own optional cap.
        /// </summary>
        public int MaxMembers { get; set; } = 0;
    }

    public class BackSettings
    {
        /// <summary>Seconds of damage immunity granted after teleporting back to a death point. 0 = none.</summary>
        public int InvincibilitySeconds { get; set; } = 5;
    }

    public class SleepSettings
    {
        /// <summary>Master switch for the /sleep vote.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Fraction of online players who must vote /sleep to flip day↔night.
        /// 0.5 = a simple majority (half or more). Clamped to (0, 1].
        /// </summary>
        public decimal RequiredRatio { get; set; } = 0.5m;
    }

    public class WarpMapSettings
    {
        /// <summary>Enables the optional interactive map view; the warp list always remains available.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// <c>native</c> follows Unturned's chart gameplay rule and map-item access;
        /// <c>always</c> exposes the chart to every player who can open this menu.
        /// </summary>
        public string Visibility { get; set; } = "native";
    }

    public class WarpTagSettings
    {
        /// <summary>True after the built-in presets have been materialized into config.yaml once.</summary>
        public bool Initialized { get; set; }

        public List<WarpTagDefinition> Presets { get; set; } = new List<WarpTagDefinition>();

        public List<WarpTagDefinition> Custom { get; set; } = new List<WarpTagDefinition>();
    }

    public class WarpTagDefinition
    {
        /// <summary>Stable lower-case ID stored on warp entries and used by filters.</summary>
        public string Id { get; set; } = string.Empty;

        public string NameEn { get; set; } = string.Empty;

        public string NameZh { get; set; } = string.Empty;

        public string Emoji { get; set; } = string.Empty;
    }

    public class WarpEntry
    {
        /// <summary>The name used in <c>/warp &lt;name&gt;</c>. Case-insensitive.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Map name this destination belongs to. Empty legacy entries stay unassigned.</summary>
        public string Map { get; set; } = string.Empty;

        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Z { get; set; }

        /// <summary>Facing yaw (degrees) applied on arrival.</summary>
        public decimal Yaw { get; set; }

        /// <summary>Read-only player-facing filter tags. Missing legacy values become default.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>Legacy single-label field used only to migrate existing configuration.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Stable administrator-controlled display order.</summary>
        public int Order { get; set; } = 0;
    }

    public class GiftEntry
    {
        /// <summary>The id used in <c>/gift &lt;id&gt;</c>. Case-insensitive.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Permission required to claim this gift (empty = everyone). Use this for VIP-only
        /// packs, e.g. <c>well404.essentials.gift.vip</c>.
        /// </summary>
        public string Permission { get; set; } = string.Empty;

        /// <summary>
        /// Standard 5-field crontab expression (minute hour day-of-month month day-of-week)
        /// defining when the gift refreshes. A player may claim once per cron period.
        /// Empty = claimable only once ever.
        /// </summary>
        public string Cron { get; set; } = string.Empty;

        /// <summary>Items granted on claim.</summary>
        public List<GiftItem> Items { get; set; } = new List<GiftItem>();
    }

    public class GiftItem
    {
        public ushort ItemId { get; set; }
        public int Amount { get; set; } = 1;
    }
}
