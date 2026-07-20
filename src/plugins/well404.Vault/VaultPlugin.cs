using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Eventing;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Core.Plugins.Events;
using OpenMod.Unturned.Plugins;
using UnturnedMods.Shared.WebPanel;

[assembly: PluginMetadata("well404.Vault", DisplayName = "Personal Vault")]

namespace well404.Vault
{
    /// <summary>
    /// A per-player personal storage: store and withdraw backpack items via commands or the web
    /// panel, with full item-state fidelity, capacity counted in inventory grid cells. The web panel
    /// is an optional integration — without it, the commands still work.
    /// </summary>
    public class VaultPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<VaultPlugin> m_Logger;

        private IPlayerMenuRegistry? m_PlayerMenuRegistry;
        private IWebPanelRegistry? m_WebPanelRegistry;
        // Captured at load so unload never resolves from the (by-then disposed) Autofac scope.
        private IPlayerCommandRegistry? m_PlayerCommandRegistry;
        private CancellationTokenSource? m_RecoveryCancellation;
        private Task? m_RecoveryTask;

        public VaultPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<VaultPlugin> logger,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
        }

        protected override async UniTask OnLoadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_start"]);

            var vault = LifetimeScope.Resolve<VaultService>();
            await vault.InitializeAsync(Path.Combine(WorkingDirectory, "vault.sqlite3"));

            var recovered = await vault.RecoverPendingTeamPurchasesAsync();
            if (recovered > 0)
            {
                m_Logger.LogWarning(
                    "Vault recovered {Count} paid vault-capacity purchase(s) left pending by an earlier interruption.",
                    recovered);
            }
            var interruptedTransfers = vault.InterruptedTransferCount;
            if (interruptedTransfers > 0)
            {
                m_Logger.LogWarning(
                    "Vault found {Count} interrupted inventory transfer audit record(s). They are left for manual reconciliation to avoid duplicating uncertain game inventory state.",
                    interruptedTransfers);
            }
            m_RecoveryCancellation = new CancellationTokenSource();
            // Start outside Unity's synchronization context. If this loop captures the main-thread
            // context, cancelling it during OpenMod shutdown deadlocks: unload waits for the loop,
            // while the loop waits for the main thread to run its cancellation continuation.
            var recoveryToken = m_RecoveryCancellation.Token;
            m_RecoveryTask = Task.Run(
                () => RunRecoveryLoopAsync(vault, recoveryToken), CancellationToken.None);

            await RegisterWebPanelExtensionsAsync();

            m_Logger.LogInformation("Vault loaded: base capacity {Slots} grid cells.", vault.BaseMaxSlots);
        }

        internal Task RegisterWebPanelExtensionsAsync()
        {
            var vault = LifetimeScope.Resolve<VaultService>();
            var translations = LifetimeScope.ResolveOptional<IWebTranslationRegistry>();
            translations?.AddBundle(VaultI18n.Zh, VaultI18n.ZhTable);

            if (m_PlayerMenuRegistry == null)
            {
                RegisterPlayerMenu(vault, translations);
            }

            if (m_WebPanelRegistry == null)
            {
                RegisterWebPanel(vault);
            }
            return Task.CompletedTask;
        }

        protected override async UniTask OnUnloadAsync()
        {
            var recoveryCancellation = m_RecoveryCancellation;
            var recoveryTask = m_RecoveryTask;
            recoveryCancellation?.Cancel();

            // Never let an auxiliary reconciliation loop block OpenMod reload/shutdown forever.
            // Normal cancellation is immediate; the timeout is only a last-resort guard for a
            // provider call that ignores its cancellation token.
            if (recoveryTask != null)
            {
                try
                {
                    var completed = await Task.WhenAny(
                            recoveryTask, Task.Delay(TimeSpan.FromSeconds(5)))
                        .ConfigureAwait(false);
                    if (completed == recoveryTask)
                    {
                        await recoveryTask.ConfigureAwait(false);
                        recoveryCancellation?.Dispose();
                        recoveryCancellation = null;
                    }
                    else
                    {
                        m_Logger.LogWarning(
                            "Vault recovery loop did not stop within 5 seconds; continuing plugin unload.");
                        if (recoveryCancellation != null)
                        {
                            var cancellationToDispose = recoveryCancellation;
                            _ = recoveryTask.ContinueWith(
                                _ => cancellationToDispose.Dispose(),
                                CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
                            recoveryCancellation = null;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    recoveryCancellation?.Dispose();
                    recoveryCancellation = null;
                }
            }
            m_RecoveryTask = null;
            recoveryCancellation?.Dispose();
            m_RecoveryCancellation = null;

            // Registry operations do not touch Unturned/Unity state, so they must not switch back
            // to a main thread that may already be paused by the runtime shutdown sequence.
            m_PlayerMenuRegistry?.UnregisterMenu(VaultPlayerMenu.MenuId);
            m_PlayerMenuRegistry = null;
            m_WebPanelRegistry?.UnregisterModule(VaultWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerCommandRegistry?.Unregister("well404.vault");
            m_PlayerCommandRegistry = null;
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        private async Task RunRecoveryLoopAsync(VaultService vault, CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    var recovered = await vault.RecoverPendingTeamPurchasesAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (recovered > 0)
                    {
                        m_Logger.LogWarning(
                            "Vault recovered {Count} paid vault-capacity purchase(s) during periodic reconciliation.",
                            recovered);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex, "Vault periodic capacity-purchase recovery failed; it will retry.");
                }
            }
        }

        internal Task RecoverPendingPurchasesAsync()
            => LifetimeScope.Resolve<VaultService>().RecoverPendingTeamPurchasesAsync();

        /// <summary>Registers the player-facing vault menu + intro command help, if a web panel is present.</summary>
        private void RegisterPlayerMenu(VaultService vault, IWebTranslationRegistry? translations)
        {
            var registry = LifetimeScope.ResolveOptional<IPlayerMenuRegistry>();
            if (registry == null || translations == null)
            {
                return;
            }

            registry.RegisterMenu(new VaultPlayerMenu(
                vault,
                LifetimeScope.Resolve<IUserManager>(),
                LifetimeScope.Resolve<OpenMod.Extensions.Games.Abstractions.Items.IItemDirectory>(),
                LifetimeScope.Resolve<OpenMod.API.Permissions.IPermissionChecker>(),
                translations));
            m_PlayerMenuRegistry = registry;

            m_PlayerCommandRegistry = LifetimeScope.ResolveOptional<IPlayerCommandRegistry>();
            m_PlayerCommandRegistry?.Register("well404.vault", new[]
            {
                new PlayerCommandInfo("/vault", "Open the personal vault — store, take and list your items.", "well404.Vault:commands.vault", "Vault"),
                new PlayerCommandInfo("/vault store <id> [amount]", "Store items from your backpack into the vault (by item id).", "well404.Vault:commands.vault.store", "Vault"),
                new PlayerCommandInfo("/vault take <id> [amount]", "Withdraw items from the vault into your backpack (by item id).", "well404.Vault:commands.vault.take", "Vault"),
                new PlayerCommandInfo("/vault list", "List your vault contents and how full it is.", "well404.Vault:commands.vault.list", "Vault"),
                new PlayerCommandInfo("/vault upgrade", "Spend your own balance to buy personal vault capacity.", "well404.Vault:commands.vault.upgrade", "Vault"),
                new PlayerCommandInfo("/vault team store <id> [amount]", "Store backpack items in your current party's shared vault.", "well404.Vault:commands.vault.team.store", "Vault"),
                new PlayerCommandInfo("/vault team take <id> [amount]", "Take items from your current party's shared vault.", "well404.Vault:commands.vault.team.take", "Vault"),
                new PlayerCommandInfo("/vault team list", "List the current party's shared vault.", "well404.Vault:commands.vault.team.list", "Vault"),
                new PlayerCommandInfo("/vault team upgrade", "Spend your own balance to buy shared vault capacity for the party.", "well404.Vault:commands.vault.team.upgrade", "Vault")
            });

            m_Logger.LogInformation("Vault: registered the player vault menu with the web panel.");
        }

        /// <summary>Registers capacity settings, unified vault inspection, and recovery data with the web panel, if present.</summary>
        private void RegisterWebPanel(VaultService vault)
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            registry.RegisterModule(VaultWebPanelModule.Create(
                new VaultConfigStore(m_Configuration, WorkingDirectory),
                vault,
                LifetimeScope.Resolve<OpenMod.Extensions.Games.Abstractions.Items.IItemDirectory>(),
                LifetimeScope.Resolve<IUserManager>()));
            m_WebPanelRegistry = registry;
        }
    }

    public sealed class WebPanelRegistrationListener : IEventListener<PluginLoadedEvent>
    {
        private readonly IPluginAccessor<VaultPlugin> m_PluginAccessor;

        public WebPanelRegistrationListener(IPluginAccessor<VaultPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

        public async Task HandleEventAsync(object? sender, PluginLoadedEvent @event)
        {
            var plugin = m_PluginAccessor.Instance;
            if (plugin != null)
            {
                await plugin.RegisterWebPanelExtensionsAsync().ConfigureAwait(false);
                await plugin.RecoverPendingPurchasesAsync().ConfigureAwait(false);
            }
        }
    }
}
