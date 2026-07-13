# well404.UnturnedMods.Shared

Runtime abstractions shared by the well404 OpenMod plugin family.

This is a dependency package, not a standalone OpenMod plugin. Install feature plugins such as
`well404.WebPanel`, `well404.Essentials`, or `well404.AdminTools`; OpenMod/NuGet installs this package
automatically. Keeping the shared interfaces in one package ensures every plugin uses the same
assembly identity for cross-plugin registries and services.

Version 1.1 adds generic WebPanel schema primitives for player prompt choices and confirmations, stable group keys, compact group-header actions, grouped admin records, and collection reorder handlers.
It retains the Shared 1.0 public constructor signatures, so already-published plugins can run beside
new 1.1 consumers without recompilation.

The package also defines WebUiExtension and IPlayerMenuUiProvider. Complex plugins can embed their
own HTML, CSS and JavaScript while simple plugins continue using the descriptor renderer. WebPanel
mounts custom resources in Shadow DOM and supplies a capability-scoped runtime.
