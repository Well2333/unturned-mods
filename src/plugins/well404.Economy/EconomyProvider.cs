using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API;
using OpenMod.API.Ioc;
using OpenMod.API.Users;
using OpenMod.Extensions.Economy.Abstractions;
using well404.Economy.Currency;

namespace well404.Economy
{
    /// <summary>
    /// Global <see cref="IEconomyProvider"/> implementation. Registered with
    /// <see cref="ServiceImplementationAttribute"/> so <b>any</b> plugin (daily
    /// check-in, well404.Shop, ...) can inject it. Like the official OpenMod.Economy
    /// provider, it is constructed in the plugin's scope, so it can read the
    /// plugin's <c>config.yaml</c> and working directory directly. It reads config
    /// on each call so live config reloads take effect, and routes to the
    /// configured <see cref="ICurrencyBackend"/>.
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class EconomyProvider : IEconomyProvider
    {
        private readonly IConfiguration m_Configuration;
        private readonly IOpenModComponent m_Component;
        private readonly IUserManager m_UserManager;

        private readonly object m_Lock = new object();
        private LiteDbCurrencyBackend? m_DbBackend;
        private ExperienceCurrencyBackend? m_XpBackend;

        public EconomyProvider(
            IConfiguration configuration,
            IOpenModComponent component,
            IUserManager userManager)
        {
            m_Configuration = configuration;
            m_Component = component;
            m_UserManager = userManager;
        }

        private EconomySettings ReadSettings()
            => m_Configuration.Get<EconomySettings>() ?? new EconomySettings();

        private ICurrencyBackend GetBackend(EconomySettings settings)
        {
            if (string.Equals(settings.Backend, "experience", StringComparison.OrdinalIgnoreCase))
            {
                lock (m_Lock)
                {
                    return m_XpBackend ??= new ExperienceCurrencyBackend(m_UserManager);
                }
            }

            var path = Path.Combine(m_Component.WorkingDirectory, settings.Database.FileName);
            lock (m_Lock)
            {
                if (m_DbBackend == null || !string.Equals(m_DbBackend.FilePath, path, StringComparison.Ordinal))
                {
                    m_DbBackend = new LiteDbCurrencyBackend(path, settings.Currency.StartingBalance);
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

        public Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType, decimal changeAmount, string? reason)
            => GetBackend(ReadSettings()).UpdateBalanceAsync(ownerId, ownerType, changeAmount, reason);

        public Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)
            => GetBackend(ReadSettings()).SetBalanceAsync(ownerId, ownerType, balance);
    }
}
