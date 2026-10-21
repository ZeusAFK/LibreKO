namespace LibreKO.Network.Readers;

internal sealed class BountyReader : IPacketReader
{
    private readonly BountySession bounty;

    public BountyReader(BountySession bounty)
    {
        this.bounty = bounty;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_BOUNTY, HandleBounty);
    }

    private void HandleBounty(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (BountySubOpcode)p.ReadByte();
        if (sub == BountySubOpcode.List)
        {
            var list = new List<BountyEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
            {
                int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                string target = p.ReadSByteString();
                string poster = p.ReadSByteString();
                int reward = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                var standing = p.RemainingBytes >= 1 ? (BountyStanding)p.ReadByte() : BountyStanding.Open;
                list.Add(new BountyEntry { Id = id, Target = target, Poster = poster, Reward = reward, Standing = standing });
            }
            bounty.RaiseBountyList(list);
        }
        else if (sub == BountySubOpcode.Post)
        {
            bool ok = p.RemainingBytes >= 1 && (BountyResult)p.ReadByte() == BountyResult.Succeeded;
            bounty.RaiseBountyPost(ok);
        }
        else if (sub == BountySubOpcode.Claim)
        {
            bool ok = p.RemainingBytes >= 1 && (BountyResult)p.ReadByte() == BountyResult.Succeeded;
            int bountyId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            bounty.RaiseBountyClaim(new BountyOutcome(bountyId, ok));
        }
        else if (sub == BountySubOpcode.Withdraw)
        {
            bool ok = p.RemainingBytes >= 1 && (BountyResult)p.ReadByte() == BountyResult.Succeeded;
            int bountyId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            bounty.RaiseBountyWithdraw(new BountyOutcome(bountyId, ok));
        }
    }
}
