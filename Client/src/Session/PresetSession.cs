namespace LibreKO.Session;

public sealed class PresetSession
{
    public event Action<PresetResult, PresetStatState>? PresetStatResultEvent;

    internal void RaisePresetStatResult(PresetResult arg1, PresetStatState presetStatState) => PresetStatResultEvent?.Invoke(arg1, presetStatState);

    public event Action<PresetResult, PresetSkillState>? PresetSkillResultEvent;

    internal void RaisePresetSkillResult(PresetResult arg1, PresetSkillState presetSkillState) => PresetSkillResultEvent?.Invoke(arg1, presetSkillState);
}
