using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxParticles : GpuParticles3D
{
    private float _age, _start, _life, _hideTime, _showTime;
    private Vector3 _origin, _velocity, _acceleration;

    private Vector3 _emitCentre;
    private Vector3[]? _emitPosKeys, _emitScaleKeys;
    private Quaternion[]? _emitRotKeys;
    private float _emitFps = 30f, _emitPosRate, _emitRotRate, _emitScaleRate, _emitWholeFrame;
    private bool _hasEmitter;

    public void SetEmitter(Vector3 centre, Vector3[]? posKeys, Quaternion[]? rotKeys,
                           Vector3[]? scaleKeys, float fps, float posRate, float rotRate,
                           float scaleRate, float wholeFrame)
    {
        _emitCentre = centre;
        _emitPosKeys = posKeys;
        _emitRotKeys = rotKeys;
        _emitScaleKeys = scaleKeys;
        _emitFps = fps;
        _emitPosRate = posRate;
        _emitRotRate = rotRate;
        _emitScaleRate = scaleRate;
        _emitWholeFrame = wholeFrame;
        _hasEmitter = true;
    }

    private Vector3 EmitterOffset(float t)
    {
        if (!_hasEmitter) return Vector3.Zero;
        var pos = Vector3.Zero;
        var rot = Quaternion.Identity;
        var scale = Vector3.One;
        if (_emitWholeFrame > 0f)
        {
            float frame = Mathf.PosMod(t * _emitFps, _emitWholeFrame);
            if (_emitPosKeys != null) pos = SampleV(_emitPosKeys, frame * _emitPosRate / 30f);
            if (_emitScaleKeys != null) scale = SampleV(_emitScaleKeys, frame * _emitScaleRate / 30f);
            if (_emitRotKeys != null) rot = SampleQ(_emitRotKeys, frame * _emitRotRate / 30f);
        }
        return pos + new Basis(rot) * (scale * _emitCentre);
    }

    private static Vector3 SampleV(Vector3[] k, float kp)
    {
        int i = Mathf.Clamp((int)kp, 0, k.Length - 1);
        int j = Mathf.Min(i + 1, k.Length - 1);
        return k[i].Lerp(k[j], Mathf.Clamp(kp - i, 0f, 1f));
    }

    private static Quaternion SampleQ(Quaternion[] k, float kp)
    {
        int i = Mathf.Clamp((int)kp, 0, k.Length - 1);
        int j = Mathf.Min(i + 1, k.Length - 1);
        return k[i].Slerp(k[j], Mathf.Clamp(kp - i, 0f, 1f));
    }

    public void SetBlink(float hideTime, float showTime)
    {
        _hideTime = hideTime;
        _showTime = showTime;
    }

    public void Configure(float start, float life, Vector3 origin,
        Vector3 velocity, Vector3 acceleration, float fadeIn = 0f, float fadeOut = 0f)
    {
        _start = Mathf.Max(0f, start);
        _life = life > 0.001f ? fadeIn + life : life;
        _origin = origin;
        _velocity = velocity;
        _acceleration = acceleration;
        Position = origin;
        Visible = false;
        Emitting = false;
    }

    public override void _Process(double delta)
    {
        if (Fx.ShuttingDown || IsQueuedForDeletion()) return;
        _age += (float)delta;
        float raw = _age - _start;
        if (raw < 0f) { Visible = false; Emitting = false; return; }

        float t = raw;
        if (_life > 0.001f && raw >= _life)
        {
            Emitting = false;
            if (raw >= _life + Lifetime)
            {
                Visible = false;
                SetProcess(false);
                QueueFree();
            }
            return;
        }
        if (Fx.PartHidden(raw, _hideTime, _showTime)) { Visible = false; return; }
        Visible = true;
        Emitting = true;
        Position = _origin + _velocity * t + 0.5f * _acceleration * t * t + EmitterOffset(t);
    }
}
