using System;
using System.Runtime.InteropServices;
using NumericsVector3 = System.Numerics.Vector3;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private void Simulate(float h, Transform3D anchor, float blend)
    {
        var attachment = _pinPrevious.InterpolateWith(_pinCurrent, blend);
        for (int col = 0; col < _nc; col++) _stepPins[col] = _oldPins[col].Lerp(_pins[col], blend);
        BuildTargets(anchor, attachment);
        _phase += h;
        _anchorVel = (anchor.Origin - _stepAnchor.Origin) / h;
        _bodyFwd = Flat(anchor.Basis.Z);
        Array.Copy(_pos, _previous, _pos.Length);
        Array.Clear(_contact);
        for (int i = 0; i < _links.Length; i++) _links[i].Lambda = 0;
        for (int i = 0; i < _pos.Length; i++)
        {
            if (_w[i] == 0)
            {
                _vel[i] = (_stepPins[i] - _pos[i]) / h;
                _pred[i] = _stepPins[i];
                continue;
            }
            float row = i / _nc / (float)(_nr - 1);
            float shoulder = Mathf.Pow(1 - row, 3) * 10 + 1.5f;
            _vel[i] += (Vector3.Up * Gravity + (_targets[i] - _pos[i]) * shoulder) * h;
        }
        ApplyAero(WindAt(anchor.Origin), h);
        float damping = Mathf.Exp(-1.4f * h);
        for (int i = _nc; i < _pos.Length; i++)
            _pred[i] = _pos[i] + _vel[i] * (h * damping);
        PrepareStepColliders(blend, h);
        var predicted = MemoryMarshal.Cast<Vector3, NumericsVector3>(_pred.AsSpan());
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            for (int k = 0; k < _links.Length; k++)
            {
                int index = (iteration & 1) == 0 ? k : _links.Length - 1 - k;
                ref var link = ref _links[index];
                var d = predicted[link.B] - predicted[link.A];
                float length = d.Length();
                if (length < 1e-7f) continue;
                float alpha = link.Compliance / (h * h);
                float lambda = (length - link.Length + alpha * link.Lambda)
                    / (_w[link.A] + _w[link.B] + alpha);
                var correction = d * (lambda / length);
                predicted[link.A] += correction * _w[link.A];
                predicted[link.B] -= correction * _w[link.B];
                link.Lambda -= lambda;
            }
            for (int i = _nc; i < _pos.Length; i++)
            {
                var pin = _pred[i % _nc];
                var d = _pred[i] - pin;
                float limit = _tether[i] * 1.025f;
                if (d.LengthSquared() > limit * limit) _pred[i] = pin + d.Normalized() * limit;
                var target = _targets[i];
                var deviation = _pred[i] - target;
                float freedom = (i / _nc / (float)(_nr - 1)) * _rigScale
                    * (0.55f + Mathf.Min(_anchorVel.Length() * 0.055f, 0.35f));
                if (deviation.LengthSquared() > freedom * freedom)
                    _pred[i] = target + deviation.Normalized() * freedom;
            }
            if (iteration == 0 || iteration == Iterations - 1) CollideParticles(iteration == 0);
        }
        for (int i = 0; i < _pos.Length; i++)
        {
            var velocity = (_pred[i] - _pos[i]) / h;
            if (_contact[i])
            {
                var relative = velocity - _contactVelocity[i];
                var normal = _contactNormal[i];
                relative -= normal * Mathf.Min(0, relative.Dot(normal));
                velocity = _contactVelocity[i] + relative * (1 - ContactFriction);
            }
            var motion = velocity - _anchorVel;
            if (motion.LengthSquared() > 144f) velocity = _anchorVel + motion.Normalized() * 12f;
            _vel[i] = velocity;
            _pos[i] = _pred[i];
        }
        _stepAnchor = anchor;
    }

    private void BuildTargets(Transform3D anchor, Transform3D attachment)
    {
        for (int row = 0; row < _nr; row++)
        {
            float weight = Mathf.Pow(1 - row / (_nr - 1f), 3);
            var frame = anchor.InterpolateWith(attachment, weight);
            for (int col = 0; col < _nc; col++)
            {
                int i = row * _nc + col;
                _targets[i] = frame * _rest[i] + (_stepPins[col] - attachment * _rest[col]) * weight;
            }
        }
    }

    private void ApplyAero(Vector3 air, float h)
    {
        for (int t = 0; t < _idx.Length; t += 3)
        {
            int a = _idx[t], b = _idx[t + 1], c = _idx[t + 2];
            var cross = (_pos[b] - _pos[a]).Cross(_pos[c] - _pos[a]);
            float area2 = cross.Length();
            if (area2 < 1e-9f) continue;
            var normal = cross / area2;
            float speed = ((_vel[a] + _vel[b] + _vel[c]) / 3 - air).Dot(normal);
            var force = normal * (-AeroDrag * area2 * speed * Mathf.Abs(speed) / 6);
            Add(a); Add(b); Add(c);
            void Add(int i)
            {
                if (_w[i] == 0) return;
                var acceleration = force * _areaW[i];
                if (acceleration.LengthSquared() > AeroMaxAccel * AeroMaxAccel)
                    acceleration = acceleration.Normalized() * AeroMaxAccel;
                _vel[i] += acceleration * h;
            }
        }
    }

    private Vector3 WindAt(Vector3 p)
    {
        float t = (float)_phase;
        return new Vector3(Mathf.Sin(t * 1.3f + p.X * 0.2f),
            0.1f * Mathf.Sin(t * 2.1f), Mathf.Cos(t * 0.8f + p.Z * 0.3f)) * GustStrength;
    }

    private Transform3D AttachmentWorld()
        => _skel.GlobalTransform * _skel.GetBoneGlobalPose(_rideBone) * _anchorLocal;

    private Transform3D AnchorWorld()
    {
        var posed = AttachmentWorld();
        var heading = Flat(_skel.GlobalTransform.Basis * _restBasis.Z);
        if (heading == Vector3.Zero) return posed;
        float pitch = Mathf.Clamp(posed.Basis.Z.Normalized().Y, -0.7f, 0.7f);
        var side = Vector3.Up.Cross(heading).Normalized();
        var forward = heading * Mathf.Sqrt(1 - pitch * pitch) + Vector3.Up * pitch;
        return new Transform3D(new Basis(side, forward.Cross(side), forward).Scaled(posed.Basis.Scale), posed.Origin);
    }
}
