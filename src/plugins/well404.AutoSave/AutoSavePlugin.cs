using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Eventing;
using OpenMod.API.Plugins;
using OpenMod.Core.Plugins.Events;
using OpenMod.Unturned.Plugins;
using UnturnedMods.Shared.WebPanel;

[assembly: PluginMetadata("well404.AutoSave", DisplayName = "Auto Save")]

namespace well404.AutoSave
{
    /// <summary>
    /// Periodically saves the game on a cron schedule and, every Nth save, writes a compressed backup
    /// of the server's savedata. There are no in-game commands: everything is configured through
    /// <c>config.yaml</c> and, when well404.WebPanel is installed, its admin panel.
    /// </summary>
    public class AutoSavePlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<AutoSavePlugin> m_Logger;

        private readonly object m_SchedulerLock = new object();

        private AutoSaveConfigStore? m_ConfigStore;
        private BackupService? m_BackupService;
        private SaveService? m_SaveService;
        private SchedulerLoop? m_SchedulerLoop;
        private CancellationTokenSource? m_SchedulerCts;
        private Task? m_SchedulerTask;
        private bool m_Stopped;

        private IWebPanelRegistry? m_WebPanelRegistry;

        public AutoSavePlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<AutoSavePlugin> logger,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
        }

        protected override async UniTask OnLoadAsync()
        {
            // Switch to the main thread before calling any Unturned / UnityEngine API.
            await UniTask.SwitchToMainThread();
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_start"]);

            var configStore = new AutoSaveConfigStore(m_Configuration, WorkingDirectory);
            var backupService = new BackupService(new TarLzmaArchiver(), m_Logger);
            var stateStore = new SaveStateStore(WorkingDirectory, m_Logger);
            var saveService = new SaveService(backupService, stateStore, configStore, m_Logger);

            m_ConfigStore = configStore;
            m_BackupService = backupService;
            m_SaveService = saveService;
            m_SchedulerLoop = new SchedulerLoop(m_Logger);

            configStore.Changed += OnSettingsChanged;
            StartScheduler();

            // Paths are resolved per run (the server id is not known until the level is up), so the
            // web module is given a factory rather than a captured value.
            RegisterWebPanelExtension();

            var settings = configStore.Current;
            m_Logger.LogInformation(
                "Auto Save loaded: cron '{Cron}', backups {State} (every {N} saves). Paths resolve at save time.",
                settings.Schedule.Cron,
                settings.Backup.Enabled && settings.Backup.EveryNSaves > 0 ? "on" : "off",
                settings.Backup.EveryNSaves);
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();

            if (m_ConfigStore != null)
            {
                m_ConfigStore.Changed -= OnSettingsChanged;
            }

            Task? running;
            lock (m_SchedulerLock)
            {
                m_Stopped = true;
                m_SchedulerCts?.Cancel();
                running = m_SchedulerTask;
            }

            if (running != null)
            {
                try
                {
                    // ConfigureAwait(false): don't force the continuation back onto the main-thread
                    // context (the rest of unload needs no Unity API), mirroring the scheduler loop.
                    await running.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex, "Auto Save: scheduler did not stop cleanly.");
                }
            }

            lock (m_SchedulerLock)
            {
                m_SchedulerCts?.Dispose();
                m_SchedulerCts = null;
                m_SchedulerTask = null;
            }

            m_WebPanelRegistry?.UnregisterModule(AutoSaveWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_BackupService = null;

            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        internal void RegisterWebPanelExtension()
        {
            if (m_WebPanelRegistry != null || m_ConfigStore == null || m_SaveService == null || m_BackupService == null)
            {
                return;
            }

            RegisterWebPanel(m_ConfigStore, m_SaveService, m_BackupService, SavePaths.Capture);
        }

        private void OnSettingsChanged()
        {
            m_Logger.LogInformation("Auto Save: settings changed; restarting the scheduler.");
            StartScheduler();
        }

        /// <summary>(Re)builds the cron schedule from current settings and runs the loop.</summary>
        private void StartScheduler()
        {
            var configStore = m_ConfigStore;
            var saveService = m_SaveService;
            var loop = m_SchedulerLoop;
            if (configStore == null || saveService == null || loop == null)
            {
                return;
            }

            var settings = configStore.Current;
            var cronError = CronSchedule.Validate(settings.Schedule.Cron);
            if (cronError != null)
            {
                m_Logger.LogError("Auto Save: invalid cron '{Cron}' ({Error}); scheduler not started.",
                    settings.Schedule.Cron, cronError);
                return;
            }

            var zone = AutoSaveTimeZone.Resolve(settings.Schedule.TimeZone);
            var schedule = new CronSchedule(settings.Schedule.Cron, zone);

            lock (m_SchedulerLock)
            {
                if (m_Stopped)
                {
                    return;
                }

                m_SchedulerCts?.Cancel();
                m_SchedulerCts?.Dispose();

                var cts = new CancellationTokenSource();
                m_SchedulerCts = cts;
                m_SchedulerTask = loop.RunAsync(schedule, () => saveService.TickAsync(), cts.Token);
            }
        }

        private void RegisterWebPanel(
            AutoSaveConfigStore configStore, SaveService saveService, BackupService backupService, Func<SavePaths> pathsFactory)
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            var translations = LifetimeScope.ResolveOptional<IWebTranslationRegistry>();
            translations?.AddBundle(AutoSaveI18n.Zh, AutoSaveI18n.ZhTable);

            // The web module localizes its messages via the registry; fall back to a no-op resolver
            // shouldn't happen because the panel always registers IWebTranslationRegistry alongside it.
            if (translations == null)
            {
                m_Logger.LogWarning("Auto Save: web panel present but no translation registry; skipping web module.");
                return;
            }

            registry.RegisterModule(AutoSaveWebPanelModule.Create(configStore, saveService, backupService, pathsFactory, translations));
            m_WebPanelRegistry = registry;
        }
    }

    public sealed class WebPanelRegistrationListener : IEventListener<PluginLoadedEvent>
    {
        private readonly IPluginAccessor<AutoSavePlugin> m_PluginAccessor;

        public WebPanelRegistrationListener(IPluginAccessor<AutoSavePlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

        public Task HandleEventAsync(object? sender, PluginLoadedEvent @event)
        {
            m_PluginAccessor.Instance?.RegisterWebPanelExtension();
            return Task.CompletedTask;
        }
    }
}
