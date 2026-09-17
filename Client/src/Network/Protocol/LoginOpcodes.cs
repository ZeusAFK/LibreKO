namespace LibreKO.Network;

public enum LoginOpcodes : ushort
{
    LS_VERSION_REQ = 0x0001,
    LS_DOWNLOADINFO_REQ = 0x0002,
    LS_LAUNCHER_NEWS = 0x0003,
    LS_CRYPTION = 0x00F2,
    LS_LOGIN = 0x00F3,
    LS_MGAME_LOGIN = 0x00F4,
    LS_SERVERLIST = 0x00F5,
    LS_NEWS = 0x00F6,
    LS_UNKNOWN_F7 = 0x00F7,
    LS_OTP = 0x00FA,
    LS_SOCKET_LIST = 0x00FD,
}
