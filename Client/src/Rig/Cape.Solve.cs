using System;
using System.Runtime.InteropServices;
using NumericsVector3 = System.Numerics.Vector3;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private void Simulate(float h, Transform3D anchor, float blend)
    {
        var attachment = pinPrevious.InterpolateWith(pinCurrent, blend);
        for (int col = 0; col < gridColumns; col++) stepPins[col] = oldPins[col].Lerp(pins[col], blend);
        BuildTargets(anchor, attachment);
        phase += h;
        anchorVel = (anchor.Origin - stepAnchor.Origin) / h;
        bodyFwd = Flat(anchor.Basis.Z);
        Array.Copy(pos, previous, pos.Length);
        Array.Clear(contact);
        for (int i = 0; i < links.Length; i++) links[i].Lambda = 0;
        for (int i = 0; i < pos.Length; i++)
        {
            if (inverseMass[i] == 0)
            {
                vel[i] = (stepPins[i] - pos[i]) / h;
                pred[i] = stepPins[i];
                continue;
            }
            float row = i / gridColumns / (float)(gridRows - 1);
            float shoulder = Mathf.Pow(1 - row, 3) * 10 + 1.5f;
            vel[i] += (Vector3.Up * Gravity + (targets[i] - pos[i]) * shoulder) * h;
        }
        ApplyAero(WindAt(anchor.Origin), h);
        float damping = Mathf.Exp(-1.4f * h);
        for (int i = gridColumns; i < pos.Length; i++)
            pred[i] = pos[i] + vel[i] * (h * damping);
        PrepareStepColliders(blend, h);
        var predicted = MemoryMarshal.Cast<Vector3, NumericsVector3>(pred.AsSpan());
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            for (int k = 0; k < links.Length; k++)
            {
                int index = (iteration & 1) == 0 ? k : links.Length - 1 - k;
                ref var link = ref links[index];
                var d = predicted[link.B] - predicted[link.A];
                float length = d.Length();
                if (length < 1e-7f) continue;
                float alpha = link.Compliance / (h * h);
                float lambda = (length - link.Length + alpha * link.Lambda)
                    / (inverseMass[link.A] + inverseMass[link.B] + alpha);
                var correction = d * (lambda / length);
                predicted[link.A] += correction * inverseMass[link.A];
                predicted[link.B] -= correction * inverseMass[link.B];
                link.Lambda -= lambda;
            }
            for (int i = gridColumns; i < pos.Length; i++)
            {
                var pin = pred[i % gridColumns];
                var d = pred[i] - pin;
                float limit = tether[i] * 1.025f;
                if (d.LengthSquared() > limit * limit) pred[i] = pin + d.Normalized() * limit;
                var target = targets[i];
                var deviation = pred[i] - target;
                float freedom = (i / gridColumns / (float)(gridRows - 1)) * rigScale
                    * (0.55f + Mathf.Min(anchorVel.Length() * 0.055f, 0.35f));
                if (deviation.LengthSquared() > freedom * freedom)
                    pred[i] = target + deviation.Normalized() * freedom;
            }
            if (iteration == 0 || iteration == Iterations - 1) CollideParticles(iteration == 0);
        }
        for (int i = 0; i < pos.Length; i++)
        {
            var velocity = (pred[i] - pos[i]) / h;
            if (contact[i])
            {
                var relative = velocity - contactVelocity[i];
                var normal = contactNormal[i];
                relative -= normal * Mathf.Min(0, relative.Dot(normal));
                velocity = contactVelocity[i] + relative * (1 - ContactFriction);
            }
            var motion = velocity - anchorVel;
            if (motion.LengthSquared() > 144f) velocity = anchorVel + motion.Normalized() * 12f;
            vel[i] = velocity;
            pos[i] = pred[i];
        }
        stepAnchor = anchor;
    }

    private void BuildTargets(Transform3D anchor, Transform3D attachment)
    {
        for (int row = 0; row < gridRows; row++)
        {
            float weight = Mathf.Pow(1 - row / (gridRows - 1f), 3);
            var frame = anchor.InterpolateWith(attachment, weight);
            for (int col = 0; col < gridColumns; col++)
            {
                int i = row * gridColumns + col;
                targets[i] = frame * rest[i] + (stepPins[col] - attachment * rest[col]) * weight;
            }
        }
    }

    private void ApplyAero(Vector3 air, float h)
    {
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            var cross = (pos[b] - pos[a]).Cross(pos[c] - pos[a]);
            float area2 = cross.Length();
            if (area2 < 1e-9f) continue;
            var normal = cross / area2;
            float speed = ((vel[a] + vel[b] + vel[c]) / 3 - air).Dot(normal);
            var force = normal * (-AeroDrag * area2 * speed * Mathf.Abs(speed) / 6);
            Add(a); Add(b); Add(c);
            void Add(int i)
            {
                if (inverseMass[i] == 0) return;
                var acceleration = force * areaW[i];
                if (acceleration.LengthSquared() > AeroMaxAccel * AeroMaxAccel)
                    acceleration = acceleration.Normalized() * AeroMaxAccel;
                vel[i] += acceleration * h;
            }
        }
    }

    private Vector3 WindAt(Vector3 p)
    {
        float t = (float)phase;
        return new Vector3(Mathf.Sin(t * 1.3f + p.X * 0.2f),
            0.1f * Mathf.Sin(t * 2.1f), Mathf.Cos(t * 0.8f + p.Z * 0.3f)) * GustStrength;
    }

    private Transform3D AttachmentWorld()
        => skel.GlobalTransform * skel.GetBoneGlobalPose(rideBone) * anchorLocal;

    private Transform3D AnchorWorld()
    {
        var posed = AttachmentWorld();
        var heading = Flat(skel.GlobalTransform.Basis * restBasis.Z);
        if (heading == Vector3.Zero) return posed;
        float pitch = Mathf.Clamp(posed.Basis.Z.Normalized().Y, -0.7f, 0.7f);
        var side = Vector3.Up.Cross(heading).Normalized();
        var forward = heading * Mathf.Sqrt(1 - pitch * pitch) + Vector3.Up * pitch;
        return new Transform3D(new Basis(side, forward.Cross(side), forward).Scaled(posed.Basis.Scale), posed.Origin);
    }
}
