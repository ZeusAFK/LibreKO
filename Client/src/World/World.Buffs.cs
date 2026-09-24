using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private sealed class ActiveBuff
    {
        public int SkillId;
        public double End;
        public bool LongLived;
        public Control Entry = null!;
        public Label Timer = null!;
    }

    private const float BuffChipPointerSize = 32f;
    private const double BuffTickStep = 0.2;
    private const int BuffLongSeconds = 60;
    private const int BuffTooltipWidth = 44;
    private static readonly Vector2 BuffPanelFromCenter = new(-300f, 56f);

    private readonly List<ActiveBuff> _buffs = new();
    private Control? _buffPanel;
    private HBoxContainer _buffShortRow = null!;
    private HBoxContainer _buffLongRow = null!;
    private double _buffNextTick;
    private readonly List<ActiveBuff> _buffOrder = new();
    private readonly List<(int SkillId, double End)> _buffRestore = new();

    private static float BuffChipSize =>
        Platform.TouchUi ? HudPlacement.BuffChipSize : BuffChipPointerSize;

    private void BuildBuffBar()
    {
        var layer = new CanvasLayer { Layer = 64 };
        AddChild(layer);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        layer.AddChild(panel);
        _buffPanel = panel;

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 5);
        panel.AddChild(col);

        _buffShortRow = BuffRow();
        _buffLongRow = BuffRow();
        col.AddChild(_buffShortRow);
        col.AddChild(_buffLongRow);

        if (Platform.TouchUi)
        {
            HudPlacement.Buffs(VitalsSize, TouchHpBarPos.X).ApplyTo(panel);
            panel.GrowHorizontal = Control.GrowDirection.End;
            return;
        }

        HudLayout.Attach(panel, "hud_buffs", panel,
            () =>
            {
                var vp = GetViewport().GetVisibleRect().Size;
                return vp * 0.5f + BuffPanelFromCenter;
            });
    }

    private static HBoxContainer BuffRow()
    {
        var row = new HBoxContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", Platform.TouchUi ? 8 : 4);
        return row;
    }

    private float AttackSpeedMultiplier()
    {
        double now = Now();
        float best = 1f;
        foreach (var (skillId, end) in Net.I.BuffEnds)
        {
            if (end <= now) continue;
            var s = SkillData.Get(skillId);
            if (s == null || s.AttackSpeedPercent == 100) continue;
            best = Mathf.Max(best, s.AttackSpeedPercent / 100f);
        }
        return best;
    }

    private void RegisterBuff(SkillData.Skill s, int targetId, int duration)
    {
        if (_buffPanel == null || targetId != _myId || duration <= 0) return;
        if (s.Type1 is not (MagicType.Buff or MagicType.DotHeal or MagicType.Stealth
            or MagicType.Transform)) return;
        AddBuffChip(s, Now() + duration);
    }

    private void RestoreBuffs()
    {
        if (_buffPanel == null || Net.I.BuffEnds.Count == 0) return;

        double now = Now();
        _buffRestore.Clear();
        foreach (var (skillId, end) in Net.I.BuffEnds) _buffRestore.Add((skillId, end));
        foreach (var (skillId, end) in _buffRestore)
        {
            var s = SkillData.Get(skillId);
            if (s == null || end <= now) { Net.I.BuffEnds.Remove(skillId); continue; }
            AddBuffChip(s, end);
            ApplyMoveSpeedBuff(s, (int)(end - now));
        }
    }

    private void AddBuffChip(SkillData.Skill s, double end)
    {
        Net.I.BuffEnds[s.Id] = end;
        foreach (var b in _buffs)
        {
            if (b.SkillId != s.Id) continue;
            b.End = end;
            return;
        }

        bool harmful = s.IsEnemy || s.MoveSpeedPercent < 100;
        bool longLived = s.Duration >= BuffLongSeconds;

        var entry = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        entry.AddThemeConstantOverride("separation", 1);

        var chip = new PanelContainer
        {
            CustomMinimumSize = new Vector2(BuffChipSize, BuffChipSize),
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = harmful
                ? $"{s.Name}\n{TextWrap.Wrap(s.Desc, BuffTooltipWidth)}"
                : $"{s.Name}\n{TextWrap.Wrap(s.Desc, BuffTooltipWidth)}\nDouble-click to remove.",
        };
        if (!harmful)
        {
            int cancelId = s.Id;
            chip.GuiInput += ev => BuffChipInput(ev, cancelId);
        }
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.85f),
            BorderColor = harmful ? new Color(0.85f, 0.3f, 0.3f) : new Color(0.79f, 0.64f, 0.15f),
        };
        style.SetCornerRadiusAll(Platform.TouchUi ? (int)(BuffChipSize * 0.16f) : 4);
        style.SetBorderWidthAll(Platform.TouchUi ? 2 : 1);
        chip.AddThemeStyleboxOverride("panel", style);
        entry.AddChild(chip);

        if (SkillData.Icon(s.Id) is { } tex)
            chip.AddChild(new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        else
            chip.AddChild(new Label
            {
                Text = s.Name.Length > 0 ? s.Name[..1] : "?",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        var timer = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        timer.AddThemeFontSizeOverride("font_size",
            Platform.TouchUi ? (int)(BuffChipSize * 0.28f) : 10);
        timer.AddThemeColorOverride("font_outline_color", Colors.Black);
        timer.AddThemeConstantOverride("outline_size", 3);
        if (Platform.TouchUi)
        {
            var cell = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var plate = new StyleBoxFlat
            {
                BgColor = new Color(0.022f, 0.025f, 0.032f, 0.90f),
                BorderColor = new Color(UiTheme.Edge, 0.34f),
            };
            plate.SetCornerRadiusAll(4);
            plate.SetBorderWidthAll(1);
            plate.ContentMarginTop = plate.ContentMarginBottom = 0f;
            cell.AddThemeStyleboxOverride("panel", plate);
            cell.AddChild(timer);
            entry.AddChild(cell);
        }
        else
        {
            entry.AddChild(timer);
        }

        (Platform.TouchUi || !longLived ? _buffShortRow : _buffLongRow).AddChild(entry);
        _buffs.Add(new ActiveBuff
        {
            SkillId = s.Id,
            End = end,
            LongLived = longLived,
            Entry = entry,
            Timer = timer,
        });
        ShowBuffRows();
    }

    private void BuffChipInput(InputEvent ev, int skillId)
    {
        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true })
            Net.I.SendMagic(MagicSub.Cancel, skillId, _myId);
    }

    private void OnBuffExpired(int buffType)
    {
        if (_buffPanel == null || buffType == 0) return;

        for (int i = _buffs.Count - 1; i >= 0; i--)
        {
            if (SkillData.Get(_buffs[i].SkillId)?.BuffType != buffType) continue;
            DropBuff(i);
            ShowBuffRows();
            return;
        }
    }

    private void DropBuffChip(int skillId)
    {
        if (skillId <= 0) return;
        for (int i = _buffs.Count - 1; i >= 0; i--)
        {
            if (_buffs[i].SkillId != skillId) continue;
            DropBuff(i);
            ShowBuffRows();
            return;
        }
    }

    private void DropBuff(int index)
    {
        var b = _buffs[index];
        if (GodotObject.IsInstanceValid(b.Entry)) b.Entry.QueueFree();
        _buffs.RemoveAt(index);
        Net.I.BuffEnds.Remove(b.SkillId);
        if (SkillData.Get(b.SkillId) is { } s && s.MoveSpeedPercent != 100) ClearMoveSpeedBuff();
    }

    private void BuffBarTick(double now)
    {
        if (_buffPanel == null || now < _buffNextTick) return;
        _buffNextTick = now + BuffTickStep;

        bool removed = false;
        for (int i = _buffs.Count - 1; i >= 0; i--)
        {
            var b = _buffs[i];
            double left = b.End - now;
            if (left <= 0)
            {
                DropBuff(i);
                removed = true;
                continue;
            }
            b.Timer.Text = left >= 60 ? $"{left / 60:0}M" : $"{left:0}S";
            b.Timer.AddThemeColorOverride("font_color",
                left <= 5 ? new Color(1f, 0.45f, 0.45f) : Colors.White);
        }
        if (removed) ShowBuffRows();
        if (Platform.TouchUi) SortBuffsByRemaining();
    }

    private void SortBuffsByRemaining()
    {
        _buffOrder.Clear();
        foreach (var b in _buffs)
            if (b.Entry.GetParent() == _buffShortRow) _buffOrder.Add(b);
        _buffOrder.Sort((a, b) => a.End.CompareTo(b.End));
        for (int i = 0; i < _buffOrder.Count; i++)
            if (_buffOrder[i].Entry.GetIndex() != i)
                _buffShortRow.MoveChild(_buffOrder[i].Entry, i);
    }

    private void ShowBuffRows()
    {
        int shortLived = 0, longLived = 0;
        foreach (var b in _buffs)
            if (b.LongLived) longLived++;
            else shortLived++;
        _buffShortRow.Visible = Platform.TouchUi ? _buffs.Count > 0 : shortLived > 0;
        _buffLongRow.Visible = !Platform.TouchUi && longLived > 0;
    }

    private void ClearAllBuffs()
    {
        foreach (var b in _buffs)
            if (GodotObject.IsInstanceValid(b.Entry)) b.Entry.QueueFree();
        _buffs.Clear();
        Net.I.BuffEnds.Clear();
        if (_buffPanel != null) ShowBuffRows();
        ClearMoveSpeedBuff();
    }
}
