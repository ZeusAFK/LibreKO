using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private readonly HashSet<int> _stealthIds = new();
    private readonly HashSet<int> _stealthDetected = new();
    private readonly HashSet<int> _infiltratingIds = new();
    private float _sightRadius;

    private const float StealthSelfAlpha = 0.40f;
    private const float StealthHideAlpha = 0.02f;
    private const float StealthSeenAlpha = 0.25f;
    private const string StealthFx = "stealth_cast";

    private void StealthInit()
    {
        Net.I.StealthHookEvents();
        Net.I.StealthEvent += OnStealth;
        Net.I.SightEvent += OnSight;
    }

    private void StealthDispose()
    {
        Net.I.SightEvent -= OnSight;
        Net.I.StealthEvent -= OnStealth;
        Net.I.StealthUnhookEvents();
    }

    private void OnSight(float radius)
    {
        if (radius <= 0f && _sightRadius <= 0f) return;

        _sightRadius = radius;
        if (radius <= 0f) _stealthDetected.Clear();
        CombatNotice(radius > 0f ? "You can see the hidden." : "The hidden fade from view.");
        foreach (int id in _stealthIds) ApplyStealthFade(id, stealthed: true);
    }

    private void StealthTick(Vector3 selfPos)
    {
        if (_stealthIds.Count == 0) return;

        float radiusSq = _sightRadius * _sightRadius;
        foreach (int id in _stealthIds)
        {
            if (id == _myId || _infiltratingIds.Contains(id)) continue;

            var body = StealthBodyOf(id);
            bool seen = _sightRadius > 0f && body != null
                && body.Position.DistanceSquaredTo(selfPos) <= radiusSq;
            if (seen == _stealthDetected.Contains(id)) continue;

            if (seen) _stealthDetected.Add(id);
            else _stealthDetected.Remove(id);
            ApplyStealthFade(id, stealthed: true);
        }
    }

    private void StealthCancelSelf()
    {
        if (!_stealthIds.Contains(_myId)) return;
        Net.I.SendStealth();
        CombatNotice("Cancelling stealth.");
    }

    private void StealthOnSpawn(int charId, int invisibility)
    {
        SetInfiltrating(charId, invisibility == Net.InvisibilityInfiltration);
        if (!_stealthIds.Add(charId)) return;
        ApplyStealthFade(charId, stealthed: true);
    }

    private void SetInfiltrating(int charId, bool on)
    {
        if (on) _infiltratingIds.Add(charId); else _infiltratingIds.Remove(charId);
        if (charId != _myId && _ents.TryGetValue(charId, out var ent)) ent.Infiltrating = on;
    }

    private void StealthForgetEntity(int charId)
    {
        _stealthIds.Remove(charId);
        _stealthDetected.Remove(charId);
        _infiltratingIds.Remove(charId);
    }

    private void OnStealth(int charId, int value)
    {
        bool on = value != 0;
        if (on)
        {
            SetInfiltrating(charId, value == Net.InvisibilityInfiltration);
            if (!_stealthIds.Add(charId))
            {
                ApplyStealthFade(charId, stealthed: true);
                return;
            }
            ApplyStealthFade(charId, stealthed: true);
            StealthSpawnFx(charId);

            if (charId == _myId)
                CombatNotice("You melt into the shadows.");
            else if (_ents.TryGetValue(charId, out var e) && !string.IsNullOrEmpty(e.Name))
                CombatNotice($"{e.Name} vanishes.");
        }
        else
        {
            SetInfiltrating(charId, false);
            if (!_stealthIds.Remove(charId)) return;
            _stealthDetected.Remove(charId);
            ApplyStealthFade(charId, stealthed: false);

            if (charId == _myId)
                CombatNotice("You step back into the light.");
        }
    }

    private void ApplyStealthFade(int charId, bool stealthed)
    {
        var body = StealthBodyOf(charId);
        if (body == null) return;

        float alpha;
        if (!stealthed) alpha = 1f;
        else if (charId == _myId) alpha = StealthSelfAlpha;
        else if (_infiltratingIds.Contains(charId)) alpha = 0f;
        else alpha = _stealthDetected.Contains(charId) ? StealthSeenAlpha : StealthHideAlpha;

        StealthSetBodyAlpha(body, alpha);
    }

    private void StealthSpawnFx(int charId)
    {
        var body = StealthBodyOf(charId);
        if (body == null) return;
        var node = Fx.Spawn(StealthFx, body, new Vector3(0, 1.0f, 0));
        if (node != null)
        {
            var timer = GetTree().CreateTimer(1.2);
            timer.Timeout += () => { if (GodotObject.IsInstanceValid(node)) node.QueueFree(); };
        }
    }

    private Node3D? StealthBodyOf(int charId)
    {
        if (charId == _myId) return GodotObject.IsInstanceValid(_self) ? _self : null;
        if (_ents.TryGetValue(charId, out var ent) && GodotObject.IsInstanceValid(ent.Body))
            return ent.Body;
        return null;
    }

    private static void StealthSetBodyAlpha(Node3D body, float alpha)
    {
        float transparency = Mathf.Clamp(1f - alpha, 0f, 1f);
        foreach (var node in StealthDescendants(body))
            if (node is GeometryInstance3D gi)
                gi.Transparency = transparency;
    }

    private static IEnumerable<Node> StealthDescendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var d in StealthDescendants(child))
                yield return d;
        }
    }
}
