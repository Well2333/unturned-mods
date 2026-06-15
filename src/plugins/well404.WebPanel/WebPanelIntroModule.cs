using System.Collections.Generic;
using System.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// The admin module that edits the shared server-intro Markdown shown on the player home page.
    /// This is panel <b>content</b> (not the security-sensitive <c>web.*</c> infrastructure config),
    /// so it is intentionally editable in the WebUI. Labels are English keys localized per request.
    /// </summary>
    internal static class WebPanelIntroModule
    {
        public const string ModuleId = "well404.webpanel";

        public static WebPanelModule Create(IntroStore store, IWebTranslationRegistry tr)
        {
            var intro = new WebPanelAction(
                id: "intro",
                label: "Introduction (Markdown)",
                kind: WebActionKind.Settings,
                handler: request =>
                {
                    store.Write(request.Get("intro") ?? string.Empty);
                    return Task.FromResult(WebActionResult.Ok(tr.Resolve("Saved.", request.Language)));
                },
                fields: new[]
                {
                    new WebField("intro", "Markdown", WebFieldType.TextArea,
                        placeholder: "Shown on the player panel home page. Markdown supported.")
                },
                description: "The welcome text players see on the panel home page (one shared text for all languages).",
                loader: () => Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string> { ["intro"] = store.Read() }));

            return new WebPanelModule(ModuleId, "Server intro", new[] { intro }, icon: "📖");
        }
    }
}
