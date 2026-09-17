using Godot;

namespace LibreKO;

public partial class World
{
    private enum PlayerMenuAction
    {
        RequestParty = 1,
        Report,
        UserTrade,
        Whisper,
        AddFriend,
        UserInformation,
        Duel,
        EquipmentView,
    }

    private PopupMenu _playerMenu = null!;
    private int _playerMenuId = -1;
    private string _playerMenuName = "";

    private void PlayerMenuInit()
    {
        _playerMenu = new PopupMenu();
        _playerMenu.IdPressed += OnPlayerMenuAction;
        AddChild(_playerMenu);
    }

    private void PlayerMenuDispose()
    {
        if (GodotObject.IsInstanceValid(_playerMenu))
            _playerMenu.IdPressed -= OnPlayerMenuAction;
    }

    private bool TryOpenPlayerMenu(Vector2 mouse)
    {
        var target = PickEntityAt(mouse, ClickPickRadius, out int id, out _);
        if (target == null || target.IsNpc || target.Dead || id == _myId) return false;

        Select(id, target);
        _playerMenuId = id;
        _playerMenuName = target.Name;

        _playerMenu.Clear();
        _playerMenu.AddItem("Request a party", (int)PlayerMenuAction.RequestParty);
        _playerMenu.AddItem("Report", (int)PlayerMenuAction.Report);
        _playerMenu.AddItem("User trade", (int)PlayerMenuAction.UserTrade);
        _playerMenu.AddItem("Whisper", (int)PlayerMenuAction.Whisper);
        _playerMenu.AddItem("Add friend", (int)PlayerMenuAction.AddFriend);
        _playerMenu.AddItem("User Information", (int)PlayerMenuAction.UserInformation);
        _playerMenu.AddItem("Duel", (int)PlayerMenuAction.Duel);
        _playerMenu.AddItem("Equipment View", (int)PlayerMenuAction.EquipmentView);
        _playerMenu.ResetSize();
        _playerMenu.Position = (Vector2I)GetViewport().GetMousePosition();
        _playerMenu.Popup();
        return true;
    }

    private void OnPlayerMenuAction(long actionId)
    {
        if (_playerMenuName.Length == 0) return;
        string name = _playerMenuName;
        int id = _playerMenuId;

        switch ((PlayerMenuAction)actionId)
        {
            case PlayerMenuAction.RequestParty:
                InvitePlayerToParty(name);
                break;

            case PlayerMenuAction.Report:
                OpenReportAgainst(name);
                break;

            case PlayerMenuAction.UserTrade:
                RequestTradeWith(id, name);
                break;

            case PlayerMenuAction.Whisper:
                OpenWhisperWith(name);
                break;

            case PlayerMenuAction.AddFriend:
                Net.I.SendFriendAdd(name);
                break;

            case PlayerMenuAction.UserInformation:
                RequestUserInformation(name);
                break;

            case PlayerMenuAction.Duel:
                ToggleDuel();
                CombatNotice($"Create or join a duel to fight {name}.");
                break;

            case PlayerMenuAction.EquipmentView:
                RequestEquipmentView(name);
                break;
        }
    }

    private void InvitePlayerToParty(string name)
    {
        if (InParty && !AmLeader)
        {
            CombatNotice("Only the party leader can invite.");
            return;
        }
        if (InParty && PartyMembers.Count >= PartyMaxMembers)
        {
            CombatNotice("Your party is full.");
            return;
        }

        if (InParty) Net.I.SendPartyInvite(name);
        else Net.I.SendPartyCreate(name);
        CombatNotice($"Inviting {name} to your party…");
    }

    private void RequestTradeWith(int charId, string name)
    {
        if (_exShown || _exWaiting) { CombatNotice("You are already trading."); return; }
        if (_selfDead) return;

        if (!_ents.TryGetValue(charId, out var e) || e.Dead)
        {
            CombatNotice($"{name} is no longer nearby.");
            return;
        }

        if (e.Body.Position.DistanceTo(_self.Position) > TradeRange)
        {
            CombatNotice($"{name} is too far away to trade.");
            return;
        }

        BeginTradeRequest(charId, name);
    }

    private void OpenReportAgainst(string name)
    {
        if (!_reportShown) ToggleReport();
        _reportTargetEdit.Text = name;
        _reportReasonEdit.GrabFocus();
    }
}
