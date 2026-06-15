# unturned-mods

> 🇨🇳 中文说明见 [README.md](README.md)（主文档）。

A collection of **Unturned** plugins built on [OpenMod](https://openmod.github.io/), in a
multi-plugin monorepo. Each plugin builds and deploys independently but shares one build/runtime
environment. The repository is developed entirely by AI; the authoritative development standards and
change history live in [`memory/`](memory/README.md).

## Plugins

| Plugin | What it does |
| --- | --- |
| `well404.Economy` | Currency core — balances, `/pay`, kill rewards, the global `IEconomyProvider`. |
| `well404.Shop` | Item shop — buy/sell items and bundles, permission-tier discounts (depends on Economy). |
| `well404.WebPanel` | Web admin panel (plugins mount modules) **+ a player-facing web UI** (`/menu`: server intro, shop, wallet transfers, utilities). Token-in-path auth, optional built-in tunnel (cloudflared/ngrok). |
| `well404.Essentials` | Player utilities — home/tp/warp/gift/sleep/back/party, shared teleport rules, optional economy fees. |
| `well404.AdminTools` | Admin tools — godmode, kick, temporary ban/unban. |

## Languages (i18n)

- **Web** (both the admin and player panels) ships **English + Simplified Chinese**, switchable from
  a dropdown (English is the default). Each plugin provides translations as an "English-source-string
  key → translation map"; adding a language is just another map.
- **In-game** messages use OpenMod's per-plugin `translations.yaml` (English out of the box). A server
  admin can translate/replace that file to set the in-game language for everyone.

## Install

```bash
openmod install well404.Economy
openmod install well404.Shop        # pulls in Economy
openmod install well404.WebPanel    # optional: web panel + player UI
openmod install well404.Essentials
openmod install well404.AdminTools
openmod reload
```

## Build / local dev

Requires .NET SDK 8. See the Chinese docs ([`docs/README.md`](docs/README.md#本地构建与调试)) for the
`scripts/build.sh` workflow; per-plugin usage is documented under [`docs/`](docs/).

## License

CC BY-NC-SA 4.0 © well404
