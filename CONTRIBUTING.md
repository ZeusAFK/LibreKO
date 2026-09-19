# Contributing to LibreKO

LibreKO is a from-scratch game server and client for a classic MMORPG: a .NET 10 server, a Godot 4
client written in C#, and a small quest language shared by both. Everything here is licensed under
the GNU Affero General Public License v3.0, and a contribution is offered under the same license.

The project belongs to everyone who works on it. Nobody's name goes into the code, a banner or a
screen; contributors are listed in `CONTRIBUTORS.md` at the repository root, with the contact
details they choose to give. How we treat each other is in `CODE_OF_CONDUCT.md`.

This file covers how to report a problem, how to propose a change, what a change has to satisfy,
and how review works. Every line of new code is held to it.

## Reporting a problem

Bugs, wrong behavior and missing features are reported as
[GitHub issues](https://github.com/ZeusAFK/LibreKO/issues). The issue list is the single source of
truth for what is broken and what is planned, so search it before filing.

New issues start from one of two templates. The bug report asks what happened, what you expected,
the steps that get there, where it happened and the evidence; the feature request asks what is
missing and how retail does it. Fill in every section the template has: a report that leaves out
the class, the zone or the steps waits until someone asks for them.

State what you observed, not what you suspect. One problem per issue. Put the area in the title,
as the templates show: the class, or `SYSTEM` for windows, items, quests, NPCs and the world.

Retail behavior is the specification. If the game behaves the way the original client and server
do, it is not a bug, and the issue is closed as such with an explanation. If you believe retail did
it differently, say what you observed and where.

Security problems in the server (anything that lets one player affect another's account or crash
the process) are reported as bugs too, marked as such in the title.

## Proposing a change

Open an issue before writing anything beyond a small, obvious fix: a bug report for a fix, a
feature request for anything new. The issue is where the approach is agreed. Reference it from
the commit message and the pull request.

One topic per pull request. A change that touches several unrelated things is split before it is
read.

The maintainer may take a change from a pull request and land it directly on `main` when that is the
quickest route, with the contributor named in the commit and in `CONTRIBUTORS.md`. The credit is the
same either way.

## Building and testing

The [README](README.md) has the full setup. In short:

- **Server**: .NET 10 SDK and a MariaDB or MySQL instance. `dotnet build Server/LibreKO.sln` builds
  the four projects and the three test projects.
- **Client**: Godot 4.7.2 .NET (mono). `dotnet build Client/LibreKO.csproj` compiles the C# side
  without opening the editor.
- **Tests**: `dotnet test Server/LibreKO.sln` runs the server suites and
  `dotnet test Client/tests/LibreKO.Tests` the client's.

Every suite passes on Windows and on Linux, with case-sensitive paths and LF line endings. Run the
tests on both platforms if you can; if you cannot, say which one you ran on in the pull request.

A behavior change comes with a test. A packet shape gets a test on the bytes; a rule gets a test on
the rule; a quest gets a test that plays it.

## What a change has to satisfy

**Language.** English only, in code, identifiers, strings, comments, commit messages and pull
request text. The one exception is the translation files under `Server/LibreKO.Game/Quests/lang`.

**No comments.** The code says what it does; the commit message says why. No `///` summaries, no
inline narration, no rationale blocks, no comment that counts things. A comment is accepted only
when it states a constraint a reader cannot get from the code, and then it is one line. Before you
push, run:

```
git diff -U0 main | grep -cE '^\+\s*(//|///)'
```

If the number is more than a couple for an ordinary change, remove them.

**No bare numbers.** Every domain value has a name: item ids, opcodes and sub-opcodes, slot counts,
wire sizes, fees, timers, zone ids. Extend the constant that already owns the concept rather
than adding a parallel one.

**File shape.** C# files are UTF-8 with a byte-order mark and CRLF line endings. Godot `.cfg` files
carry no byte-order mark, because the ConfigFile parser then misses the first section. The diff
shows only what you changed.

**Nothing left behind.** No debug prints, no commented-out code, no TODO markers, no unused fields.

**No credits.** No author names, handles, greetings or banners in code, console output or on screen.

**No secrets, no personal paths.** Tracked settings files carry no connection strings and no
passwords beyond the local-database defaults the shipped `appsettings.Development.json` files hold.
Nothing in the tree names a path on someone's machine.

## Architecture rules

The two halves mirror each other: the client's `src/Network`, `src/Domain` and `src/World` folders
follow the server's `Protocol`, `Common/Domain` and game layers, and types that cross the wire share
their names on both sides.

### Server

**Packet writers own the bytes.** Every byte written to a packet lives in
`Server/LibreKO.Game/Protocol/Writers/<Packet>PacketWriter.cs`. A writer takes primitives only,
never a session, character or NPC; a service maps domain objects onto it and never calls `Write*`
itself. Multiplexed opcodes use named static factories so a caller cannot assemble an invalid
combination. Every field has a name, including the zeros, and so does every wire constant, so that a
shape change fails a test instead of shipping. A gameplay cap is a separate constant from the wire
count it happens to equal.

**A packet has one shape.** A field is either always present or never present. Write it always,
with a neutral value when it does not apply.

**A wire change lands on both sides in the same change.** The client carries its own copy of every
layout.

**Dependencies are injected.** Constructor injection of the interface you need, never a service
locator. If injecting a dependency creates a cycle, raise it in the issue.

**Seed data is hand-maintained.** The JSON under `Server/LibreKO.Game/Seed/Data` is edited directly.
Keep the model normalized; where an old table layout has to survive, it survives in the packet
writer, not in the entities.

### Client

**The wire codec under `Client/src/Network/Protocol` mirrors the server's.** Framing, opcodes, LZF,
CRC and the cipher are a contract between the two halves: a change to any of them lands on both
sides in the same change.

**Adding a packet end to end.** The opcode goes into the shared opcode list; the parse lives in the
matching `Net.<Domain>.cs` partial and raises an event from the facade; the owning
`World.<Domain>.cs` or system subscribes. Outgoing packets are all in `Net.Senders.cs`.

**New world features are systems, not partials.** A new facet is a class that takes `IWorldContext`
and owns its state. The remaining `World` partials are being extracted into systems, and new work
does not add to them.

**Keys come from `KeyBinds`.** Every action is rebindable, so game code never tests a `Key`
directly.

**UI artwork goes through `UiIcons`.** No hardcoded paths into the asset tree from production UI.

**Anything the player never runs stays out of the shipped tree.** Asset baking lives in
[LibreKO-Assets](https://github.com/ZeusAFK/LibreKO-Assets), and development harnesses are not part
of this repository. A tool may depend on the game; the game never depends on a tool.

**Godot bookkeeping.** Scenes and `project.godot` reference scripts by path, and `.cs.uid` sidecars
travel with their scripts, so a moved file means re-pointed scenes. Baked table resources embed
their script's path too. The repository ships no game content: `Client/assets` is produced by the
LibreKO-Assets pipeline and stays ignored, apart from the original artwork, the icon set and the
pre-rendered minimaps.

### Quests

Quests are `.quest` files under `Server/LibreKO.Game/Quests`, in a small language with a versioned
specification. A change to its syntax, routing, state semantics or reward behavior updates the
specification, the compiler and the regression tests together. Files are named
`<npc>_<zone>_<quest>.quest`, with `0_0_<quest>.quest` for a quest bound to several NPCs; includes
come first and import names, locations and text, never handlers. `quest-manifest.json` lists what
the server loads, and every file it lists compiles with every include it names present in the tree.
Quest text is English in the files; other languages live in the translation catalog.

## Commits and pull requests

**A commit subject says what changed**, for a player or a developer, in one line, present tense, no
prefix tags:

```
Master skills spend one class stone per cast and keep the scroll
Character Info fits a 720p screen
```

The body, when there is one, carries the reasoning that would otherwise have become a comment: what
was wrong, what decided the fix, what was measured. Name the issue as `Fixes #N` when the change
closes it.

**The pull request description matches the diff.** It says what the change does, why, how it was
tested and on which platform. When the diff changes during review, the description changes with it.

**Keep the branch current with `main`** and the history readable: squash fix-up commits before
asking for a review.

## How review works

Reviews are GitHub reviews with comments on the lines concerned. Expect a full read of the diff,
a build of both halves, a test run on both platforms, and a comment on every rule above that the
change misses. A pull request stays at "changes requested" until each comment is addressed in a new
commit or answered in its thread; resolving a thread is the reviewer's call.

Every incoming change is also read for anything that has no place in a game server or client:
hidden network calls or listeners, hardcoded hosts, credentials or paths, encoded or obfuscated
blobs, binaries that cannot be read, process, registry or filesystem access outside the game's own
directories, and changes to deploy or build identity. Any of these is a rejection on its own.
