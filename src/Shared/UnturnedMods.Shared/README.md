# well404.UnturnedMods.Shared

Runtime abstractions shared by the well404 OpenMod plugin family.

This is a dependency package, not a standalone OpenMod plugin. Install feature plugins such as
`well404.WebPanel`, `well404.Essentials`, or `well404.AdminTools`; OpenMod/NuGet installs this package
automatically. Keeping the shared interfaces in one package ensures every plugin uses the same
assembly identity for cross-plugin registries and services.
