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

    public static Audio? I { get; private set; }

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly Dictionary<int, int> _live = new();
    private readonly List<AudioStreamPlayer3D> _pool3 = new();
    private readonly List<AudioStreamPlayer> _pool2 = new();
    private AudioStreamPlayer _bgmA = null!;
    private AudioStreamPlayer _bgmB = null!;
    private AudioStreamPlayer _ambience = null!;
    private int _ambienceId;
    private bool _bgmUseA;
    private string _bgmFile = "";
    private float _fade;
    private Node3D? _listener;

    public string PlayingBgm => _bgmFile;

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        EnsureBuses();
        SoundCatalog.EnsureLoaded();

        _bgmA = NewBgmPlayer("BgmA");
        _bgmB = NewBgmPlayer("BgmB");
        _ambience = new AudioStreamPlayer { Name = "Ambience", Bus = BusSfx };
        AddChild(_ambience);
        ApplyVolumes();
        Config.AudioChanged += ApplyVolumes;
    }

    public override void _ExitTree()
    {
        Config.AudioChanged -= ApplyVolumes;
        if (I == this) I = null;
    }

    public static void HookButton(BaseButton b)
    {
        if (b.HasMeta("uiclick")) return;
        b.SetMeta("uiclick", true);
        b.Pressed += () => PlayUi(b is CheckBox or CheckButton ? Sfx.CheckboxClick : Sfx.UiButton);
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

    public static void SetListener(Node3D? node) { if (I != null) I._listener = node; }

    public static void Play(int soundId, Vector3 pos)
    {
        if (I == null || !Config.AudioEnabled) return;
        if (!SoundCatalog.TryGet(soundId, out var e)) return;
        if (e.Type == SoundCatalog.Kind.Stream) { Bgm(soundId); return; }
        if (e.Type == SoundCatalog.Kind.TwoD) { I.Play2DInternal(soundId, e, BusSfx); return; }
        I.Play3DInternal(soundId, e, pos);
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

    public static void PlayVoice(int soundId)
    {
        if (I == null || !Config.AudioEnabled) return;
        if (SoundCatalog.TryGet(soundId, out var e)) I.Play2DInternal(soundId, e, BusVoice);
    }

    public static void PlayAt(int soundId, Node3D? node)
    {
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
        I._bgmFile = "";
        I._fade = 0f;
        I._bgmA.Stop();
        I._bgmB.Stop();
    }

    public static void Ambience(int soundId)
    {
        if (I == null || I._ambienceId == soundId) return;
        I._ambienceId = soundId;
        if (soundId == 0) { I._ambience.Stop(); return; }
        if (!SoundCatalog.TryGet(soundId, out var e)) { I._ambience.Stop(); return; }
        var stream = I.Stream(e.File, looping: true);
        if (stream == null) { I._ambience.Stop(); return; }
        I._ambience.Stream = stream;
        I._ambience.VolumeDb = e.GainDb;
        I._ambience.Play();
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
        if (_bgmFile.Length == 0 && _fade <= 0f) return;
        float dt = (float)delta;
        var rising = _bgmUseA ? _bgmA : _bgmB;
        var falling = _bgmUseA ? _bgmB : _bgmA;

        if (_fade < 1f)
        {
            _fade = Mathf.Min(1f, _fade + dt / BgmFade);
            rising.VolumeDb = Mathf.LinearToDb(_fade);
            if (falling.Playing)
            {
                float v = 1f - _fade;
                if (v <= 0.001f) falling.Stop();
                else falling.VolumeDb = Mathf.LinearToDb(v);
            }
        }
    }

    private void StartBgm(string file)
    {
        if (file == _bgmFile) return;
        var stream = Stream(file, looping: true);
        if (stream == null) return;

        _bgmFile = file;
        _bgmUseA = !_bgmUseA;
        var rising = _bgmUseA ? _bgmA : _bgmB;
        rising.Stream = stream;
        rising.VolumeDb = Mathf.LinearToDb(0.001f);
        rising.Play();
        _fade = 0f;
    }

    private void Play3DInternal(int soundId, SoundCatalog.Entry e, Vector3 pos)
    {
        if (_listener != null && GodotObject.IsInstanceValid(_listener)
            && _listener.GlobalPosition.DistanceSquaredTo(pos) > MaxAudible * MaxAudible)
            return;
        if (!Reserve(soundId, e.Instances)) return;
        var p = Take3D();
        if (p == null) { Release(soundId); return; }
        var stream = Stream(e.File, looping: false);
        if (stream == null) { Release(soundId); return; }
        p.Stream = stream;
        p.VolumeDb = e.GainDb;
        p.GlobalPosition = pos;
        p.SetMeta("snd", soundId);
        p.Play();
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
        p.SetMeta("snd", soundId);
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
        _live.TryGetValue(soundId, out int n);
        if (n >= cap) return false;
        _live[soundId] = n + 1;
        return true;
    }

    private void Release(int soundId)
    {
        if (_live.TryGetValue(soundId, out int n))
            _live[soundId] = Mathf.Max(0, n - 1);
    }

    private void OnFinished(GodotObject player)
    {
        if (player is Node n && n.HasMeta("snd"))
        {
            Release(n.GetMeta("snd").AsInt32());
            n.RemoveMeta("snd");
        }
    }

    private AudioStreamPlayer3D? Take3D()
    {
        foreach (var p in _pool3)
            if (!p.Playing) return p;
        if (_pool3.Count >= Pool3D) return null;
        var np = new AudioStreamPlayer3D
        {
            Bus = BusSfx,
            MaxDistance = MaxAudible,
            UnitSize = UnitSize,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
        };
        AddChild(np);
        np.Finished += () => OnFinished(np);
        _pool3.Add(np);
        return np;
    }

    private AudioStreamPlayer? Take2D()
    {
        foreach (var p in _pool2)
            if (!p.Playing) return p;
        if (_pool2.Count >= Pool2D) return null;
        var np = new AudioStreamPlayer { Bus = BusUi };
        AddChild(np);
        np.Finished += () => OnFinished(np);
        _pool2.Add(np);
        return np;
    }

    private AudioStream? Stream(string file, bool looping)
    {
        string key = looping ? file + "#loop" : file;
        if (_streams.TryGetValue(key, out var cached)) return cached;

        string path = SoundCatalog.Dir + file;
        AudioStream? s = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
        if (s == null && Godot.FileAccess.FileExists(path)) s = AudioStreamOggVorbis.LoadFromFile(path);
        if (s == null)
        {
            _streams[key] = null!;
            GD.PushWarning($"[sound] cannot load {path}");
            return null;
        }
        if (looping)
        {
            s = (AudioStream)s.Duplicate();
            if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        }
        _streams[key] = s;
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
