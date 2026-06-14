using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.API.Ioc;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>Identifies the player a menu is being rendered/invoked for.</summary>
    public sealed class PlayerMenuContext
    {
        public PlayerMenuContext(string steamId, string displayName, string language = "en")
        {
            SteamId = steamId ?? throw new ArgumentNullException(nameof(steamId));
            DisplayName = displayName ?? string.Empty;
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language;
        }

        /// <summary>The player's Steam ID (matches the economy owner id / OpenMod user id).</summary>
        public string SteamId { get; }

        public string DisplayName { get; }

        /// <summary>
        /// The UI language the player picked in the web client (e.g. <c>en</c>, <c>zh</c>). Menus
        /// should render their text in this language via <see cref="IWebTranslationRegistry"/>.
        /// </summary>
        public string Language { get; }
    }

    /// <summary>A clickable button on a <see cref="PlayerCard"/>.</summary>
    public sealed class PlayerButton
    {
        public PlayerButton(string actionId, string label, string? style = null, string? promptLabel = null, string? promptDefault = null)
        {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Style = style;
            PromptLabel = promptLabel;
            PromptDefault = promptDefault;
        }

        /// <summary>Action id passed back to <see cref="IPlayerMenu.InvokeAsync"/> (e.g. <c>buy</c>, <c>claim</c>).</summary>
        public string ActionId { get; }

        public string Label { get; }

        /// <summary>Optional visual hint: <c>primary</c>, <c>success</c>, <c>danger</c>, or null (default).</summary>
        public string? Style { get; }

        /// <summary>
        /// If set, the client prompts for a number (e.g. an amount) with this label before posting;
        /// the entered value arrives as the <c>value</c> argument of <see cref="IPlayerMenu.InvokeAsync"/>.
        /// </summary>
        public string? PromptLabel { get; }

        /// <summary>Pre-filled value for the prompt (e.g. the available count, so "OK" acts on all). Null = "1".</summary>
        public string? PromptDefault { get; }
    }

    /// <summary>One entry (item, gift, ...) shown as a card with text and buttons.</summary>
    public sealed class PlayerCard
    {
        public PlayerCard(
            string key,
            string label,
            IReadOnlyList<string>? lines = null,
            IReadOnlyList<string>? tags = null,
            IReadOnlyList<PlayerButton>? buttons = null,
            string? group = null,
            string? badge = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Lines = lines ?? Array.Empty<string>();
            Tags = tags ?? Array.Empty<string>();
            Buttons = buttons ?? Array.Empty<PlayerButton>();
            Group = group;
            Badge = badge;
        }

        /// <summary>Identifies the entry; passed back as the <c>cardKey</c> on a button action.</summary>
        public string Key { get; }

        public string Label { get; }

        /// <summary>Plain text lines (e.g. price, status).</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>Pill badges (e.g. bundle contents).</summary>
        public IReadOnlyList<string> Tags { get; }

        public IReadOnlyList<PlayerButton> Buttons { get; }

        /// <summary>
        /// Optional section heading this card belongs to. When cards carry groups, the client
        /// renders a heading per group (cards are grouped in their given order). Null = ungrouped.
        /// A generic UI hint — the renderer has no knowledge of what the groups mean.
        /// </summary>
        public string? Group { get; }

        /// <summary>
        /// Optional short leading badge (e.g. an id) shown before the label in compact layouts.
        /// Null = none. Generic; the renderer does not interpret it.
        /// </summary>
        public string? Badge { get; }
    }

    /// <summary>The rendered state of a player menu (one tab).</summary>
    public sealed class PlayerMenuView
    {
        public PlayerMenuView(
            string title,
            string? header,
            IReadOnlyList<PlayerCard> cards,
            string? message = null,
            string? bodyMarkdown = null,
            string? layout = null)
        {
            Title = title ?? string.Empty;
            Header = header;
            Cards = cards ?? Array.Empty<PlayerCard>();
            Message = message;
            BodyMarkdown = bodyMarkdown;
            Layout = layout;
        }

        public string Title { get; }

        /// <summary>A summary line shown at the top (e.g. the player's balance). Null = none.</summary>
        public string? Header { get; }

        public IReadOnlyList<PlayerCard> Cards { get; }

        /// <summary>
        /// Optional layout hint for the cards: <c>"list"</c> renders compact rows (id/label/buttons),
        /// anything else (default/null) renders full cards. A generic presentation choice the menu
        /// makes; the host renders it uniformly for every menu.
        /// </summary>
        public string? Layout { get; }

        /// <summary>An optional notice (e.g. "you must be online").</summary>
        public string? Message { get; }

        /// <summary>
        /// Optional Markdown body rendered above the cards (used by the server-intro page). The
        /// client renders it; keep it to a safe Markdown subset (headings, lists, links, emphasis).
        /// </summary>
        public string? BodyMarkdown { get; }
    }

    /// <summary>Outcome of a player button action.</summary>
    public sealed class PlayerActionResult
    {
        public bool Success { get; set; }

        public string? Message { get; set; }

        /// <summary>Whether the client should re-fetch the view after this action (default true).</summary>
        public bool Refresh { get; set; } = true;

        public static PlayerActionResult Ok(string? message = null, bool refresh = true)
            => new PlayerActionResult { Success = true, Message = message, Refresh = refresh };

        public static PlayerActionResult Fail(string message, bool refresh = false)
            => new PlayerActionResult { Success = false, Message = message, Refresh = refresh };
    }

    /// <summary>
    /// A player-facing menu a feature plugin exposes through the web panel's player surface
    /// (opened in the player's browser via an in-game link). Unlike <see cref="WebPanelModule"/>
    /// (admin/config CRUD), this renders per-player content and executes actions as that player.
    /// Handlers run on a web server thread — switch to the main thread before touching Unturned.
    /// </summary>
    public interface IPlayerMenu
    {
        /// <summary>Stable, unique menu id (e.g. <c>shop</c>); also the URL tab anchor.</summary>
        string Id { get; }

        /// <summary>Tab title shown to the player.</summary>
        string Title { get; }

        /// <summary>Optional icon hint (emoji/short label).</summary>
        string? Icon { get; }

        Task<PlayerMenuView> RenderAsync(PlayerMenuContext context);

        /// <summary>
        /// Executes a card button. <paramref name="value"/> is the optional number entered for a
        /// button with a <see cref="PlayerButton.PromptLabel"/> (null otherwise).
        /// </summary>
        Task<PlayerActionResult> InvokeAsync(PlayerMenuContext context, string actionId, string cardKey, string? value);
    }

    /// <summary>
    /// Registration surface for player-facing menus, implemented by <c>well404.WebPanel</c> as a
    /// global singleton (same cross-plugin pattern as <see cref="IWebPanelRegistry"/>). Feature
    /// plugins inject it optionally and register their menus.
    /// </summary>
    [Service]
    public interface IPlayerMenuRegistry
    {
        void RegisterMenu(IPlayerMenu menu);

        void UnregisterMenu(string menuId);

        IReadOnlyList<IPlayerMenu> GetMenus();

        IPlayerMenu? GetMenu(string menuId);
    }

    /// <summary>
    /// Mints short-lived, per-player web links into the panel's player surface. Implemented by
    /// <c>well404.WebPanel</c>; feature plugins inject it optionally and, in a command, send the
    /// returned URL to the player with <c>Player.sendBrowserRequest</c>.
    /// </summary>
    [Service]
    public interface IPlayerWebSessionService
    {
        /// <summary>
        /// Creates a session for the player and returns a full URL to open, or null if the panel
        /// has no reachable public base URL configured. <paramref name="menuId"/> focuses a tab.
        /// </summary>
        string? CreateLink(string steamId, string displayName, string? menuId = null);
    }
}
