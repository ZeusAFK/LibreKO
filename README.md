<div align="center">

# LibreKO

### An open-source game server and client for a classic MMORPG, written in C# (.NET 10) and Godot 4 (C#).

[![Website](https://img.shields.io/badge/website-libreko.org-0a7cff?style=flat-square)](https://libreko.org)
[![Tests](https://img.shields.io/github/actions/workflow/status/ZeusAFK/LibreKO/tests.yml?branch=main&style=flat-square&label=tests)](https://github.com/ZeusAFK/LibreKO/actions/workflows/tests.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white)](#requirements)
[![Godot 4.7](https://img.shields.io/badge/Godot-4.7_.NET-478cbf?style=flat-square&logo=godotengine&logoColor=white)](#client)
[![AGPL-3.0 license](https://img.shields.io/badge/license-AGPL--3.0-a42e2b?style=flat-square)](LICENSE)
[![GitHub stars](https://img.shields.io/github/stars/ZeusAFK/LibreKO?style=flat-square&color=f4c430)](https://github.com/ZeusAFK/LibreKO/stargazers)
[![Discord](https://img.shields.io/discord/1542258360903139388?style=flat-square&logo=discord&logoColor=white&label=Discord&color=5865f2)](https://discord.gg/wyQ335WfPx)
[![Buy Me a Coffee](https://img.shields.io/badge/Buy_Me_a_Coffee-support-ffdd00?style=flat-square&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/zeusafk)

</div>

- `Server/` — the login server, the game server, the shared library and the quest language compiler.
- `Client/` — the Godot 4 client project.

Both are licensed under the GNU Affero General Public License v3.0, see [LICENSE](LICENSE).
Third-party notices for the server are in [Server/NOTICES.md](Server/NOTICES.md).

## Requirements

- .NET 10 SDK
- MariaDB 10.6 or newer (MySQL works too)
- Godot 4.7.2 .NET (mono) edition, for the client

## Server

The server reads `appsettings.json`, then `appsettings.<Environment>.json`, then environment variables.
`DOTNET_ENVIRONMENT=Development` is what the run profiles use; the shipped
`appsettings.Development.json` in `LibreKO.Login` and `LibreKO.Game` points at a local database:

```
Server=localhost;Port=3306;Database=libreko;User=libreko;Password=libreko
```

Create that database and user (or change the connection string), then start the two servers from
their own project directories, because the game server loads its maps, quests and seed data relative
to the working directory:

```
cd Server/LibreKO.Login
dotnet run

cd Server/LibreKO.Game
dotnet run
```

On first start the game server applies the database migrations and seeds every game table from
`LibreKO.Game/Seed/Data`; this takes a few minutes the first time. The login server listens on TCP
15100 and the game server on TCP 15001. The address the login server advertises for the game server
comes from `LibreKO.Login/Seed/Data/Servers.json` (127.0.0.1 as shipped); change it and re-seed if
the game server runs on another host.

Any setting can be overridden with environment variables, for example
`ConnectionStrings__Default` or `GameServer__BindPort`.

## Client

The repository ships no game content. Bake it from your own game installation with the
[LibreKO-Assets](https://github.com/ZeusAFK/LibreKO-Assets) pipeline and point its output at the
client's asset folder:

```
python bake.py --ko "<your game install>" --out <this repo>/Client/assets
```

Everything under `Client/assets` except the original artwork in `backgrounds/`, the icons in
`ui/icons/` and the pre-rendered `terrain/<zone>/minimap.webp` images is produced by that pipeline
and ignored by git. Run it from inside this checkout so it
finds `Server/LibreKO.Game/Seed/Data` on its own; the skill bake joins those tables for buff
durations and speeds (otherwise pass `--server-data`). The first Godot start after a bake imports
the new files, which takes a while.

Open `Client/project.godot` with Godot 4.7.2 .NET and press Play, or build the C# project first with:

```
cd Client
dotnet build
```

The repository ships no export presets. To build a standalone executable or an APK, create a preset
under Project > Export in the Godot editor; the game itself runs from the editor without one.
Godot exports only the files it recognizes as resources, so set the preset's Resources > Filters to
export non-resource files to `settings.default.cfg,version.txt,shaders/*.comp,assets/terrain/*/collision.bin`.
Without the last pattern the exported game has no object collision: characters sink into stairs and
bridges and walk through walls. An export split into several packs may ship these files from
different presets; the client tests check that each of them is in some preset's filter in
`Client/export_presets.cfg`.

The first import of the assets takes a while. The client connects to `127.0.0.1:15100` by default;
copy `Client/settings.default.cfg` to `Client/settings.cfg` (created on first run) to change the
host, port, language and video settings.

## Docker

`docker compose up` at the repository root builds the two servers and starts them with a MariaDB
instance, exposing 15100 and 15001. This setup has not been exercised on every platform yet; report
what breaks.

## Quest scripts

Quests live in `Server/LibreKO.Game/Quests` as `.quest` files, a small language described by the
`LibreKO.Quests` compiler. `quest-manifest.json` lists the files the server loads.

## Support

LibreKO is free and always will be. If you want to help its development:

- Buy Me a Coffee: https://buymeacoffee.com/zeusafk
- Crypto (USDT, USDC or the network's own coin):
  - **Ethereum, BNB Smart Chain or Polygon:** `0xD8b7c496d0DdA069d6e7F5d72afBEb76C8e3D697`
  - **Solana:** `57JWyWxcY9BuK9jinj7Hf4b2ieyVQDL7j1aXg39GF8FS`
  - **Tron (TRC-20):** `TLg1Jhy51vL6ekity1pV3nUFNPhAJHKptP`

Send only on the networks listed next to each address.

Donations support development only and buy nothing in game.

## Star History

Thanks to everyone using LibreKO, reporting bugs, requesting features, and contributing fixes.

<a href="https://www.star-history.com/?repos=zeusafk%2Flibreko&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=zeusafk/libreko&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=zeusafk/libreko&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=zeusafk/libreko&type=date&legend=top-left" />
 </picture>
</a>
