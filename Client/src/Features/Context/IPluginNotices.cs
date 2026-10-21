using Godot;

namespace LibreKO.Features;

internal interface IPluginNotices
{
    void LogAdd(GameLogKind kind, string text, Color colour);

    void Vendor();

    void Warehouse();

    void VipVault();

    void Trade();

    void Drops(int targetId);

    void Whispers();

    void WhisperLine(string name, RoomPost post);

    void WhisperCollapse(string name);

    void Hotbar();

    void HotbarReady(int[] ready);

    void Skills();

    void Quests();

    void Party();

    void Warp();

    void Inventory();

    void Genie();
}
