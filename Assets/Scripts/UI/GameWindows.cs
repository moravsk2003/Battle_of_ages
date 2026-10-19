namespace BattleOfAges.UI
{
    /// <summary>Which game windows are open, in one place (gameplay input stops while any of them is up).</summary>
    public static class GameWindows
    {
        public static bool InventoryOpen => InventoryUI.Instance != null && InventoryUI.Instance.IsOpen;
        public static bool WorkbenchOpen => WorkbenchUI.Instance != null && WorkbenchUI.Instance.IsOpen;
        public static bool WorkersOpen => NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen;
        public static bool BuildMenuOpen => BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen;
        public static bool EquipmentOpen => EquipmentUI.IsVisible;
        public static bool SaveSlotsOpen => SaveSlotsUI.IsVisible;
        public static bool HeroOpen => HeroUI.IsVisible;
        public static bool Paused => PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused();

        /// <summary>A window that takes the mouse and keyboard (not the pause menu itself).</summary>
        public static bool AnyModalOpen =>
            InventoryOpen || WorkbenchOpen || WorkersOpen || BuildMenuOpen || EquipmentOpen || SaveSlotsOpen || HeroOpen;

        /// <summary>The player character should not move, look around or act.</summary>
        public static bool BlockGameplay => AnyModalOpen || Paused;

        public static void CloseAllModals()
        {
            if (InventoryOpen) InventoryUI.Instance.CloseInventory();
            if (WorkersOpen) NpcGatherCommandUI.Instance.CloseMenu();
            if (BuildMenuOpen) BuildMenuUI.Instance.CloseMenu();
            if (WorkbenchOpen) WorkbenchUI.Instance.CloseUI();
            if (EquipmentOpen) EquipmentUI.Instance.Close();
            if (SaveSlotsOpen) SaveSlotsUI.Close();
            if (HeroOpen) HeroUI.Instance.Close();
        }
    }
}
