using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<bool, string, int>? UpgradeNoticeEvent;

    private bool _upgradeNoticeWired;

    public void UpgradeNoticeWireUp()
    {
        if (_upgradeNoticeWired) return;
        _upgradeNoticeWired = true;
        ShoutUpgradeEvent += (ok, name, itemId, _) => UpgradeNoticeEvent?.Invoke(ok, name, itemId);
    }

    public void SendWatchUpgrade(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_UPGRADE_NOTICE);
        p.WriteInt(itemId);
        _conn.Send(p);
    }
}
