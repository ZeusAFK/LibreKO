using System;

namespace LibreKO.Network;

public sealed class LoginConn : FramedConn
{
    private volatile byte[]? _seed;

    public bool CryptoReady => _seed != null;

    protected override void ResetProtocolState() => _seed = null;

    protected override byte[] TransformOutgoing(byte[] body)
    {
        var seed = _seed;
        if (seed != null && body.Length > 0 && body[0] != (byte)LoginOpcodes.LS_CRYPTION)
            return LoginSeedCipher.Protect(body, seed);
        return body;
    }

    protected override Packet? BuildIncoming(byte[] body)
    {
        var seed = _seed;
        if (seed != null && LoginSeedCipher.LooksLikeProtectedPacket(body))
            body = LoginSeedCipher.Unprotect(body, seed);

        if (seed == null && body.Length >= 2 && body[0] == (byte)LoginOpcodes.LS_CRYPTION)
        {
            int keyLen = body[1];
            if (keyLen > 0 && body.Length >= 2 + keyLen)
                _seed = body[2..(2 + keyLen)];
        }

        return BuildPacket(body);
    }
}
