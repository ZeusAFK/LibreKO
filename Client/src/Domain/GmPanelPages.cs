using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public sealed record GmPanelGroup(string Id, string Title, string Icon);

public sealed record GmPanelPage(string Id, string Group, string Title, string Icon, bool GameMasterOnly = true, bool UsesTarget = false,
    bool FillsHeight = false);

public static class GmPanelPages
{
    public const string PlayersGroup = "players";
    public const string EventsGroup = "events";
    public const string WorldGroup = "world";
    public const string ServerGroup = "server";
    public const string SelfGroup = "self";

    public const string Find = "players-find";
    public const string Player = "players-player";
    public const string Inventory = "players-inventory";
    public const string Calendar = "events-calendar";
    public const string Lotteries = "events-lotteries";
    public const string Races = "events-races";
    public const string Tips = "events-tips";
    public const string Travel = "world-travel";
    public const string Monsters = "world-monsters";
    public const string Monster = "world-monster";
    public const string WorldControl = "world-control";
    public const string Commands = "server-commands";
    public const string Tools = "server-tools";
    public const string SelfCharacter = "self-character";
    public const string SelfItems = "self-items";
    public const string SelfClass = "self-class";
    public const string SelfSkills = "self-skills";

    public static readonly IReadOnlyList<GmPanelGroup> Groups =
    [
        new(PlayersGroup, "Players", "system/users-three"),
        new(EventsGroup, "Events", "system/clock"),
        new(WorldGroup, "World", "system/pin"),
        new(ServerGroup, "Server", "system/gear"),
        new(SelfGroup, "My character", "system/star"),
    ];

    public static readonly IReadOnlyList<GmPanelPage> All =
    [
        new(Find, PlayersGroup, "Find", "system/search"),
        new(Player, PlayersGroup, "Player", "system/user", UsesTarget: true),
        new(Inventory, PlayersGroup, "Inventory", "system/bag", UsesTarget: true),
        new(Calendar, EventsGroup, "Calendar", "system/clock"),
        new(Lotteries, EventsGroup, "Lotteries", "system/ticket"),
        new(Races, EventsGroup, "Collection races", "system/trophy"),
        new(Tips, EventsGroup, "Gameplay tips", "system/chat-circle"),
        new(Travel, WorldGroup, "Travel", "system/door", GameMasterOnly: false, FillsHeight: true),
        new(Monsters, WorldGroup, "Monsters & NPCs", "system/combat-attack"),
        new(Monster, WorldGroup, "Monster", "system/sliders"),
        new(WorldControl, WorldGroup, "World control", "system/sparkle"),
        new(Commands, ServerGroup, "Commands", "system/scroll", FillsHeight: true),
        new(Tools, ServerGroup, "Tools", "system/hammer"),
        new(SelfCharacter, SelfGroup, "Character", "system/user", GameMasterOnly: false),
        new(SelfItems, SelfGroup, "Items", "system/gift", GameMasterOnly: false, FillsHeight: true),
        new(SelfClass, SelfGroup, "Class", "system/medal", GameMasterOnly: false),
        new(SelfSkills, SelfGroup, "Skills", "system/level", GameMasterOnly: false),
    ];

    public static GmPanelPage? Get(string id) => All.FirstOrDefault(page => page.Id == id);

    public static GmPanelGroup? GroupOf(GmPanelPage page) => Groups.FirstOrDefault(group => group.Id == page.Group);

    public static IReadOnlyList<GmPanelPage> For(bool gameMaster) =>
        All.Where(page => gameMaster || !page.GameMasterOnly).ToList();

    public static string FirstPage(bool gameMaster) => gameMaster ? Find : SelfCharacter;
}
