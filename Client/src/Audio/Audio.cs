using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Audio : Node
{
    public const string BusMusic = "Music";
    public const string BusSfx = "Sfx";
    public const string BusUi = "Ui";
    public const string BusVoice = "Voice";
    public const string BusMaster = "Master";

    public const float MaxAudible = 60f;
    private const float UnitSize = 7f;
    private const int Pool3D = 48;
    private const int Pool2D = 12;
    private const float BgmFade = 2.0f;
    private static readonly StringName SoundMeta = "snd";

    public static Audio? I { get; private set; }

    private readonly Dictionary<string, AudioStream> streams = new();
    private readonly Dictionary<int, int> live = new();
    private readonly Dictionary<(ulong Owner, SoundOwnerKind Kind), AudioStreamPlayer3D> owned = new();
    private const int OwnedPruneAt = 1024;
    private readonly List<AudioStreamPlayer3D> pool3 = new();
    private readonly List<AudioStreamPlayer> pool2 = new();
    private AudioStreamPlayer bgmA = null!;
    private AudioStreamPlayer bgmB = null!;
    private AudioStreamPlayer ambience = null!;
    private int ambienceId;
    private bool bgmUseA;
    private string bgmFile = "";
    private float fade;
    private Node3D? listener;

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        EnsureBuses();
        SoundCatalog.EnsureLoaded();

        bgmA = NewBgmPlayer("BgmA");
        bgmB = NewBgmPlayer("BgmB");
        ambience = new AudioStreamPlayer { Name = "Ambience", Bus = BusSfx };
        AddChild(ambience);
        ApplyVolumes();
        Config.AudioChanged += ApplyVolumes;
        VoiceChat.Ensure(this, Game.Session.RoomMedia, Game.Session.NationChat, Game.Session.Self, Game.Requests.RoomMedia);
    }

    public override void _ExitTree()
    {
        Config.AudioChanged -= ApplyVolumes;
        if (I == this) I = null;
    }

    public const string QuietMeta = "ui_quiet";

    public static void HookButton(BaseButton b)
    {
        if (b.HasMeta("uiclick")) return;
        b.SetMeta("uiclick", true);
        b.Pressed += () =>
        {
            if (!IsQuiet(b)) PlayUi(b is CheckBox or CheckButton ? Sfx.CheckboxClick : Sfx.UiButton);
        };
    }

    private static bool IsQuiet(Node node)
    {
        for (Node? n = node; n != null; n = n.GetParent())
            if (n.HasMeta(QuietMeta)) return true;
        return false;
    }

    public static void ApplyVolumes()
    {
        SetBus(BusMaster, Config.MasterVolume, Config.AudioMuted || !Config.AudioEnabled);
        SetBus(BusMusic, Config.MusicVolume, false);
        SetBus(BusSfx, Config.SfxVolume, false);
        SetBus(BusUi, Config.UiVolume, false);
        SetBus(BusVoice, Config.VoiceVolume, false);
    }

    public static void PreviewVolume(string bus, float linear) =>
        SetBus(bus, linear, bus == BusMaster && (Config.AudioMuted || !Config.AudioEnabled));

    public static void SetListener(Node3D? node) { if (I != null) I.listener = node; }

    public static void Play(int soundId, Vector3 pos)
    {
        if (Perf.SkipAudio) return;
        if (I == null || !Config.AudioEnabled) return;
        if (!SoundCatalog.TryGet(soundId, out var e)) return;
        if (e.Type == SoundCatalog.Kind.Stream) { Bgm(soundId); return; }
        if (e.Type == SoundCatalog.Kind.TwoD) { I.Play2DInternal(soundId, e, BusSfx); return; }
        I.Play3DInternal(soundId, e, pos, e.Instances);
    }

    private const int SingleCopy = 1;

    public static void PlayOwned(int soundId, Vector3 pos, ulong owner, SoundOwnerKind kind, bool single = false)
    {
        if (Perf.SkipAudio) return;
        if (I == null || !Config.AudioEnabled) return;
        if (!SoundCatalog.TryGet(soundId, out var e)) return;
        if (e.Type != SoundCatalog.Kind.ThreeD) { Play(soundId, pos); return; }
        I.StopOwned(owner, kind);
        if (I.Play3DInternal(soundId, e, pos, single ? SingleCopy : e.Instances) is { } player)
        {
            if (I.owned.Count >= OwnedPruneAt) I.PruneOwned();
            I.owned[(owner, kind)] = player;
        }
    }

    private void StopOwned(ulong owner, SoundOwnerKind kind)
    {
        if (!owned.Remove((owner, kind), out var player) || !GodotObject.IsInstanceValid(player) || !player.Playing) return;
        player.Stop();
        OnFinished(player);
    }

    private void PruneOwned()
    {
        var idle = new List<(ulong, SoundOwnerKind)>();
        foreach (var (key, player) in owned)
            if (!GodotObject.IsInstanceValid(player) || !player.Playing) idle.Add(key);
        foreach (var key in idle) owned.Remove(key);
    }

    public static void PlayUi(int soundId)
    {
        if (I == null || !Config.AudioEnabled) return;
        if (SoundCatalog.TryGet(soundId, out var e)) I.Play2DInternal(soundId, e, BusUi);
    }

    public static void PlayUiFile(string file)
    {
        if (I == null || !Config.AudioEnabled) return;
        I.Play2DFile(file, BusUi);
    }

    public static void PlayAt(int soundId, Node3D? node)
    {
        if (Perf.SkipAudio) return;
        if (node != null && GodotObject.IsInstanceValid(node)) Play(soundId, node.GlobalPosition);
    }

    public static void Bgm(int soundId)
    {
        if (I == null) return;
        if (soundId == 0) { StopBgm(); return; }
        if (SoundCatalog.TryGet(soundId, out var e)) I.StartBgm(e.File);
    }

    public static void BgmFile(string file)
    {
        if (I == null) return;
        I.StartBgm(file);
    }

    public static void StopBgm()
    {
        if (I == null) return;
        I.bgmFile = "";
        I.fade = 0f;
        I.bgmA.Stop();
        I.bgmB.Stop();
    }

    public static void Ambience(int soundId)
    {
        if (I == null || I.ambienceId == soundId) return;
        I.ambienceId = soundId;
        if (soundId == 0) { I.ambience.Stop(); return; }
        if (!SoundCatalog.TryGet(soundId, out var e)) { I.ambience.Stop(); return; }
        var stream = I.Stream(e.File, looping: true);
        if (stream == null) { I.ambience.Stop(); return; }
        I.ambience.Stream = stream;
        I.ambience.VolumeDb = e.GainDb;
        I.ambience.Play();
    }

    public static AudioStreamPlayer3D? Loop(int soundId, Node3D parent)
    {
        if (I == null) return null;
        if (!SoundCatalog.TryGet(soundId, out var e)) return null;
        var stream = I.Stream(e.File, looping: true);
        if (stream == null) return null;
        var p = new AudioStreamPlayer3D
        {
            Stream = stream,
            Bus = BusSfx,
            VolumeDb = e.GainDb,
            MaxDistance = MaxAudible,
            UnitSize = UnitSize,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
        };
        parent.AddChild(p);
        p.Play();
        return p;
    }

    public override void _Process(double delta)
    {
        if (bgmFile.Length == 0 && fade <= 0f) return;
        float dt = (float)delta;
        var rising = bgmUseA ? bgmA : bgmB;
        var falling = bgmUseA ? bgmB : bgmA;

        if (fade < 1f)
        {
            fade = Mathf.Min(1f, fade + dt / BgmFade);
            rising.VolumeDb = Mathf.LinearToDb(fade);
            if (falling.Playing)
            {
                float v = 1f - fade;
                if (v <= 0.001f) falling.Stop();
                else falling.VolumeDb = Mathf.LinearToDb(v);
            }
        }
    }

    private void StartBgm(string file)
    {
        if (file == bgmFile) return;
        var stream = Stream(file, looping: true);
        if (stream == null) return;

        bgmFile = file;
        bgmUseA = !bgmUseA;
        var rising = bgmUseA ? bgmA : bgmB;
        rising.Stream = stream;
        rising.VolumeDb = Mathf.LinearToDb(0.001f);
        rising.Play();
        fade = 0f;
    }

    private AudioStreamPlayer3D? Play3DInternal(int soundId, SoundCatalog.Entry e, Vector3 pos, int instances)
    {
        if (listener != null && GodotObject.IsInstanceValid(listener)
            && listener.GlobalPosition.DistanceSquaredTo(pos) > MaxAudible * MaxAudible)
            return null;
        if (!Reserve(soundId, instances)) return null;
        var p = Take3D();
        if (p == null) { Release(soundId); return null; }
        var stream = Stream(e.File, looping: false);
        if (stream == null) { Release(soundId); return null; }
        p.Stream = stream;
        p.VolumeDb = e.GainDb;
        p.GlobalPosition = pos;
        p.SetMeta(SoundMeta, soundId);
        p.Play();
        return p;
    }

    private void Play2DInternal(int soundId, SoundCatalog.Entry e, string bus)
    {
        if (!Reserve(soundId, e.Instances)) return;
        var p = Take2D();
        if (p == null) { Release(soundId); return; }
        var stream = Stream(e.File, looping: false);
        if (stream == null) { Release(soundId); return; }
        p.Stream = stream;
        p.Bus = bus;
        p.VolumeDb = e.GainDb;
        p.SetMeta(SoundMeta, soundId);
        p.Play();
    }

    private void Play2DFile(string file, string bus)
    {
        var p = Take2D();
        if (p == null) return;
        var stream = Stream(file, looping: false);
        if (stream == null) return;
        p.Stream = stream;
        p.Bus = bus;
        p.VolumeDb = 0f;
        p.Play();
    }

    private bool Reserve(int soundId, int cap)
    {
        live.TryGetValue(soundId, out int n);
        if (n >= cap) n = live[soundId] = PlayingCopies(soundId);
        if (n >= cap) return false;
        live[soundId] = n + 1;
        return true;
    }

    private int PlayingCopies(int soundId)
    {
        int n = 0;
        foreach (var p in pool3)
            if (p.Playing && p.HasMeta(SoundMeta) && p.GetMeta(SoundMeta).AsInt32() == soundId) n++;
        foreach (var p in pool2)
            if (p.Playing && p.HasMeta(SoundMeta) && p.GetMeta(SoundMeta).AsInt32() == soundId) n++;
        return n;
    }

    private void Release(int soundId)
    {
        if (live.TryGetValue(soundId, out int n))
            live[soundId] = Mathf.Max(0, n - 1);
    }

    private void OnFinished(GodotObject player)
    {
        if (player is Node n && n.HasMeta(SoundMeta))
        {
            Release(n.GetMeta(SoundMeta).AsInt32());
            n.RemoveMeta(SoundMeta);
        }
    }

    private AudioStreamPlayer3D? Take3D()
    {
        foreach (var p in pool3)
            if (!p.Playing)
            {
                OnFinished(p);
                return p;
            }
        if (pool3.Count >= Pool3D) return null;
        var np = new AudioStreamPlayer3D
        {
            Bus = BusSfx,
            MaxDistance = MaxAudible,
            UnitSize = UnitSize,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
        };
        AddChild(np);
        np.Finished += () => OnFinished(np);
        pool3.Add(np);
        return np;
    }

    private AudioStreamPlayer? Take2D()
    {
        foreach (var p in pool2)
            if (!p.Playing)
            {
                OnFinished(p);
                return p;
            }
        if (pool2.Count >= Pool2D) return null;
        var np = new AudioStreamPlayer { Bus = BusUi };
        AddChild(np);
        np.Finished += () => OnFinished(np);
        pool2.Add(np);
        return np;
    }

    private AudioStream? Stream(string file, bool looping)
    {
        string key = looping ? file + "#loop" : file;
        if (streams.TryGetValue(key, out var cached)) return cached;

        string path = SoundCatalog.Dir + file;
        AudioStream? s = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
        if (s == null && Godot.FileAccess.FileExists(path)) s = AudioStreamOggVorbis.LoadFromFile(path);
        if (s == null)
        {
            streams[key] = null!;
            GD.PushWarning($"[sound] cannot load {path}");
            return null;
        }
        if (looping)
        {
            s = (AudioStream)s.Duplicate();
            if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        }
        streams[key] = s;
        return s;
    }

    private AudioStreamPlayer NewBgmPlayer(string name)
    {
        var p = new AudioStreamPlayer { Name = name, Bus = BusMusic, VolumeDb = -80f };
        AddChild(p);
        return p;
    }

    private static void EnsureBuses()
    {
        foreach (var name in new[] { BusMusic, BusSfx, BusUi, BusVoice })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            int idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, BusMaster);
        }
    }

    private static void SetBus(string name, float linear, bool mute)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusMute(idx, mute);
        AudioServer.SetBusVolumeDb(idx, linear <= 0.0005f ? -80f : Mathf.LinearToDb(Mathf.Clamp(linear, 0f, 1f)));
    }
}
