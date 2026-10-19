using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GeniePotionsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly HotbarFeature hotbar;
    private readonly PetSession pet;
    private readonly IPetRequests petRequests;
    private readonly PetFeature petWindow;
    private readonly SelfSession self;
    private readonly PotionBarFeature potionBar;
    private readonly PetSkillBarFeature petSkillBar;

    public GeniePotionsFeature(IWorldContext context, HotbarFeature hotbar, PetSession pet, IPetRequests petRequests, PetFeature petWindow, SelfSession self, PotionBarFeature potionBar, PetSkillBarFeature petSkillBar)
    {
        this.context = context;
        this.hotbar = hotbar;
        this.pet = pet;
        this.petRequests = petRequests;
        this.petWindow = petWindow;
        this.self = self;
        this.potionBar = potionBar;
        this.petSkillBar = petSkillBar;
    }

    private IGenieRunner runner = null!;

    internal void AttachRunner(IGenieRunner runner) => this.runner = runner;

    internal void DetachRunner() => runner = null!;

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    internal const int PetTarget = -1;

    private int hpItem;
    private int mpItem;
    private int petItem;
    private Button hpSelect = null!;
    private Button mpSelect = null!;
    private Button petSelect = null!;
    internal PopupMenu PotionPicker { get; private set; } = null!;
    private int potionTarget;

    private static int PotionHealOf(int target) => target == PetTarget ? HealTarget.Hp : target;

    internal void BuildPotionSelector(VBoxContainer parent, int target, CanvasLayer layer)
    {
        var button = new Button { Text = "Automatic (strongest)",
            CustomMinimumSize = new Vector2(210, 30), ExpandIcon = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            FocusMode = Control.FocusModeEnum.None };
        button.AddThemeConstantOverride("icon_max_width", 24);
        parent.AddChild(button);
        if (target == HealTarget.Hp) hpSelect = button;
        else if (target == HealTarget.Mp) mpSelect = button;
        else petSelect = button;
        button.Pressed += () => OpenPotionPicker(target);
        if (PotionPicker != null) return;
        PotionPicker = new PopupMenu();
        layer.AddChild(PotionPicker);
        PotionPicker.IdPressed += id =>
        {
            SetPotionItem(potionTarget, (int)id);
            RefreshPotionSelectors();
        };
    }

    internal void SavePotionSettings(ConfigFile config)
    {
        config.SetValue("genie", "hp_potion_item", hpItem);
        config.SetValue("genie", "mp_potion_item", mpItem);
        config.SetValue("genie", "pet_potion_item", petItem);
    }

    internal void LoadPotionSettings(ConfigFile config)
    {
        hpItem = config.GetValue("genie", "hp_potion_item", 0).AsInt32();
        mpItem = config.GetValue("genie", "mp_potion_item", 0).AsInt32();
        petItem = config.GetValue("genie", "pet_potion_item", 0).AsInt32();
    }

    private int PotionItem(int target) =>
        target == HealTarget.Hp ? hpItem : target == HealTarget.Mp ? mpItem : petItem;

    private void SetPotionItem(int target, int id)
    {
        if (target == HealTarget.Hp) hpItem = id;
        else if (target == HealTarget.Mp) mpItem = id;
        else petItem = id;
        context.Plugins.Genie();
    }

    private Button GeniePotionButton(int target) =>
        target == HealTarget.Hp ? hpSelect : target == HealTarget.Mp ? mpSelect : petSelect;

    internal void OpenPotionPicker(int target)
    {
        potionTarget = target;
        PotionPicker.Clear();
        PotionPicker.AddItem("Automatic (strongest)", 0);
        foreach (int id in PotionChoices(target))
            PotionPicker.AddIconItem(ItemIcons.Get(id), $"{ItemData.DisplayName(id)} ({self.Inventory.CountInBackpack(id)})", id);
        PotionPicker.Position = (Vector2I)GeniePotionButton(target).GetGlobalRect().End;
        PotionPicker.Popup();
    }

    private List<int> PotionChoices(int target)
    {
        var choices = new List<int>();
        var seen = new HashSet<int>();
        int heal = PotionHealOf(target);
        for (int i = InventoryFeature.GridStart; i < self.Inventory.Length; i++)
        {
            if (i >= InventoryConstants.CospreStart && i < InventoryConstants.MagicBagStart) continue;
            int id = self.Inventory[i].ItemId;
            if (id <= 0 || !seen.Add(id) || ItemData.PotionHeal(id, heal) <= 0 || self.Inventory.CountInBackpack(id) <= 0) continue;
            choices.Add(id);
        }
        return choices;
    }

    internal void RefreshPotionSelectors()
    {
        RefreshPotionSelector(hpSelect, hpItem);
        RefreshPotionSelector(mpSelect, mpItem);
        RefreshPotionSelector(petSelect, petItem);
    }

    private void RefreshPotionSelector(Button button, int id)
    {
        button.Icon = id > 0 ? ItemIcons.Get(id) : null;
        button.Text = id > 0 ? $"{ItemData.DisplayName(id)} ({self.Inventory.CountInBackpack(id)})" : "Automatic (strongest)";
        button.TooltipText = id > 0
            ? $"{ItemData.DisplayName(id)}\nUses only this potion. Stops when it runs out."
            : "Automatically chooses the strongest available potion.";
    }

    internal int PotionInUse(int target)
    {
        int selected = PotionItem(target);
        return selected == 0 ? potionBar.BestPotion(PotionHealOf(target)) : selected;
    }

    internal void UsePotion(int target)
    {
        int id = PotionInUse(target);
        if (id <= 0 || self.Inventory.CountInBackpack(id) <= 0 || ItemData.PotionHeal(id, target) <= 0
            || !MaestroPotions.CanUse(id, self.Sheet.Gold)) return;
        hotbar.UseHotItem(id);
    }

    private double feedAt;
    private double petPotionAt;

    internal void FamiliarTick(double now)
    {
        if (pet.Pet is not { } petSheet || petSkillBar.MyPetEntity() == null) return;
        if (GenieRecovery.WantsFeed(runner.Setup.Has(GenieOther.FeedFamiliar), true, petSheet.Satisfaction) && now >= feedAt)
        {
            feedAt = now + GenieRecovery.FeedRetrySeconds;
            int food = petWindow.BestFoodSlot();
            if (food >= 0) petRequests.Feed(food - InventoryFeature.GridStart, self.Inventory[food].ItemId);
        }
        if (!runner.Setup.Has(GenieMoreMode.PetPotion) || petSheet.Hp >= petSheet.MaxHp
            || !GenieRecovery.Below(petSheet.Hp, petSheet.MaxHp, runner.Setup.PetPercent) || now < petPotionAt) return;
        petPotionAt = now + GenieRecovery.PetPotionRetrySeconds;
        int potion = petItem > 0 ? petItem : potionBar.BestPotion(HealTarget.Hp);
        int slot = BagSlotOf(potion);
        if (slot >= 0) petRequests.Feed(slot - InventoryFeature.GridStart, potion);
    }

    private int BagSlotOf(int itemId)
    {
        if (itemId <= 0) return -1;
        for (int abs = InventoryFeature.GridStart; abs < InventoryFeature.GridStart + InventoryFeature.GridCount && abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].ItemId == itemId && self.Inventory[abs].Count > 0 && self.Inventory[abs].State != ItemFlag.Duplicate) return abs;
        return -1;
    }

    internal int PotionStock(int target)
    {
        int selected = target == HealTarget.Hp ? hpItem : mpItem;
        if (selected > 0) return self.Inventory.CountInBackpack(selected);
        int stock = 0;
        var counted = new HashSet<int>();
        for (int abs = InventoryFeature.GridStart; abs < self.Inventory.Length; abs++)
        {
            int id = self.Inventory[abs].ItemId;
            if (id <= 0 || !counted.Add(id) || ItemData.PotionHeal(id, target) <= 0) continue;
            stock += self.Inventory.CountInBackpack(id);
        }
        return stock;
    }
}
