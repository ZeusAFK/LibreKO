using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LibreKO.Tests;

public class KeyBindTests
{
    [Fact]
    public void EveryActionIsListedExactlyOnce()
    {
        var listed = KeyBinds.All.Select(e => e.Action).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(Enum.GetValues<KeyAction>().Length, listed.Count);
    }

    [Fact]
    public void DefaultsAreAssignedAndUnique()
    {
        var seen = new Dictionary<KeyChord, KeyAction>();
        foreach (var entry in KeyBinds.All)
        {
            var optional = entry.Action is KeyAction.PotionHp or KeyAction.PotionMp
                or KeyAction.CameraTurn or KeyAction.GameMenu or KeyAction.HotPageNext;
            Assert.Equal(!optional, entry.Default.Assigned);
            if (optional) continue;
            Assert.False(seen.TryGetValue(entry.Default, out var owner),
                $"{entry.Action} and {owner} both default to the same key");
            seen[entry.Default] = entry.Action;
        }
    }

    [Fact]
    public void HotbarActionsAreContiguous()
    {
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(KeyAction.HotSlot1 + i, Enum.Parse<KeyAction>($"HotSlot{i + 1}"));
            Assert.Equal(KeyAction.HotPage1 + i, Enum.Parse<KeyAction>($"HotPage{i + 1}"));
        }
    }
}
