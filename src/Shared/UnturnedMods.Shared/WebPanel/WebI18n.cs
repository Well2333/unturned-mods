using System;
using System.Collections.Generic;
using OpenMod.API.Ioc;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>
    /// A cross-plugin store of web-UI translations. Both panels (admin modules and player menus)
    /// localize their text by <b>key</b>: feature plugins register their <c>(language → key → text)</c>
    /// tables here on load, the server resolves keys for the language the client requested, and the
    /// frontend offers a language switcher. English (<see cref="DefaultLanguage"/>) is the fallback.
    /// <para>
    /// Reserving the i18n interface: adding a language is just registering another bundle; an
    /// unresolved key falls back to English and then to the key itself, so partial translations are
    /// safe. Implemented by <c>well404.WebPanel</c> as a global singleton.
    /// </para>
    /// </summary>
    [Service]
    public interface IWebTranslationRegistry
    {
        /// <summary>The fallback language code used when a key is missing in the requested one.</summary>
        string DefaultLanguage { get; }

        /// <summary>Languages that have at least one registered bundle (for the UI switcher).</summary>
        IReadOnlyList<string> Languages { get; }

        /// <summary>Registers one <c>key → text</c> table for a language (merged with any existing).</summary>
        void AddBundle(string language, IReadOnlyDictionary<string, string> entries);

        /// <summary>
        /// Resolves <paramref name="key"/> for <paramref name="language"/>, falling back to the
        /// default language and finally to the key itself, so callers always get a non-null string.
        /// </summary>
        string Resolve(string key, string language);
    }

    /// <summary>Convenience helpers over <see cref="IWebTranslationRegistry"/>.</summary>
    public static class WebText
    {
        /// <summary>Resolves a key and <see cref="string.Format(string,object[])"/>s the arguments into it.</summary>
        public static string Format(
            this IWebTranslationRegistry registry, string language, string key, params object[] args)
        {
            var template = registry.Resolve(key, language);
            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }
    }
}
