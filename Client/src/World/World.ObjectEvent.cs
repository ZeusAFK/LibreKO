using LibreKO.Network;
using Godot;

namespace LibreKO;

public partial class World
{
    private void ObjectEventInit()
    {
        Net.I.ObjectEventResultEvent += OnObjectEventResult;
        Net.I.ObjectEventGateStateEvent += OnObjectGateState;
    }

    private void ObjectEventDispose()
    {
        Net.I.ObjectEventResultEvent -= OnObjectEventResult;
        Net.I.ObjectEventGateStateEvent -= OnObjectGateState;
    }

    private const string AnvilSuccessFx = "item_success";
    private const string AnvilFailFx = "item_fail";

    public bool TryOperateObject(short objectIndex, int npcId)
    {
        if (!_worldReady || _selfDead) return false;
        Net.I.SendObjectEvent(objectIndex, npcId);
        return true;
    }

    private void OnObjectEventResult(byte type, bool success, int objectId)
    {
        if (type == Net.ObjectEventAnvil)
        {
            SpawnAnvilFx(objectId, success ? AnvilSuccessFx : AnvilFailFx);
            return;
        }
        switch (type)
        {
            case Net.ObjectEventBind when success:
                Chat.Info("Recall point set.");
                break;
            case Net.ObjectEventRemoveBind when success:
                Chat.Info("Recall point cleared.");
                break;
        }
    }

    private void SpawnAnvilFx(int anvilId, string fx)
    {
        var anchor = AnvilAnchor(anvilId);
        if (anchor != null) Fx.Spawn(fx, anchor, Vector3.Zero, oneShot: true);
    }

    private void OnObjectGateState(int uniqueId, int npcId, byte npcType, int maxHp, int hp, bool gateOpen)
    {
        string label = _ents.TryGetValue(uniqueId, out var ent) && ent.Name.Length > 0 ? ent.Name : "The gate";
        Chat.Info($"{label} {(gateOpen ? "opens" : "closes")}.");
    }
}
