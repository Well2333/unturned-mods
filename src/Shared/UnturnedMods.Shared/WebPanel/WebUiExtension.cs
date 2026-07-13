using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>
    /// Trusted, plugin-owned web UI mounted by WebPanel inside an isolated Shadow DOM.
    /// The host supplies authentication, routing, theme tokens and a capability-scoped API;
    /// the feature plugin owns its markup, styles and behavior.
    /// </summary>
    public sealed class WebUiExtension
    {
        public WebUiExtension(
            string html,
            string css,
            string javaScript,
            bool replaceDefault = true)
        {
            Html = html ?? string.Empty;
            Css = css ?? string.Empty;
            JavaScript = javaScript ?? string.Empty;
            ReplaceDefault = replaceDefault;
        }

        public string Html { get; }

        public string Css { get; }

        public string JavaScript { get; }

        /// <summary>
        /// True replaces the descriptor renderer. False mounts the custom surface before the
        /// descriptor-generated controls, useful for summaries and dashboards.
        /// </summary>
        public bool ReplaceDefault { get; }

        public static WebUiExtension FromEmbeddedResources(
            Assembly assembly,
            string htmlSuffix,
            string cssSuffix,
            string javaScriptSuffix,
            bool replaceDefault = true)
            => new WebUiExtension(
                Load(assembly, htmlSuffix),
                Load(assembly, cssSuffix),
                Load(assembly, javaScriptSuffix),
                replaceDefault);

        private static string Load(Assembly assembly, string suffix)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (string.IsNullOrWhiteSpace(suffix)) throw new ArgumentException("Resource suffix is required.", nameof(suffix));

            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                throw new InvalidOperationException("Embedded web UI resource is missing: " + suffix);
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("Embedded web UI resource cannot be opened: " + suffix);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
