# LibreKO

An open-source game server and client for a classic MMORPG, written in C# (.NET 10) and Godot 4 (C#).

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
