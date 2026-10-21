namespace LibreKO.Network.Readers;

internal interface IPreGameHost
{
    void RecordVersion(int version);
    void AnnounceVersion();
    void Disconnect(bool expected);
    void ReportError(string message);
    void LeaveToLogin(string message);
    void RetryWhileConnected(double seconds, Action retry);
    bool RefreshesWorld { get; }
    void RebuildWorldInPlace(float x, float z, float y);
    ulong NowMsec { get; }
    void Warn(string message);
}

internal sealed class PreGameReader : IPacketReader
{
    private const string ClientOutOfDateText = "Your client is out of date. Run the launcher to update it.";

    private const string EvictedMessage = "You were disconnected because this account signed in from somewhere else.";
    private const byte KingFlag = 1;
    private const byte CharacterListReady = 1;
    private const byte LoginRejected = 0xFF;
    private const int TextServerFull = 6950;
    private const int SelectRetryLimit = 2;
    private const double SelectRetrySeconds = 0.8;
    private const int DenialHeaderBytes = 6;

    private readonly GameSession session;
    private readonly IPreGameRequests requests;
    private readonly LoginState login;
    private readonly ReconnectSupervisor reconnect;
    private readonly IPreGameHost host;
    private int pendingZone;

    public PreGameReader(GameSession session, IPreGameRequests requests, LoginState login, ReconnectSupervisor reconnect, IPreGameHost host)
    {
        this.session = session;
        this.requests = requests;
        this.login = login;
        this.reconnect = reconnect;
        this.host = host;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_VERSION_CHECK, HandleVersionCheck);
        dispatcher.On(GameOpcodes.GS_LOGIN, HandleLogin);
        dispatcher.On(GameOpcodes.GS_KICKOUT, HandleKickOut);
        dispatcher.On(GameOpcodes.GS_SERVER_FULL, HandleServerFull);
        dispatcher.On(GameOpcodes.GS_NATION_SELECT, HandleNationSelect);
        dispatcher.On(GameOpcodes.GS_CREATE_CHARACTER, HandleCreateCharacter);
        dispatcher.On(GameOpcodes.GS_ALLCHAR_INFO_REQ, HandleCharacterList);
        dispatcher.On(GameOpcodes.GS_SELECT_CHARACTER, HandleSelect);
        dispatcher.On(GameOpcodes.GS_MYINFO, HandleMyInfo);
    }

    private void HandleVersionCheck(Packet p)
    {
        p.ReadByte();
        host.RecordVersion(p.ReadUShort());
        if (p.RemainingBytes >= 2)
        {
            p.ReadByte();
            if ((GameServerState)p.ReadByte() == GameServerState.ClientOutOfDate)
            {
                login.AutoLoginPending = false;
                if (reconnect.State != ReconnectPhase.Idle)
                {
                    reconnect.GiveUp(ClientOutOfDateText);
                    return;
                }
                host.Disconnect(expected: true);
                host.ReportError(ClientOutOfDateText);
                return;
            }
        }

        host.AnnounceVersion();
        if (login.AutoLoginPending)
        {
            login.AutoLoginPending = false;
            requests.Login(login.Account, login.Password);
        }
    }

    private void HandleLogin(Packet p)
    {
        var result = p.ReadByte();
        if (result == LoginRejected && HandleLoginDenied(p)) return;
        session.Self.ApplyNation(result == LoginRejected ? Nations.NotSelected : result);
        if (reconnect.LoggedIn(result != LoginRejected)) return;
        session.PreGame.RaiseLoginResult(result != LoginRejected, session.Self.Nation);
    }

    private bool HandleLoginDenied(Packet p)
    {
        var denial = ReadDenial(p);
        if (denial == GameLoginDenial.TooManyAttempts)
        {
            if (reconnect.InProgress) reconnect.GiveUp(LoginResults.Describe((int)LoginResult.TooManyAttempts));
            else session.PreGame.RaiseLoginThrottled();
            return true;
        }

        if (denial != GameLoginDenial.AccountInUse || ReadAccountInUse(p) is not { } occupant)
            return false;

        if (reconnect.InProgress)
        {
            if (reconnect.KickSent)
                reconnect.GiveUp("Your account has been connected from somewhere else.");
            else
            {
                reconnect.KickSent = true;
                requests.KickOut();
            }
            return true;
        }

        session.AccountLock.RaiseAccountInUse(occupant);
        return true;
    }

    private static GameLoginDenial? ReadDenial(Packet p)
    {
        if (p.RemainingBytes < DenialHeaderBytes
            || p.ReadUInt() != LibreKOProtocol.AccountLockMagic
            || p.ReadByte() != LibreKOProtocol.ExtensionVersion)
            return null;

        return (GameLoginDenial)p.ReadByte();
    }

    private static AccountInUse? ReadAccountInUse(Packet p)
    {
        try
        {
            return new AccountInUse
            {
                Stage = (AccountClaimStage)p.ReadByte(),
                ServerId = p.ReadShort(),
                ServerName = p.ReadString(),
                CharacterName = p.ReadString(),
            };
        }
        catch (Exception e)
        {
            Diag.Report("account-in-use parse", e);
            return null;
        }
    }

    private void HandleKickOut(Packet p)
    {
        var code = p.RemainingBytes > 0 ? (AccountKickCode)p.ReadByte() : AccountKickCode.Evicted;

        if (RemovalMessage(code) is { } message)
        {
            host.LeaveToLogin(message);
            return;
        }

        if (reconnect.KickSent && reconnect.InProgress)
        {
            requests.RetryLogin();
            return;
        }

        session.AccountLock.RaiseKickResult(code);
    }

    private static string? RemovalMessage(AccountKickCode code) => code switch
    {
        AccountKickCode.Evicted => EvictedMessage,
        AccountKickCode.RemovedByGameMaster => "A Game Master removed you from the server.",
        AccountKickCode.Banned => "This account has been banned.",
        _ => null,
    };

    private void HandleServerFull(Packet p) =>
        host.LeaveToLogin(ItemData.Text(TextServerFull, "Server is full now. Please try later. Thank you."));

    private void HandleNationSelect(Packet p)
    {
        int nation = p.ReadByte();
        if (nation != Nations.NotSelected) session.Self.ApplyNation(nation);
        session.PreGame.RaiseNationResult(nation);
    }

    private void HandleCreateCharacter(Packet p)
    {
        if (p.RemainingBytes <= 2)
            session.PreGame.RaiseCreateCharResult((CreateCharacterResult)p.ReadByte());
    }

    private void HandleCharacterList(Packet p)
    {
        var sub = (AllCharacterInfoOpcode)p.ReadByte();
        if (sub == AllCharacterInfoOpcode.NameChange)
        {
            HandleRequiredNameChange(p);
            return;
        }
        if (sub != AllCharacterInfoOpcode.CharacterList || p.ReadByte() != CharacterListReady) return;

        var list = new List<CharacterSummary>();
        for (int slot = 0; slot < CharacterRecordWire.SlotCount; slot++)
        {
            if (p.RemainingBytes < 2) break;
            var character = CharacterRecordWire.Read(p);
            character.Slot = slot;
            list.Add(character);
        }
        session.PreGame.RaiseCharList(list);
    }

    private void HandleRequiredNameChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var result = (SelectingCharacterNameChangeResult)p.ReadByte();
        if (result == SelectingCharacterNameChangeResult.Success && p.RemainingBytes >= 2 + 2)
        {
            int slot = p.ReadShort();
            session.PreGame.RaiseRequiredNameChange(result, slot, p.ReadString());
            return;
        }
        session.PreGame.RaiseRequiredNameChange(result, -1, "");
    }

    private void HandleSelect(Packet p)
    {
        var result = (SelectCharacterResult)p.ReadByte();
        if (result != SelectCharacterResult.Success)
        {
            if (login.SelectRetries < SelectRetryLimit && session.Self.SelectedChar.Length > 0)
            {
                login.SelectRetries++;
                host.RetryWhileConnected(SelectRetrySeconds, requests.ResendSelect);
                return;
            }
            if (reconnect.SelectFailed()) return;
            host.ReportError($"character select failed (result {(byte)result})");
            return;
        }
        pendingZone = p.ReadShort();
        p.ReadShort(); p.ReadShort(); p.ReadShort();
        session.Self.MarkOnline(host.NowMsec);
        session.Self.SetGmCollisionsOff(false);
        session.Admin.ClearGrant();
        session.RoomMedia.ForgetWhispersUnlessOf(session.Self.SelectedChar);
        requests.GameStart(GameStartSubOpcode.CharacterPicked);
    }

    private void HandleMyInfo(Packet p)
    {
        session.Self.ApplyCharacterId(p.ReadInt());
        string name = p.ReadSByteString();
        float x = p.ReadShort() / Coord.TenthsPerUnit;
        float z = p.ReadShort() / Coord.TenthsPerUnit;
        float y = p.ReadShort() / Coord.TenthsPerUnit;

        int authority = 1;
        int knightsRank = NationRankAura.Unranked, personalRank = NationRankAura.Unranked;
        int level = 0, maxHp = 0, hp = 0, maxMp = 0, mp = 0;
        long exp = 0, maxExp = 0;
        int nation = 0, race = 0, cls = 0, face = 0, hair = 0;
        int statPoints = 0, np = 0, monthlyNp = 0, gold = 0, maxWeight = 0;
        int str = 0, sta = 0, dex = 0, intel = 0, magicStat = 0;
        int strB = 0, staB = 0, dexB = 0, intelB = 0, magicB = 0;
        int ap = 0, ac = 0, fr = 0, cr = 0, lr = 0, mr = 0, dr = 0, pr = 0;
        int[] gear = Array.Empty<int>();
        ItemSlot[] inventory = Array.Empty<ItemSlot>();
        byte[] skillPoints = new byte[9];
        int capeId = 0, capeR = 0, capeG = 0, capeB = 0;
        short knightsId = 0; byte clanFame = 0, clanFlag = 0, clanGrade = 0, clanRanking = 0;
        int allianceId = 0, markVersion = 0; string clanName = "";
        bool isKing = false;
        int rebirthLevel = 0, rebStr = 0, rebSta = 0, rebDex = 0, rebInt = 0, rebMag = 0, manner = 0;
        long sealedExp = 0;
        try
        {
            nation = p.ReadByte();
            race = p.ReadByte();
            cls = p.ReadShort();
            face = p.ReadByte();
            hair = p.ReadInt();
            p.ReadByte(); p.ReadByte();
            isKing = p.ReadByte() == KingFlag;
            p.ReadByte();
            level = p.ReadByte();
            statPoints = p.ReadShort();
            maxExp = p.ReadLong();
            exp = p.ReadLong();
            np = p.ReadInt();
            monthlyNp = p.ReadInt();
            knightsId = p.ReadShort();
            clanFame = p.ReadByte();
            if (knightsId > 0)
            {
                allianceId = p.ReadShort();
                clanFlag = p.ReadByte();
                clanName = p.ReadSByteString();
                clanGrade = p.ReadByte(); clanRanking = p.ReadByte();
                markVersion = p.ReadShort();
                capeId = p.ReadShort();
                capeR = p.ReadByte(); capeG = p.ReadByte(); capeB = p.ReadByte();
                p.ReadByte();
            }
            else
            {
                p.ReadLong();
                capeId = p.ReadUShort();
                p.ReadInt();
            }
            p.ReadLong();
            maxHp = p.ReadShort(); hp = p.ReadShort();
            maxMp = p.ReadShort(); mp = p.ReadShort();
            maxWeight = p.ReadInt(); p.ReadInt();
            str = p.ReadByte(); strB = p.ReadByte();
            sta = p.ReadByte(); staB = p.ReadByte();
            dex = p.ReadByte(); dexB = p.ReadByte();
            intel = p.ReadByte(); intelB = p.ReadByte();
            magicStat = p.ReadByte(); magicB = p.ReadByte();
            ap = p.ReadShort(); ac = p.ReadShort();
            fr = p.ReadByte(); cr = p.ReadByte(); lr = p.ReadByte();
            mr = p.ReadByte(); dr = p.ReadByte(); pr = p.ReadByte();
            gold = p.ReadInt();
            authority = p.ReadByte();
            knightsRank = p.ReadByte();
            personalRank = p.ReadByte();
            skillPoints = new byte[9];
            for (int i = 0; i < 9; i++) skillPoints[i] = p.ReadByte();
            gear = new int[InventoryConstants.VisualSlotCount];
            inventory = new ItemSlot[InventoryConstants.InventoryTotal];
            for (int wireIndex = 0; wireIndex < InventoryConstants.MyInfoWireTotal; wireIndex++)
            {
                if (p.RemainingBytes < MyInfoWire.SlotBytes)
                {
                    host.Warn($"[myinfo] inventory block ends at wire slot {wireIndex} of "
                        + $"{InventoryConstants.MyInfoWireTotal} - server is on an older slot layout");
                    break;
                }
                int slot = InventoryConstants.MyInfoWireSlot(wireIndex);
                inventory[slot] = PetWire.ReadItemRecord(p, out var petInfo);
                session.Pet.Remember(petInfo);
                int itemId = inventory[slot].ItemId;
                int vis = Array.IndexOf(InventoryConstants.VisualSlots, slot);
                if (vis >= 0) gear[vis] = itemId;
            }
            MyInfoWire.ReadTail(p, out manner, out rebirthLevel, out rebStr, out rebSta, out rebDex, out rebInt, out rebMag, out sealedExp);
        }
        catch (Exception e)
        {
            host.Warn($"[myinfo] parse failed, GM features disabled: {e.Message}");
            authority = 1;
        }

        bool refresh = host.RefreshesWorld;
        session.Self.ApplyMyInfo(new MyInfo
        {
            CharId = session.Self.MyCharId,
            Name = name,
            X = x,
            Z = z,
            Y = y,
            Zone = refresh ? session.Self.LastEnter.Zone : pendingZone,
            Authority = authority,
            Nation = nation,
            Race = race,
            Class = cls,
            Face = face,
            Hair = hair,
            Gear = gear,
            CapeId = capeId,
            CapeR = capeR,
            CapeG = capeG,
            CapeB = capeB,
            KnightsId = knightsId,
            ClanFame = clanFame,
            AllianceId = allianceId,
            ClanFlag = clanFlag,
            ClanName = clanName,
            ClanGrade = clanGrade,
            ClanRanking = clanRanking,
            MarkVersion = markVersion,
            KnightsRank = knightsRank,
            PersonalRank = personalRank,
            IsKing = isKing,
        }, inventory);
        session.Clan.SeedMyClan(session.Self.LastEnter);
        if (!refresh) session.ResetCharacterState();
        session.Self.SeedState(
            level, exp, maxExp, hp, maxHp, mp, maxMp,
            str, sta, dex, intel, magicStat, statPoints,
            strB, staB, dexB, intelB, magicB,
            ap, ac, gold, np, maxWeight,
            fr, cr, lr, mr, dr, pr, skillPoints);
        session.Self.Sheet.ApplyLoyalty(np, monthlyNp);
        session.Self.Sheet.SetManner(manner);
        session.Self.Sheet.SeedRebirth(rebirthLevel, rebStr, rebSta, rebDex, rebInt, rebMag);
        if (refresh)
        {
            session.ExpSeal.Reset(sealedExp);
            host.RebuildWorldInPlace(x, z, y);
            return;
        }
        session.Entities.Clear();
        session.GmFx.Clear();
        session.UserOption.ResetForWorld();
        session.Zone.ForgetEventTitles();
        session.Siege.ResetForWorld();
        session.Vanguard.Reset();
        session.ExpSeal.Reset(sealedExp);
        if (reconnect.Entered()) return;
        session.PreGame.RaiseEnterWorld(session.Self.LastEnter);
    }
}
