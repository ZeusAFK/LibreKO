using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Common.Tests;

public class LoginSeedCipherTests
{
    [Fact]
    public void Unprotect_RecoversTheLoginPayloadFromASeededPacket()
    {
        var seed = Convert.FromHexString("DA2E3C590240BE7C");
        var protectedPayload = Convert.FromHexString("011453C218C00D1C4E85F7D99EDEB7A9F50BDD9F9D6D24819EFD605A98F9FFC718");

        var plaintext = LoginSeedCipher.Unprotect(protectedPayload, seed);

        Convert.ToHexString(plaintext).Should().Be("F3040074657374070041514C505754300000000000");

        var packet = new Packet(plaintext[0]);
        packet.WriteBytes(plaintext[1..]);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)LoginOpcodes.LS_LOGIN);
        packet.ReadString().Should().Be("test");
        packet.ReadString().Should().Be("AQLPWT0");
        packet.ReadByte().Should().Be(0);
        packet.ReadUInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
