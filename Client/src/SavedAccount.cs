using System;
using System.Security.Cryptography;
using System.Text;
using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public static class SavedAccount
{
    private const string Path = "user://account.cfg";
    private const string Section = "account";
    private const string KeySalt = "gko-account-v1";

    public static string Name { get; private set; } = "";

    private static string _secret = "";

    public static bool Any => Name.Length > 0 && _secret.Length > 0;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        Name = cfg.GetValue(Section, "name", "").AsString();
        _secret = cfg.GetValue(Section, "secret", "").AsString();
        if (Password.Length == 0) Forget();
    }

    public static string Password => _secret.Length == 0 ? "" : Unprotect(_secret);

    public static void Save(string name, string password)
    {
        if (name.Length == 0 || password.Length == 0) { Forget(); return; }
        Name = name;
        _secret = Protect(password);
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "name", Name);
        cfg.SetValue(Section, "secret", _secret);
        if (cfg.Save(Path) != Error.Ok)
            GD.PushWarning($"[account] could not write {Path}");
    }

    internal static void Preview(string name)
    {
        Name = name;
        _secret = name.Length == 0 ? "" : Protect("preview");
    }

    public static void Forget()
    {
        Name = "";
        _secret = "";
        if (FileAccess.FileExists(Path))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
    }

    private static byte[] DeviceKey()
    {
        string id = OS.GetUniqueId();
        if (id.Length == 0) id = OS.GetName();
        return SHA256.HashData(Encoding.UTF8.GetBytes(KeySalt + "|" + id));
    }

    private static string Protect(string plain)
    {
        using var aes = Aes.Create();
        aes.Key = DeviceKey();
        aes.GenerateIV();
        byte[] body = aes.EncryptCbc(Encoding.UTF8.GetBytes(plain), aes.IV);
        byte[] blob = new byte[aes.IV.Length + body.Length];
        Buffer.BlockCopy(aes.IV, 0, blob, 0, aes.IV.Length);
        Buffer.BlockCopy(body, 0, blob, aes.IV.Length, body.Length);
        return Convert.ToBase64String(blob);
    }

    private static string Unprotect(string blob)
    {
        try
        {
            byte[] raw = Convert.FromBase64String(blob);
            using var aes = Aes.Create();
            if (raw.Length <= aes.BlockSize / 8) return "";
            aes.Key = DeviceKey();
            byte[] iv = raw[..(aes.BlockSize / 8)];
            return Encoding.UTF8.GetString(aes.DecryptCbc(raw[(aes.BlockSize / 8)..], iv));
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return "";
        }
    }
}
