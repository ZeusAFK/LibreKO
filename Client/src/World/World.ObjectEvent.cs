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

    private const int TextGateClosed = 1801;
    private const int TextGateOpened = 1802;
    private const int TextLeverFailed = 1005;

    private void OnObjectEventResult(byte type, bool success, int objectId)
    {
        if (!success && type is Net.ObjectEventGateLever or Net.ObjectEventFlagLever)
        {
            CombatNotice(SystemText(TextLeverFailed, "Failed turning the lever"));
            return;
        }
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

    private void OnObjectGateState(int uniqueId, bool gateOpen)
    {
        if (!_ents.TryGetValue(uniqueId, out var ent)) return;
        NoteGateState(ent.KoX, ent.KoZ, gateOpen);
        if (ent.NpcType == NpcTypes.Lever) return;
        CombatNotice(gateOpen
            ? SystemText(TextGateOpened, "The Castle Gate has opened")
            : SystemText(TextGateClosed, "The Castle Gate has been closed"));
    }
}
