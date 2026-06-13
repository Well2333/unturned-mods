using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// A dependency-free HTTP host (BCL <see cref="HttpListener"/>) that serves the
    /// generic single-page panel and a small JSON API over the
    /// <see cref="IWebPanelRegistry"/>. The admin surface lives entirely under a secret
    /// <b>token path</b> (<c>/&lt;token&gt;/…</c>) — the unguessable first path segment IS the
    /// auth, so a wrong or missing token is an ordinary 404 (no unauthorized oracle):
    /// <list type="bullet">
    /// <item><c>GET /&lt;token&gt;/</c> — the SPA (its API calls are relative, so they stay in-path).</item>
    /// <item><c>GET /&lt;token&gt;/api/modules</c> — module + action metadata to render from.</item>
    /// <item><c>POST /&lt;token&gt;/api/modules/{module}/{action}</c> — invoke an action.</item>
    /// </list>
    /// The token is mandatory (the plugin generates one when config leaves it empty); anything
    /// outside the token path — including bare <c>/</c> — reveals nothing.
    /// <para>
    /// It also serves a separate, per-player surface (no admin token; authenticated by a
    /// short-lived session token a player receives in-game):
    /// <list type="bullet">
    /// <item><c>GET /p</c> — the player SPA (reads the <c>?t=</c> session token).</item>
    /// <item><c>GET /api/p/view</c> — the player's menus, each pre-rendered.</item>
    /// <item><c>POST /api/p/invoke/{menu}</c> — execute a card button as that player.</item>
    /// </list>
    /// Player <c>/api/p</c> calls carry the session token in <c>?t=</c> (or the
    /// <c>X-Player-Token</c> header); it is validated against <see cref="PlayerWebSessionManager"/>.
    /// </para>
    /// </summary>
    public sealed class WebPanelHttpServer : IDisposable
    {
        private readonly IWebPanelRegistry m_Registry;
        private readonly IPlayerMenuRegistry m_PlayerRegistry;
        private readonly IWebTranslationRegistry m_Translations;
        private readonly PlayerWebSessionManager m_Sessions;
        private readonly ILogger m_Logger;
        private readonly string m_Token;
        private readonly string m_Html;
        private readonly string m_PlayerHtml;
        private readonly HttpListener m_Listener;
        private CancellationTokenSource? m_Cts;

        public WebPanelHttpServer(
            IWebPanelRegistry registry,
            IPlayerMenuRegistry playerRegistry,
            IWebTranslationRegistry translations,
            PlayerWebSessionManager sessions,
            ILogger logger,
            string prefix,
            string token,
            string html,
            string playerHtml)
        {
            m_Registry = registry;
            m_PlayerRegistry = playerRegistry;
            m_Translations = translations;
            m_Sessions = sessions;
            m_Logger = logger;
            m_Token = token;
            m_Html = html;
            m_PlayerHtml = playerHtml;
            m_Listener = new HttpListener();
            m_Listener.Prefixes.Add(prefix);
        }

        /// <summary>The language requested via <c>?lang=</c>, or the default when absent.</summary>
        private string LangOf(HttpListenerRequest request)
        {
            var lang = request.QueryString["lang"];
            return string.IsNullOrWhiteSpace(lang) ? m_Translations.DefaultLanguage : lang!;
        }

        private string Tr(string? key, string lang) => m_Translations.Resolve(key ?? string.Empty, lang);

        public void Start()
        {
            m_Listener.Start();
            m_Cts = new CancellationTokenSource();
            // Fire-and-forget accept loop; it ends when the listener is stopped.
            _ = AcceptLoopAsync(m_Cts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await m_Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var path = request.Url?.AbsolutePath ?? "/";

                // CORS preflight (only fires for non-simple cross-origin requests via a proxy).
                if (request.HttpMethod == "OPTIONS")
                {
                    WriteText(context.Response, 204, "text/plain; charset=utf-8", string.Empty);
                    return;
                }

                // ----- player surface (separate, per-player session auth) -----
                if (request.HttpMethod == "GET" && (path == "/p" || path == "/p/"))
                {
                    WriteText(context.Response, 200, "text/html; charset=utf-8", m_PlayerHtml);
                    return;
                }

                if (path.StartsWith("/api/p/", StringComparison.Ordinal) || path == "/api/p")
                {
                    await HandlePlayerApiAsync(context, path).ConfigureAwait(false);
                    return;
                }

                // ----- admin surface (token-in-path: /<token>/…) --------------
                // The secret token is the first path segment; a wrong/absent one is a plain 404,
                // indistinguishable from a non-existent route (no "unauthorized" oracle).
                var prefix = "/" + m_Token;
                if (path == prefix)
                {
                    // Redirect to the trailing-slash form so the SPA's relative API calls resolve
                    // under /<token>/ rather than the server root.
                    context.Response.Redirect(prefix + "/");
                    context.Response.StatusCode = 302;
                    context.Response.Close();
                    return;
                }

                if (!path.StartsWith(prefix + "/", StringComparison.Ordinal))
                {
                    // Anything outside the token path (including bare "/") reveals nothing.
                    var body = path == "/"
                        ? "Web Panel is running. Access requires the admin token URL."
                        : "Not found";
                    WriteText(context.Response, path == "/" ? 200 : 404, "text/plain; charset=utf-8", body);
                    return;
                }

                // Strip the token prefix; route the remainder exactly like the old admin API.
                var adminPath = path.Substring(prefix.Length);

                if (request.HttpMethod == "GET" && (adminPath == "/" || adminPath == "/index.html"))
                {
                    WriteText(context.Response, 200, "text/html; charset=utf-8", m_Html);
                    return;
                }

                if (!adminPath.StartsWith("/api/", StringComparison.Ordinal) && adminPath != "/api")
                {
                    WriteText(context.Response, 404, "text/plain; charset=utf-8", "Not found");
                    return;
                }

                if (request.HttpMethod == "GET" && adminPath == "/api/modules")
                {
                    WriteJson(context.Response, 200, BuildModulesJson(LangOf(request)));
                    return;
                }

                var segments = adminPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 5 && segments[1] == "modules")
                {
                    // ["api", "modules", "{module}", "{action}", "values" | "records" | "delete"]
                    if (request.HttpMethod == "GET" && segments[4] == "values")
                    {
                        await LoadValuesAsync(context, segments[2], segments[3]).ConfigureAwait(false);
                        return;
                    }

                    if (request.HttpMethod == "GET" && segments[4] == "records")
                    {
                        await LoadRecordsAsync(context, segments[2], segments[3]).ConfigureAwait(false);
                        return;
                    }

                    if (request.HttpMethod == "POST" && segments[4] == "delete")
                    {
                        await DeleteAsync(context, segments[2], segments[3]).ConfigureAwait(false);
                        return;
                    }
                }

                // ["api", "modules", "{module}", "{action}"]
                if (request.HttpMethod == "POST" && segments.Length == 4 && segments[1] == "modules")
                {
                    await InvokeActionAsync(context, segments[2], segments[3]).ConfigureAwait(false);
                    return;
                }

                WriteJson(context.Response, 404, "{\"success\":false,\"message\":\"Unknown endpoint\"}");
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: error handling request.");
                try
                {
                    WriteJson(context.Response, 500, "{\"success\":false,\"message\":\"Internal error\"}");
                }
                catch
                {
                    // The response may already be closed; nothing more to do.
                }
            }
        }

        private async Task InvokeActionAsync(HttpListenerContext context, string moduleId, string actionId)
        {
            var module = m_Registry.GetModules()
                .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            var action = module?.Actions
                .FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));

            if (action == null)
            {
                WriteJson(context.Response, 404, "{\"success\":false,\"message\":\"Unknown action\"}");
                return;
            }

            var values = ParseForm(ReadBody(context.Request));

            WebActionResult result;
            try
            {
                result = await action.Handler(new WebActionRequest(values, LangOf(context.Request))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: action {Module}/{Action} threw.", moduleId, actionId);
                result = WebActionResult.Fail(ex.Message);
            }

            WriteJson(context.Response, 200, BuildResultJson(result));
        }

        private async Task LoadValuesAsync(HttpListenerContext context, string moduleId, string actionId)
        {
            var module = m_Registry.GetModules()
                .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            var action = module?.Actions
                .FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));

            if (action?.Loader == null)
            {
                WriteJson(context.Response, 200, "{\"values\":{}}");
                return;
            }

            try
            {
                var values = await action.Loader().ConfigureAwait(false);
                WriteJson(context.Response, 200, BuildValuesJson(values));
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: loader {Module}/{Action} threw.", moduleId, actionId);
                WriteJson(context.Response, 500, "{\"values\":{}}");
            }
        }

        private static string BuildValuesJson(IReadOnlyDictionary<string, string> values)
        {
            var sb = new StringBuilder();
            sb.Append("{\"values\":{");
            var first = true;
            foreach (var pair in values)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append(Json.Encode(pair.Key)).Append(':').Append(Json.Encode(pair.Value));
            }

            sb.Append("}}");
            return sb.ToString();
        }

        private async Task LoadRecordsAsync(HttpListenerContext context, string moduleId, string actionId)
        {
            var action = FindAction(moduleId, actionId);
            if (action?.RecordsLoader == null)
            {
                WriteJson(context.Response, 200, "{\"records\":[]}");
                return;
            }

            try
            {
                var records = await action.RecordsLoader().ConfigureAwait(false);
                WriteJson(context.Response, 200, BuildRecordsJson(records));
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: records loader {Module}/{Action} threw.", moduleId, actionId);
                WriteJson(context.Response, 500, "{\"records\":[]}");
            }
        }

        private async Task DeleteAsync(HttpListenerContext context, string moduleId, string actionId)
        {
            var action = FindAction(moduleId, actionId);
            if (action?.DeleteHandler == null)
            {
                WriteJson(context.Response, 404, "{\"success\":false,\"message\":\"不支持删除\"}");
                return;
            }

            var values = ParseForm(ReadBody(context.Request));
            WebActionResult result;
            try
            {
                result = await action.DeleteHandler(new WebActionRequest(values, LangOf(context.Request))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: delete {Module}/{Action} threw.", moduleId, actionId);
                result = WebActionResult.Fail(ex.Message);
            }

            WriteJson(context.Response, 200, BuildResultJson(result));
        }

        private WebPanelAction? FindAction(string moduleId, string actionId)
        {
            var module = m_Registry.GetModules()
                .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            return module?.Actions
                .FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildRecordsJson(IReadOnlyList<WebRecord> records)
        {
            var sb = new StringBuilder();
            sb.Append("{\"records\":[");
            for (var i = 0; i < records.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var record = records[i];
                sb.Append('{')
                    .Append("\"key\":").Append(Json.Encode(record.Key)).Append(',')
                    .Append("\"label\":").Append(Json.Encode(record.Label)).Append(',')
                    .Append("\"tags\":");
                AppendStringArray(sb, record.Tags);
                sb.Append(',')
                    .Append("\"values\":{");
                var first = true;
                foreach (var pair in record.Values)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append(Json.Encode(pair.Key)).Append(':').Append(Json.Encode(pair.Value));
                }

                sb.Append("}}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        // ----- player surface -------------------------------------------------

        /// <summary>
        /// Routes the player-facing JSON API. Authenticates every call against the
        /// per-player session token (<c>?t=</c> or <c>X-Player-Token</c>); the admin token is
        /// never accepted here, and the session token is never accepted on the admin API.
        /// </summary>
        private async Task HandlePlayerApiAsync(HttpListenerContext context, string path)
        {
            var session = m_Sessions.Validate(GetPlayerToken(context.Request));
            if (session == null)
            {
                WriteJson(context.Response, 401,
                    "{\"ok\":false,\"message\":\"Session expired — reopen the panel in-game.\"}");
                return;
            }

            var ctx = new PlayerMenuContext(session.SteamId, session.DisplayName, LangOf(context.Request));
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // ["api", "p", "view"]
            if (context.Request.HttpMethod == "GET" && segments.Length == 3 && segments[2] == "view")
            {
                await PlayerViewAsync(context, ctx).ConfigureAwait(false);
                return;
            }

            // ["api", "p", "invoke", "{menuId}"]
            if (context.Request.HttpMethod == "POST" && segments.Length == 4 && segments[2] == "invoke")
            {
                await PlayerInvokeAsync(context, ctx, segments[3]).ConfigureAwait(false);
                return;
            }

            WriteJson(context.Response, 404, "{\"ok\":false,\"message\":\"Unknown endpoint\"}");
        }

        private async Task PlayerViewAsync(HttpListenerContext context, PlayerMenuContext ctx)
        {
            var menus = m_PlayerRegistry.GetMenus();
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"player\":")
                .Append("{\"name\":").Append(Json.Encode(ctx.DisplayName)).Append('}')
                .Append(",\"lang\":").Append(Json.Encode(ctx.Language))
                .Append(',');
            AppendLanguagesJson(sb);
            sb.Append(",\"menus\":[");

            for (var i = 0; i < menus.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var menu = menus[i];
                var view = await RenderSafeAsync(menu, ctx).ConfigureAwait(false);
                sb.Append('{')
                    .Append("\"id\":").Append(Json.Encode(menu.Id)).Append(',')
                    .Append("\"title\":").Append(Json.Encode(Tr(menu.Title, ctx.Language))).Append(',')
                    .Append("\"icon\":").Append(Json.Encode(menu.Icon)).Append(',')
                    .Append("\"view\":");
                AppendPlayerView(sb, view);
                sb.Append('}');
            }

            sb.Append("]}");
            WriteJson(context.Response, 200, sb.ToString());
        }

        private async Task PlayerInvokeAsync(HttpListenerContext context, PlayerMenuContext ctx, string menuId)
        {
            var menu = m_PlayerRegistry.GetMenu(menuId);
            if (menu == null)
            {
                WriteJson(context.Response, 404, "{\"success\":false,\"message\":\"Unknown menu\"}");
                return;
            }

            var values = ParseForm(ReadBody(context.Request));
            values.TryGetValue("action", out var actionId);
            values.TryGetValue("cardKey", out var cardKey);
            values.TryGetValue("value", out var value);

            PlayerActionResult result;
            try
            {
                result = await menu.InvokeAsync(ctx, actionId ?? string.Empty, cardKey ?? string.Empty,
                    string.IsNullOrEmpty(value) ? null : value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: player menu {Menu} action threw.", menuId);
                result = PlayerActionResult.Fail(ex.Message);
            }

            var sb = new StringBuilder();
            sb.Append('{')
                .Append("\"success\":").Append(Json.Bool(result.Success)).Append(',')
                .Append("\"message\":").Append(Json.Encode(result.Message)).Append(',')
                .Append("\"refresh\":").Append(Json.Bool(result.Refresh)).Append(',')
                .Append("\"view\":");

            if (result.Refresh)
            {
                AppendPlayerView(sb, await RenderSafeAsync(menu, ctx).ConfigureAwait(false));
            }
            else
            {
                sb.Append("null");
            }

            sb.Append('}');
            WriteJson(context.Response, 200, sb.ToString());
        }

        private async Task<PlayerMenuView> RenderSafeAsync(IPlayerMenu menu, PlayerMenuContext ctx)
        {
            try
            {
                return await menu.RenderAsync(ctx).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: player menu {Menu} render threw.", menu.Id);
                return new PlayerMenuView(menu.Title, null, Array.Empty<PlayerCard>(),
                    "Failed to load: " + ex.Message);
            }
        }

        private static void AppendPlayerView(StringBuilder sb, PlayerMenuView view)
        {
            sb.Append('{')
                .Append("\"title\":").Append(Json.Encode(view.Title)).Append(',')
                .Append("\"header\":").Append(Json.Encode(view.Header)).Append(',')
                .Append("\"message\":").Append(Json.Encode(view.Message)).Append(',')
                .Append("\"bodyMarkdown\":").Append(Json.Encode(view.BodyMarkdown)).Append(',')
                .Append("\"cards\":[");

            for (var i = 0; i < view.Cards.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var card = view.Cards[i];
                sb.Append('{')
                    .Append("\"key\":").Append(Json.Encode(card.Key)).Append(',')
                    .Append("\"label\":").Append(Json.Encode(card.Label)).Append(',')
                    .Append("\"lines\":");
                AppendStringArray(sb, card.Lines);
                sb.Append(",\"tags\":");
                AppendStringArray(sb, card.Tags);
                sb.Append(",\"buttons\":[");

                for (var j = 0; j < card.Buttons.Count; j++)
                {
                    if (j > 0)
                    {
                        sb.Append(',');
                    }

                    var button = card.Buttons[j];
                    sb.Append('{')
                        .Append("\"actionId\":").Append(Json.Encode(button.ActionId)).Append(',')
                        .Append("\"label\":").Append(Json.Encode(button.Label)).Append(',')
                        .Append("\"style\":").Append(Json.Encode(button.Style)).Append(',')
                        .Append("\"promptLabel\":").Append(Json.Encode(button.PromptLabel))
                        .Append('}');
                }

                sb.Append("]}");
            }

            sb.Append("]}");
        }

        private static string? GetPlayerToken(HttpListenerRequest request)
        {
            var header = request.Headers["X-Player-Token"];
            return !string.IsNullOrEmpty(header) ? header : request.QueryString["t"];
        }

        // ----- JSON building -------------------------------------------------

        /// <summary>Appends <c>"lang":"..","languages":[..]</c> for the client's language switcher.</summary>
        private void AppendLanguagesJson(StringBuilder sb)
        {
            sb.Append("\"defaultLanguage\":").Append(Json.Encode(m_Translations.DefaultLanguage))
                .Append(",\"languages\":");
            AppendStringArray(sb, m_Translations.Languages);
        }

        private string BuildModulesJson(string lang)
        {
            var sb = new StringBuilder();
            sb.Append("{\"lang\":").Append(Json.Encode(lang)).Append(',');
            AppendLanguagesJson(sb);
            sb.Append(",\"modules\":[");
            var modules = m_Registry.GetModules();
            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('{')
                    .Append("\"id\":").Append(Json.Encode(module.Id)).Append(',')
                    .Append("\"title\":").Append(Json.Encode(Tr(module.Title, lang))).Append(',')
                    .Append("\"icon\":").Append(Json.Encode(module.Icon)).Append(',')
                    .Append("\"actions\":[");

                for (var j = 0; j < module.Actions.Count; j++)
                {
                    var action = module.Actions[j];
                    if (j > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append('{')
                        .Append("\"id\":").Append(Json.Encode(action.Id)).Append(',')
                        .Append("\"label\":").Append(Json.Encode(Tr(action.Label, lang))).Append(',')
                        .Append("\"kind\":").Append(Json.Encode(action.Kind.ToString().ToLowerInvariant())).Append(',')
                        .Append("\"hasLoader\":").Append(Json.Bool(action.Loader != null)).Append(',')
                        .Append("\"hasDelete\":").Append(Json.Bool(action.DeleteHandler != null)).Append(',')
                        .Append("\"keyField\":").Append(Json.Encode(action.KeyField)).Append(',')
                        .Append("\"layout\":").Append(Json.Encode(action.Layout)).Append(',')
                        .Append("\"description\":").Append(Json.Encode(Tr(action.Description, lang))).Append(',')
                        .Append("\"fields\":[");

                    for (var k = 0; k < action.Fields.Count; k++)
                    {
                        var field = action.Fields[k];
                        if (k > 0)
                        {
                            sb.Append(',');
                        }

                        sb.Append('{')
                            .Append("\"name\":").Append(Json.Encode(field.Name)).Append(',')
                            .Append("\"label\":").Append(Json.Encode(Tr(field.Label, lang))).Append(',')
                            .Append("\"type\":").Append(Json.Encode(field.Type.ToString().ToLowerInvariant())).Append(',')
                            .Append("\"required\":").Append(Json.Bool(field.Required)).Append(',')
                            .Append("\"placeholder\":").Append(Json.Encode(field.Placeholder == null ? null : Tr(field.Placeholder, lang))).Append(',')
                            .Append("\"options\":");
                        AppendLocalizedStringArray(sb, field.Options, lang);
                        sb.Append('}');
                    }

                    sb.Append("]}");
                }

                sb.Append("]}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string BuildResultJson(WebActionResult result)
        {
            var sb = new StringBuilder();
            sb.Append('{')
                .Append("\"success\":").Append(Json.Bool(result.Success)).Append(',')
                .Append("\"message\":").Append(Json.Encode(result.Message)).Append(',')
                .Append("\"columns\":");
            AppendStringArray(sb, result.Columns);
            sb.Append(",\"rows\":");

            if (result.Rows == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('[');
                for (var i = 0; i < result.Rows.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    AppendStringArray(sb, result.Rows[i]);
                }

                sb.Append(']');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStringArray(StringBuilder sb, IReadOnlyList<string>? items)
        {
            if (items == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('[');
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(Json.Encode(items[i]));
            }

            sb.Append(']');
        }

        /// <summary>Like <see cref="AppendStringArray"/> but resolves each item as an i18n key.</summary>
        private void AppendLocalizedStringArray(StringBuilder sb, IReadOnlyList<string>? items, string lang)
        {
            if (items == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('[');
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(Json.Encode(Tr(items[i], lang)));
            }

            sb.Append(']');
        }

        // ----- request / response helpers -----------------------------------

        private static string ReadBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                return string.Empty;
            }

            // Always decode as UTF-8: form bodies are percent-encoded ASCII (the SPA) or raw
            // UTF-8; honoring a possibly-absent/wrong Content-Type charset only causes mojibake.
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>Parses an <c>application/x-www-form-urlencoded</c> body into a value map.</summary>
        private static IReadOnlyDictionary<string, string> ParseForm(string body)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(body))
            {
                return result;
            }

            foreach (var pair in body.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var eq = pair.IndexOf('=');
                var rawKey = eq >= 0 ? pair.Substring(0, eq) : pair;
                var rawValue = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                var key = Uri.UnescapeDataString(rawKey.Replace('+', ' '));
                var value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
                if (key.Length > 0)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static void WriteText(HttpListenerResponse response, int status, string contentType, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            SetCorsHeaders(response);
            response.StatusCode = status;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Permissive CORS so the panel keeps working behind an arbitrary user-supplied reverse
        /// proxy that may serve the page from a different origin. Safe to allow <c>*</c> here: auth
        /// is a secret URL/token, never a cookie, so cross-origin JS gains nothing a direct request
        /// wouldn't already have.
        /// </summary>
        private static void SetCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Player-Token";
        }

        private static void WriteJson(HttpListenerResponse response, int status, string json)
            => WriteText(response, status, "application/json; charset=utf-8", json);

        public void Dispose()
        {
            try
            {
                m_Cts?.Cancel();
            }
            catch
            {
                // ignored
            }

            try
            {
                if (m_Listener.IsListening)
                {
                    m_Listener.Stop();
                }

                m_Listener.Close();
            }
            catch
            {
                // ignored
            }

            m_Cts?.Dispose();
        }
    }
}
