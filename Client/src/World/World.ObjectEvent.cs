using LibreKO.Network;
using Godot;

namespace LibreKO;

public partial class World
{
    private void ObjectEventInit()
    {
        Net.I.ObjectEventResultEvent += OnObjectEventResult;
        Net.I.ObjectEventGateStateEvent += OnObjectGateState;
        Net.I.NoticeEvent += OnJuraidNotice;
        Net.I.ChatEvent += OnJuraidChat;
    }

    private readonly System.Collections.Generic.HashSet<int> _openedGateUniqueIds = new();
    private readonly System.Collections.Generic.HashSet<int> _unlockedJuraidTraps = new();

    private void ObjectEventDispose()
    {
        Net.I.ObjectEventResultEvent -= OnObjectEventResult;
        Net.I.ObjectEventGateStateEvent -= OnObjectGateState;
        Net.I.NoticeEvent -= OnJuraidNotice;
        Net.I.ChatEvent -= OnJuraidChat;
        _openedGateUniqueIds.Clear();
        _unlockedJuraidTraps.Clear();
    }

    private void OnJuraidChat(ChatLine line)
    {
        if (!string.IsNullOrEmpty(line.Message))
            OnJuraidNotice(line.Message);
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
        if (gateOpen)
            _openedGateUniqueIds.Add(uniqueId);
        else
            _openedGateUniqueIds.Remove(uniqueId);

        if (_ents.TryGetValue(uniqueId, out var ent))
        {
            NoteGateState(ent.KoX, ent.KoZ, gateOpen);
            if (ent.IsBridge)
            {
                if (gateOpen)
                    StartLoweringBridge(ent);
                else
                    ResetBridge(ent);
                return;
            }
            if (ent.NpcType == NpcTypes.Lever) return;
            CombatNotice(gateOpen
                ? SystemText(TextGateOpened, "The Castle Gate has opened")
                : SystemText(TextGateClosed, "The Castle Gate has been closed"));
        }
    }

    private void OnJuraidNotice(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!text.Contains("Juraid Mountain", System.StringComparison.OrdinalIgnoreCase)) return;

        if (text.Contains("All bridges have been OPENED by GM", System.StringComparison.OrdinalIgnoreCase))
        {
            for (int t = 1; t <= 6; t++) _unlockedJuraidTraps.Add(t);
            Chat.Info("[Juraid] GM unlocked all bridges! Lowering all bridges...");
            foreach (var ent in _ents.Values)
            {
                if (ent.IsBridge)
                {
                    StartLoweringBridge(ent);
                }
            }
            return;
        }

        int trap = 0;
        if (text.Contains("Stage 1", System.StringComparison.OrdinalIgnoreCase) && text.Contains("El Morad", System.StringComparison.OrdinalIgnoreCase)) trap = 4;
        else if (text.Contains("Stage 2", System.StringComparison.OrdinalIgnoreCase) && text.Contains("El Morad", System.StringComparison.OrdinalIgnoreCase)) trap = 5;
        else if (text.Contains("Stage 3", System.StringComparison.OrdinalIgnoreCase) && text.Contains("El Morad", System.StringComparison.OrdinalIgnoreCase)) trap = 6;
        else if (text.Contains("Stage 1", System.StringComparison.OrdinalIgnoreCase) && text.Contains("Karus", System.StringComparison.OrdinalIgnoreCase)) trap = 1;
        else if (text.Contains("Stage 2", System.StringComparison.OrdinalIgnoreCase) && text.Contains("Karus", System.StringComparison.OrdinalIgnoreCase)) trap = 2;
        else if (text.Contains("Stage 3", System.StringComparison.OrdinalIgnoreCase) && text.Contains("Karus", System.StringComparison.OrdinalIgnoreCase)) trap = 3;

        if (trap > 0)
        {
            _unlockedJuraidTraps.Add(trap);
            Chat.Info($"[Juraid] Stage cleared! Lowering Bridge {trap}...");
            foreach (var ent in _ents.Values)
            {
                if (ent.IsBridge && TrapNumberForPosition(ent.KoX, ent.KoZ) == trap)
                {
                    StartLoweringBridge(ent);
                }
            }
        }
    }
}
