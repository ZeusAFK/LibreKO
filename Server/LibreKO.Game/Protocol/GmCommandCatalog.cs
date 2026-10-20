namespace LibreKO.Game.Protocol;

public readonly record struct GmCommand(string Group, string Syntax, string Description);

public static class GmCommandCatalog
{
    private const string Items = "Items and currency";
    private const string Character = "Character";
    private const string Players = "Players";
    private const string World = "World";
    private const string Monsters = "Monsters";
    private const string Events = "Events";
    private const string Quests = "Quests";
    private const string Server = "Server";

    public static readonly IReadOnlyList<GmCommand> All =
    [
        new(Items, "+item <itemId> [count] | <name>", "Give yourself an item, or search items by name"),
        new(Items, "+gold <amount>", "Give or take gold"),
        new(Items, "+kc <name> <amount>", "Give or take Knight Cash"),
        new(Items, "+clear [characterName]", "Clear an inventory (equipped gear is kept)"),
        new(Character, "+setlevel <1-83>", "Set the level; resets stats and mastery, clears the skill bar"),
        new(Character, "+exp <amount>", "Give experience"),
        new(Character, "+hp", "Restore HP and MP"),
        new(Character, "+gm", "Toggle GM mode: the GM aura, one-hit kills, 1 damage taken"),
        new(Character, "+sealcode <8 digits>", "Set this account's item seal security code"),
        new(Character, "+rank [name]", "Show a player's national ranking places"),
        new(Character, "+exp_change <name> <amount>", "Give or take a player's experience"),
        new(Character, "+np_change <name> <amount>", "Give or take a player's national points"),
        new(Players, "+summon <name|pattern>", "Bring a player, or everyone whose name matches (* = any text), to you"),
        new(Players, "+summonuser <name>", "Bring one player to you"),
        new(Players, "+goto <name> | <zone> <x> <z>", "Go to a player or to a spot"),
        new(Players, "+kick <name>", "Disconnect a player"),
        new(Players, "+forcerename <name>", "Make a character choose a new name at character selection"),
        new(Players, "+kill <name>", "Kill a player"),
        new(Players, "+mute <name> / +unmute <name>", "Stop or allow a player's chat"),
        new(Players, "+ban <name> / +unban <login>", "Ban or unban an account"),
        new(Players, "+hapis <name>", "Send a player to prison"),
        new(World, "+zone [id]", "List the zones, or go to a zone's home point"),
        new(World, "+time <hh[:mm]> | reset", "Set the game time and the event clock for everyone"),
        new(World, "+weather <clear|rain|snow|leaves> [0-100]", "Set the weather for everyone"),
        new(World, "+terrain <0-4>", "Show yourself a terrain effect: 0 none, 1 hay, 2 swamp, 3 water, 4 slowing swamp"),
        new(World, "+objturn <npcId> <degrees> [1|2]", "Turn an object NPC around you to an angle: 1 the angle grows, 2 it shrinks"),
        new(World, "+notice <text>", "Send a server notice"),
        new(World, "+online", "Show the online count"),
        new(World, "+countzone", "Show the ten busiest zones"),
        new(World, "+countlevel", "Show players by level band"),
        new(Monsters, "+mon <npcId> [count]", "Spawn monsters here"),
        new(Monsters, "+monsummon <npcId> [count]", "Spawn monsters here once; they never respawn"),
        new(Monsters, "+killall", "Kill every NPC in your zone"),
        new(Monsters, "+bots [status] | <count> [minLevel maxLevel] | stop", "BotSim bots around you (count is the total)"),
        new(Events, "+waropen <zoneId> / +warclose / +snowwar", "Open or close a national war"),
        new(Events, "+siegewar on|countdown|off", "Open the castle siege war now, after the 10-minute countdown, or close it"),
        new(Events, "+wanted on|off", "Run the Wanted event in Ronark Land and Ronark Land Base, or stop it"),
        new(Events, "+clanmatch <clan> <clan>|off", "Send up to 8 online members of each clan to Party Battle 1 for a 20-minute match, or end it"),
        new(Events, "+bifrost start [minutes] | close | enter | winner <karus|elmorad>", "Bifrost event"),
        new(Events, "+jr / +bdw / +chaos [sec|min] / +templecancel", "Temple events: Juraid Mountain, Border Defense War, Chaos Dungeon"),
        new(Events, "+utc open|close", "Under the Castle"),
        new(Events, "+krowaz open|close", "Krowaz's Dominion"),
        new(Events, "+ft [close|enter|wave <n>|boss]", "Forgotten Temple"),
        new(Events, "+cropen <eventIndex> / +crclose / +crstatus", "Collection Race"),
        new(Events, "+lottery start [id] | close | cancel", "Lottery event"),
        new(Events, "+soccer blue|red|start|end", "Moradon soccer: join a team, start a practice match that needs no second player, or end the match"),
        new(Events, "+santa / +angel / +offsanta / +offangel", "Santa and Angel"),
        new(Events, "+exp_add / +money_add / +np_add / +drop_add <0-255>", "Event bonus percent for experience, coins, national points or drops"),
        new(Quests, "+questinfo [id]", "Show your quests, or one quest's state"),
        new(Quests, "+questreset <id>", "Reset one of your quests"),
        new(Quests, "+reloadscripts", "Reload quest scripts without a restart"),
        new(Server, "+savemerchantbots", "Save the active merchant bots to the database"),
        new(Server, "+loadbotmerchant", "Load and spawn merchant bots from the database"),
        new(Server, "+clearmerchantbots", "Clear all active merchant bots"),
        new(Players, "+slave", "Activate Slave Merchant mode or view status"),
        new(Players, "+slaveclaim", "Claim earned Noah from your Slave Merchant"),
        new(Players, "+slaveclose", "Close your Slave Merchant and retrieve unsold items"),
        new(Players, "+slavestatus", "Check status of your Slave Merchant"),
        new(Server, "+dailyrank", "Recompute the daily rankings now; today's changes stay against yesterday"),
        new(Server, "+reseed", "Seed the JSON data into the database and reload (drops, NPCs, items)"),
        new(Server, "+?", "Open this list"),
    ];
}
