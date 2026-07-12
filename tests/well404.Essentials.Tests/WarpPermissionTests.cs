using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using OpenMod.API;
using OpenMod.API.Permissions;
using OpenMod.API.Persistence;
using OpenMod.API.Plugins;
using well404.Essentials.Warps;
using Xunit;

namespace well404.Essentials.Tests
{
    public class WarpPermissionTests
    {
        private sealed class FakeComponent : IOpenModComponent
        {
            public string OpenModComponentId => "well404.Essentials";
            public string WorkingDirectory => string.Empty;
            public bool IsComponentAlive => true;
            public ILifetimeScope LifetimeScope => null!;
            public IDataStore? DataStore => null;
        }

        private sealed class FakePluginAccessor : IPluginAccessor<EssentialsPlugin>
        {
            public EssentialsPlugin? Instance => null;
        }

        private sealed class FakePermissionRegistry : IPermissionRegistry
        {
            public List<string> Registered { get; } = new List<string>();

            public void RegisterPermission(
                IOpenModComponent component,
                string permission,
                string? description = null,
                PermissionGrantResult? defaultGrant = null)
                => Registered.Add(permission);

            public IReadOnlyCollection<IPermissionRegistration> GetPermissions(IOpenModComponent component)
                => new List<IPermissionRegistration>();

            public IPermissionRegistration? FindPermission(string permission) => null;

            public IPermissionRegistration? FindPermission(IOpenModComponent component, string permission) => null;
        }

        private sealed class FakePermissionChecker : IPermissionChecker
        {
            public string? LastPermission { get; private set; }

            public IReadOnlyCollection<IPermissionCheckProvider> PermissionCheckProviders { get; }
                = new List<IPermissionCheckProvider>();

            public IReadOnlyCollection<IPermissionStore> PermissionStores { get; }
                = new List<IPermissionStore>();

            public Task<PermissionGrantResult> CheckPermissionAsync(IPermissionActor actor, string permission)
            {
                LastPermission = permission;
                return Task.FromResult(PermissionGrantResult.Grant);
            }

            public Task InitAsync() => Task.CompletedTask;
        }

        private sealed class FakeActor : IPermissionActor
        {
            public string Id => "player";
            public string Type => "player";
            public string DisplayName => "Player";
            public string FullActorName => "Player (player)";
        }

        [Fact]
        public async Task ExistingWarp_IsRegisteredAndCheckedWithQualifiedPermission()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["warps:0:name"] = "绿洲",
                    ["warps:0:x"] = "1",
                    ["warps:0:y"] = "2",
                    ["warps:0:z"] = "3"
                })
                .Build();
            var store = new EssentialsConfigStore(configuration, new FakePluginAccessor());
            var registry = new FakePermissionRegistry();
            var checker = new FakePermissionChecker();
            var service = new WarpService(store, checker, registry, new FakeComponent());

            Assert.Contains("well404.essentials.warps.绿洲", registry.Registered);
            Assert.True(await service.HasAccessAsync(new FakeActor(), "绿洲"));
            Assert.Equal("well404.Essentials:well404.essentials.warps.绿洲", checker.LastPermission);
        }
    }
}
