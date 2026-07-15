# well404.UnturnedMods.Shared

Runtime abstractions shared by the well404 OpenMod plugin family.

This is a dependency package, not a standalone OpenMod plugin. Install feature plugins such as
`well404.WebPanel`, `well404.Essentials`, or `well404.AdminTools`; OpenMod/NuGet installs this package
automatically. Keeping the shared interfaces in one package ensures every plugin uses the same
assembly identity for cross-plugin registries and services.

Version 1.3 extends the shared `LocalizedItemCatalog`: in addition to bilingual vanilla/workshop
names and authoritative `ItemAsset.showQuality`, it exposes normalized native item type, rarity,
rarity rank and broad category metadata for consistent plugin sorting, filtering and presentation.

It retains the Shared 1.0/1.1 public constructor signatures, so already-published plugins can run beside new 1.3 consumers without recompilation.

The package also defines WebUiExtension and IPlayerMenuUiProvider. Complex plugins can embed their
own HTML, CSS and JavaScript while simple plugins continue using the descriptor renderer. WebPanel
mounts custom resources in Shadow DOM and supplies a capability-scoped runtime.
