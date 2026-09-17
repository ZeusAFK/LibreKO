using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private void CosmeticsInit()
    {
        Net.I.HelmetEvent += OnHelmet;
    }

    private void CosmeticsDispose()
    {
        Net.I.HelmetEvent -= OnHelmet;
    }

    private void ToggleHelmet()
    {
        Net.I.SendHelmetToggle(!Net.I.HelmetHidden);
    }

    private void OnHelmet(int charId, bool hidden)
    {
        if (charId == _myId)
        {
            RerenderSelfEquipment();
            return;
        }
        if (!_ents.TryGetValue(charId, out var e) || e.IsNpc) return;
        e.HelmetHidden = hidden;
        RedressEntity(e);
    }
}
