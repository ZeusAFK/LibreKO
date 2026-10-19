namespace LibreKO.Network.Readers;

internal sealed class ZoneAbilityReader : IPacketReader
{
    private readonly HostilityRules hostility;
    private readonly ZoneAbilitySession zoneAbility;

    public ZoneAbilityReader(HostilityRules hostility, ZoneAbilitySession zoneAbility)
    {
        this.hostility = hostility;
        this.zoneAbility = zoneAbility;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ZONEABILITY, HandleZoneAbility);
    }

    private void HandleZoneAbility(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ZoneAbilitySubOpcode)p.ReadByte();
        if (sub == ZoneAbilitySubOpcode.UserStatus)
        {
            HandleOwnStatus(p);
            return;
        }
        if (sub != ZoneAbilitySubOpcode.Update || p.RemainingBytes < 5) return;
        bool canTrade = p.ReadByte() != 0;
        var zoneType = (ZoneAbilityType)p.ReadByte();
        bool canTalk = p.ReadByte() != 0;
        int tariff = p.ReadUShort();

        zoneAbility.ApplyZoneAbility(new ZoneAbilityInfo
        {
            ZoneType = zoneType,
            Tariff = tariff,
            CanTrade = canTrade,
            CanTalk = canTalk,
        });
        hostility.RefreshKnownHostility();
        zoneAbility.RaiseZoneAbility(zoneAbility.CurrentZoneAbility);
    }

    private void HandleOwnStatus(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte type = p.ReadByte();
        bool inflicted = (UserStatusChange)p.ReadByte() == UserStatusChange.Inflicted;
        zoneAbility.SetOwnStatus(type, inflicted);
        zoneAbility.RaiseOwnStatus(type, inflicted);
    }
}
