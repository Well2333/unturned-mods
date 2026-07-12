using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Commands;
using OpenMod.API.Ioc;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Extensions.Economy.Abstractions;
using well404.Economy.Currency;

namespace well404.Economy
{
    /// <summary>
    /// Global <see cref="IEconomyProvider"/> implementation. Registered with
    /// <see cref="ServiceImplementationAttribute"/> so <b>any</b> plugin (daily
    /// check-in, well404.Shop, ...) can inject it.
    /// <para>
    /// A global service is activated in the global scope, where plugin-scoped
    /// services (the plugin's <see cref="IConfiguration"/>, working directory) are
    /// NOT injectable. So it reaches them lazily through
    /// <see cref="IPluginAccessor{TPlugin}"/> — the documented way for a global
    /// service to access its plugin. Config is read on each call so live reloads
    /// take effect.
    /// </para>
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class EconomyProvider : IEconomyProvider
    {
        private readonly IPluginAccessor<EconomyPlugin> m_PluginAccessor;
        private readonly IUserManager m_UserManager;

        private readonly object m_Lock = new object();
        private SqliteCurrencyBackend? m_DbBackend;
        private ExperienceCurrencyBackend? m_XpBackend;

        public EconomyProvider(
            IPluginAccessor<EconomyPlugin> pluginAccessor,
            IUserManager userManager)
        {
            m_PluginAccessor = pluginAccessor;
            m_UserManager = userManager;
        }

        private EconomyPlugin Plugin
        {
            get
            {
                var plugin = m_PluginAccessor.Instance;
                if (plugin == null || !plugin.IsComponentAlive)
                {
                    throw new UserFriendlyException("The economy plugin is not loaded.");
                }

                return plugin;
            }
        }

        private EconomySettings ReadSettings()
            => Plugin.LifetimeScope.Resolve<IConfiguration>().Get<EconomySettings>() ?? new EconomySettings();

        private ICurrencyBackend GetBackend(EconomySettings settings)
        {
            if (string.Equals(settings.Backend, "experience", StringComparison.OrdinalIgnoreCase))
            {
                lock (m_Lock)
                {
                    return m_XpBackend ??= new ExperienceCurrencyBackend(m_UserManager);
                }
            }

            // Do not reuse the former configurable LiteDB path: an existing economy.db
            // is not a SQLite database and this major version performs no migration.
            var path = Path.Combine(Plugin.WorkingDirectory, "economy.sqlite3");

            lock (m_Lock)
            {
                if (m_DbBackend == null || !string.Equals(m_DbBackend.FilePath, path, StringComparison.Ordinal))
                {
                    m_DbBackend = new SqliteCurrencyBackend(path, settings.Currency.StartingBalance);
                }
                else
                {
                    m_DbBackend.StartingBalance = settings.Currency.StartingBalance;
                }

                return m_DbBackend;
            }
        }

        public string CurrencyName => ReadSettings().Currency.Name;

        public string CurrencySymbol => ReadSettings().Currency.Symbol;

        public Task<decimal> GetBalanceAsync(string ownerId, string ownerType)
            => GetBackend(ReadSettings()).GetBalanceAsync(ownerId, ownerType);

        /// <summary>
        /// Lists accounts from the active backend. Not part of <see cref="IEconomyProvider"/>;
        /// used by the web panel module (which casts this concrete provider).
        /// </summary>
        public Task<System.Collections.Generic.IReadOnlyList<Currency.AccountSnapshot>> ListAccountsAsync()
            => GetBackend(ReadSettings()).ListAccountsAsync();

        public Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType, decimal changeAmount, string? reason)
            => GetBackend(ReadSettings()).UpdateBalanceAsync(ownerId, ownerType, changeAmount, reason);

        public Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)
            => GetBackend(ReadSettings()).SetBalanceAsync(ownerId, ownerType, balance);

        /// <summary>Deletes an account from the active backend. Not part of <see cref="IEconomyProvider"/>.</summary>
        public Task DeleteAccountAsync(string ownerId, string ownerType)
            => GetBackend(ReadSettings()).DeleteAccountAsync(ownerId, ownerType);
    }
}
