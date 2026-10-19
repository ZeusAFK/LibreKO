namespace LibreKO.Network.Readers;

internal sealed class AdminReader : IPacketReader
{
    private readonly AdminSession admin;
    private readonly ProgressSession progress;
    private readonly SelfSession self;

    private const int FindMonsterFlag = 1;
    private const int FindBotFlag = 2;
    private const int FindRowMinBytes = 17;
    private const int SpawnRowMinBytes = 31;
    private const int CollectionRaceRowMinBytes = 26;

    public AdminReader(AdminSession admin, ProgressSession progress, SelfSession self)
    {
        this.admin = admin;
        this.progress = progress;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<AdminReplySubOpcode>(GameOpcodes.GS_ADMIN_PANEL, HandleAdminPanel);
    }

    private void HandleAdminPanel(Packet p, AdminReplySubOpcode sub)
    {
        switch (sub)
        {
            case AdminReplySubOpcode.State: ParseAdminState(p); break;
            case AdminReplySubOpcode.CollectionRaces: ParseAdminCollectionRaces(p); break;
            case AdminReplySubOpcode.Find: ParseAdminFind(p); break;
            case AdminReplySubOpcode.SpawnRow: ParseAdminSpawnRow(p); break;
            case AdminReplySubOpcode.Commands:
                {
                    var (commands, open) = AdminWire.ReadCommands(p);
                    admin.RaiseAdminCommands(commands, open);
                    break;
                }
            case AdminReplySubOpcode.Result:
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                admin.RaiseAdminResult(ok, p.RemainingBytes >= 2 ? p.ReadString() : "");
                break;
            case AdminReplySubOpcode.Grant:
                var grant = p.RemainingBytes >= 1 ? (AdminPanelGrant)p.ReadByte() : AdminPanelGrant.None;
                bool speedGranted = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                admin.ApplyGrant(grant, speedGranted);
                break;
        }
    }

    private void ParseAdminState(Packet p)
    {
        var state = new AdminState
        {
            Granted = p.RemainingBytes >= 1 && p.ReadByte() == 1,
            SkillPoints = new byte[9],
            ClassOptions = Array.Empty<int>(),
        };
        if (!state.Granted || p.RemainingBytes < 25)
        {
            state.Granted = false;
            admin.RaiseAdminState(state);
            return;
        }

        state.Class = p.ReadShort();
        state.Level = p.ReadByte();
        state.Str = p.ReadByte();
        state.Sta = p.ReadByte();
        state.Dex = p.ReadByte();
        state.Intel = p.ReadByte();
        state.MagicStat = p.ReadByte();
        state.StatPoints = p.ReadShort();
        state.MaxHp = p.ReadShort();
        state.MaxMp = p.ReadShort();
        state.Ap = p.ReadShort();
        state.Ac = p.ReadShort();
        state.Gold = p.ReadInt();
        for (int i = 0; i < 9 && p.RemainingBytes >= 1; i++)
            state.SkillPoints[i] = p.ReadByte();

        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        var options = new int[Math.Min(count, p.RemainingBytes / 2)];
        for (int i = 0; i < options.Length; i++)
            options[i] = p.ReadShort();
        state.ClassOptions = options;

        state.Nation = p.RemainingBytes >= 1 ? p.ReadByte() : self.LastEnter.Nation;
        state.Race = p.RemainingBytes >= 1 ? p.ReadByte() : self.LastEnter.Race;
        state.Loyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;

        if (state.Class != 0) self.ApplyClassChange(state.Class);

        // The 3D body model is only rebuilt on world-enter, so it needs a relog to change.
        self.ChangeOrigin(state.Nation, state.Race);
        self.ApplyAdminState(state);
        progress.ApplyGold(state.Gold);

        admin.RaiseAdminState(state);
    }

    private void ParseAdminCollectionRaces(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int count = p.ReadUShort();
        var rows = new List<AdminCollectionRace>(count);
        for (int i = 0; i < count && p.RemainingBytes >= CollectionRaceRowMinBytes; i++)
        {
            rows.Add(new AdminCollectionRace
            {
                Id = p.ReadInt(),
                Name = p.ReadSByteString(),
                ZoneId = p.ReadByte(),
                MinLevel = p.ReadByte(),
                MaxLevel = p.ReadByte(),
                DurationMinutes = p.ReadInt(),
                AutoStart = p.ReadByte() == 1,
                Active = p.ReadByte() == 1,
                RemainingSeconds = p.ReadInt(),
                Completions = p.ReadInt(),
                MaxWinners = p.ReadInt(),
                Schedule = p.ReadSByteString(),
                Objectives = p.ReadSByteString(),
            });
        }
        admin.RaiseAdminCollectionRaces(rows);
    }

    private void ParseAdminFind(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int kind = p.ReadByte();
        int total = p.ReadUShort();
        int count = p.ReadUShort();
        var hits = new List<AdminFindHit>(count);
        for (int i = 0; i < count && p.RemainingBytes >= FindRowMinBytes; i++)
        {
            var hit = new AdminFindHit { Id = p.ReadInt(), SpawnRow = p.ReadInt(), Name = p.ReadSByteString() };
            hit.Level = p.ReadShort();
            hit.Zone = p.ReadByte();
            hit.X = p.ReadUShort();
            hit.Z = p.ReadUShort();
            int flags = p.ReadByte();
            hit.Monster = (flags & FindMonsterFlag) != 0;
            hit.Bot = (flags & FindBotFlag) != 0;
            hits.Add(hit);
        }
        admin.RaiseAdminFind(kind, total, hits);
    }

    private void ParseAdminSpawnRow(Packet p)
    {
        if (p.RemainingBytes < SpawnRowMinBytes) return;
        var row = new AdminSpawnRow { CanPersist = p.ReadByte() == 1, Index = p.ReadInt(), NpcId = p.ReadInt(), Name = p.ReadSByteString() };
        row.Zone = p.ReadByte();
        row.Monster = p.ReadByte() == 1;
        row.X = p.ReadInt();
        row.Z = p.ReadInt();
        row.Y = p.ReadInt() / Coord.TenthsPerUnit;
        row.Direction = p.ReadInt();
        row.Count = p.ReadByte();
        row.RespawnSeconds = p.ReadShort();
        row.SpawnRange = p.ReadShort();
        row.Alive = p.ReadInt();
        admin.RaiseAdminSpawnRow(row);
    }
}
