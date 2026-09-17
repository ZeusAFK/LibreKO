using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    private const byte KingElection = 1, KingImpeachment = 2, KingTax = 3, KingEvent = 4, KingNpc = 5, KingNationIntro = 6;
    private const byte KingElSchedule = 1, KingElNominate = 2, KingElNoticeBoard = 3, KingElPoll = 4, KingElResign = 5;
    private const byte KingBoardWrite = 1, KingBoardRead = 2;
    private const byte KingImRequest = 1, KingImRequestElect = 2, KingImList = 3, KingImElect = 4,
        KingImRequestUiOpen = 8, KingImElectionUiOpen = 9;

    public struct KingCandidate
    {
        public string Name;
        public string Clan;
    }

    public event Action<bool, int, int, int, int>? KingScheduleEvent;
    public event Action<int>? KingNominateEvent;
    public event Action<List<KingCandidate>>? KingPollListEvent;
    public event Action<int>? KingVoteEvent;
    public event Action<int>? KingResignEvent;
    public event Action<List<string>, string>? KingBoardEvent;
    public event Action<int, int, string>? KingImpeachmentEvent;
    public event Action<string>? KingNpcEvent;
    public event Action<string, int, int>? KingNationIntroEvent;
    public event Action<int, int, bool, int>? KingGovernanceEvent;

    private void HandleKing(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte main = p.ReadByte();
        switch (main)
        {
            case KingElection:      HandleKingElection(p); break;
            case KingImpeachment:   HandleKingImpeachment(p); break;
            case KingTax:           HandleKingTax(p); break;
            case KingEvent:         HandleKingEvent(p); break;
            case KingNpc:
                KingNpcEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadSByteString().Trim() : "");
                break;
            case KingNationIntro:
            {
                if (p.RemainingBytes >= 1) p.ReadByte();
                string kingName = p.RemainingBytes >= 1 ? p.ReadSByteString().Trim() : "";
                int treasury = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int tariff = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                KingNationIntroEvent?.Invoke(kingName, treasury, tariff);
                break;
            }
        }
    }

    private void HandleKingElection(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte op = p.ReadByte();
        switch (op)
        {
            case KingElSchedule:
            {
                byte active = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (active == 1 && p.RemainingBytes >= 4)
                {
                    int month = p.ReadByte(), day = p.ReadByte(), hour = p.ReadByte(), minute = p.ReadByte();
                    KingScheduleEvent?.Invoke(true, month, day, hour, minute);
                }
                else KingScheduleEvent?.Invoke(false, 0, 0, 0, 0);
                break;
            }
            case KingElNominate:
                KingNominateEvent?.Invoke(p.RemainingBytes >= 2 ? p.ReadShort() : 0);
                break;
            case KingElResign:
                KingResignEvent?.Invoke(p.RemainingBytes >= 2 ? p.ReadShort() : 0);
                break;
            case KingElNoticeBoard:
                HandleKingNoticeBoard(p);
                break;
            case KingElPoll:
                HandleKingPoll(p);
                break;
        }
    }

    private void HandleKingNoticeBoard(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte boardOp = p.ReadByte();
        if (boardOp == KingBoardWrite)
        {
            short res = p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;
            KingBoardEvent?.Invoke(new List<string>(), res == 1 ? "__write_ok__" : "__write_fail__");
            return;
        }
        if (boardOp != KingBoardRead) return;

        if (p.RemainingBytes < 1) return;
        byte readSub = p.ReadByte();
        if (readSub == 1)
        {
            var names = new List<string>();
            int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
                names.Add(p.ReadSByteString());
            KingBoardEvent?.Invoke(names, "");
        }
        else if (readSub == 2)
        {
            short len = p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;
            string notice = "";
            if (len > 0 && p.RemainingBytes >= len)
                notice = System.Text.Encoding.ASCII.GetString(p.ReadBytes(len));
            KingBoardEvent?.Invoke(new List<string>(), notice);
        }
    }

    private void HandleKingPoll(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte pollOp = p.ReadByte();
        if (pollOp == 1)
        {
            short ok = p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;
            var list = new List<KingCandidate>();
            if (ok == 1 && p.RemainingBytes >= 1)
            {
                int count = p.ReadByte();
                for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
                {
                    string name = p.ReadSByteString();
                    string clan = p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
                    list.Add(new KingCandidate { Name = name, Clan = clan });
                }
            }
            KingPollListEvent?.Invoke(list);
        }
        else if (pollOp == 2)
        {
            KingVoteEvent?.Invoke(p.RemainingBytes >= 2 ? p.ReadShort() : 0);
        }
    }

    private void HandleKingImpeachment(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte op = p.ReadByte();
        if (op == KingImList)
        {
            short res = p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;
            string kingName = res == 1 && p.RemainingBytes >= 2 ? p.ReadString() : "";
            KingImpeachmentEvent?.Invoke(op, res, kingName);
        }
        else
        {
            KingImpeachmentEvent?.Invoke(op, p.RemainingBytes >= 2 ? p.ReadShort() : 0, "");
        }
    }

    private void HandleKingTax(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte op = p.ReadByte();
        bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
        int value = 0;
        if (ok)
        {
            switch (op)
            {
                case 2: value = p.RemainingBytes >= 4 ? p.ReadInt() : 0; break;
                case 3: value = p.RemainingBytes >= 1 ? p.ReadByte() : 0; break;
                case 4: value = p.RemainingBytes >= 1 ? p.ReadByte() : 0; break;
            }
        }
        KingGovernanceEvent?.Invoke(KingTax, op, ok, value);
    }

    private void HandleKingEvent(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte op = p.ReadByte();
        bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
        int value = ok && p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        KingGovernanceEvent?.Invoke(KingEvent, op, ok, value);
    }

    public void SendKingSchedule() => SendKingElection(KingElSchedule);

    public void SendKingPollList()
    {
        var p = NewKing(KingElection);
        p.WriteByte(KingElPoll);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendKingVote(string candidateName)
    {
        var p = NewKing(KingElection);
        p.WriteByte(KingElPoll);
        p.WriteByte(2);
        p.WriteSByteString(candidateName);
        _conn.Send(p);
    }

    public void SendKingNominate(string nomineeName)
    {
        var p = NewKing(KingElection);
        p.WriteByte(KingElNominate);
        p.WriteSByteString(nomineeName);
        _conn.Send(p);
    }

    public void SendKingResign() => SendKingElection(KingElResign);

    public void SendKingBoardList()
    {
        var p = NewKing(KingElection);
        p.WriteByte(KingElNoticeBoard);
        p.WriteByte(KingBoardRead);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendKingBoardRead(string candidateName)
    {
        var p = NewKing(KingElection);
        p.WriteByte(KingElNoticeBoard);
        p.WriteByte(KingBoardRead);
        p.WriteByte(2);
        p.WriteSByteString(candidateName);
        _conn.Send(p);
    }

    public void SendKingBoardWrite(string notice)
    {
        var p = NewKing(KingElection);
        p.WriteByte(KingElNoticeBoard);
        p.WriteByte(KingBoardWrite);
        p.WriteSByteString(notice);
        _conn.Send(p);
    }

    public void SendKingNpc() => SendKingMain(KingNpc);

    public void SendKingNationIntro() => SendKingMain(KingNationIntro);

    public void SendKingCollectTax() => SendKingTax(2);

    public void SendKingTariffRead() => SendKingTax(3);

    public void SendKingSetTariff(int tariff)
    {
        var p = NewKing(KingTax);
        p.WriteByte(4);
        p.WriteByte((byte)tariff);
        _conn.Send(p);
    }

    public void SendKingImpeachmentUiOpen(bool electionStage)
    {
        var p = NewKing(KingImpeachment);
        p.WriteByte(electionStage ? KingImElectionUiOpen : KingImRequestUiOpen);
        _conn.Send(p);
    }

    public void SendKingImpeachmentRequest()
    {
        var p = NewKing(KingImpeachment);
        p.WriteByte(KingImRequest);
        _conn.Send(p);
    }

    public void SendKingImpeachmentVote(bool electionStage, bool yes)
    {
        var p = NewKing(KingImpeachment);
        p.WriteByte(electionStage ? KingImElect : KingImRequestElect);
        p.WriteByte((byte)(yes ? 1 : 0));
        _conn.Send(p);
    }

    public void SendKingImpeachmentList()
    {
        var p = NewKing(KingImpeachment);
        p.WriteByte(KingImList);
        _conn.Send(p);
    }

    private void SendKingElection(byte electionOp)
    {
        var p = NewKing(KingElection);
        p.WriteByte(electionOp);
        _conn.Send(p);
    }

    private void SendKingTax(byte taxOp)
    {
        var p = NewKing(KingTax);
        p.WriteByte(taxOp);
        _conn.Send(p);
    }

    private void SendKingMain(byte main)
    {
        var p = NewKing(main);
        _conn.Send(p);
    }

    private static Packet NewKing(byte main)
    {
        var p = new Packet(GameOpcodes.GS_KING);
        p.WriteByte(main);
        return p;
    }
}
