using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// Global <see cref="IWebTranslationRegistry"/> implementation: a thread-safe
    /// <c>language → (key → text)</c> store. English is the fallback; an unknown key resolves to the
    /// default language and then to the key itself, so partial translations never throw.
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class WebTranslationRegistry : IWebTranslationRegistry
    {
        public const string Default = "en";

        private readonly object m_Lock = new object();
        private readonly Dictionary<string, Dictionary<string, string>> m_Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public string DefaultLanguage => Default;

        public IReadOnlyList<string> Languages
        {
            get
            {
                lock (m_Lock)
                {
                    // The default (English) is always offered even with no bundle, since source
                    // strings ARE the English text; other languages come from registered maps.
                    var set = new HashSet<string>(m_Tables.Keys, StringComparer.OrdinalIgnoreCase) { Default };
                    return set
                        .OrderBy(l => string.Equals(l, Default, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(l => l, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
        }

        public void AddBundle(string language, IReadOnlyDictionary<string, string> entries)
        {
            if (string.IsNullOrWhiteSpace(language) || entries == null)
            {
                return;
            }

            lock (m_Lock)
            {
                if (!m_Tables.TryGetValue(language, out var table))
                {
                    table = new Dictionary<string, string>(StringComparer.Ordinal);
                    m_Tables[language] = table;
                }

                foreach (var pair in entries)
                {
                    table[pair.Key] = pair.Value;
                }
            }
        }

        public string Resolve(string key, string language)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            lock (m_Lock)
            {
                if (!string.IsNullOrWhiteSpace(language)
                    && m_Tables.TryGetValue(language, out var table)
                    && table.TryGetValue(key, out var text))
                {
                    return text;
                }

                if (m_Tables.TryGetValue(Default, out var fallback) && fallback.TryGetValue(key, out var def))
                {
                    return def;
                }

                return key;
            }
        }
    }
}
