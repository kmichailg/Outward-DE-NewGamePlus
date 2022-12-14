using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
// using System.Linq;
using System.Reflection;
// using System.Text;
using SideLoader;
using SideLoader.SaveData;

namespace NewGamePlusDE
{
    public class Settings
    {
        public static bool DisableNG = false;
        public static string DisableNG_Name = "DisableNG";

        public static bool DeleteKeys = true;
        public static string DeleteKeys_Name = "DeleteKeys";

        public static bool TransferExalted = false;
        public static string TransferExalted_Name = "TransferExalted";
    }

    public class NewGameExtension : PlayerSaveExtension
    {
        public int LegacyLevel;
        public int[] LegacySkills;

        public NewGameExtension() : base()
        {
            LegacyLevel = 0;
        }

        public override void Save(Character character, bool isWorldHost)
        {
            if (Plugin.SaveData.TryGetValue(character.UID, out NewGameExtension save))
            {
                LegacyLevel = save.LegacyLevel;
                LegacySkills = save.LegacySkills;
            }
        }

        public override void ApplyLoadedSave(Character character, bool isWorldHost)
        {
            if (Plugin.SaveData.ContainsKey(character.UID))
                Plugin.Log.Log(LogLevel.Error, "Save Data already exists for " + character.Name);
            Plugin.SaveData[character.UID] = this;

            Plugin.Log.Log(LogLevel.Message, "Loaded Legacy Level for " + character.Name + ": " + LegacyLevel);
            if (LegacySkills?.Length > 0)
                Plugin.Log.Log(LogLevel.Message, "Loaded LegacySkills for " + character.Name + ": " + LegacySkills.Length);
        }

        public static NewGameExtension CreateExtensionFor(string UID)
        {
            if (!Plugin.SaveData.TryGetValue(UID, out NewGameExtension nge))
            {
                nge = TryLoadExtension<NewGameExtension>(UID);
                if (nge == null)
                    nge = new NewGameExtension();
                Plugin.SaveData[UID] = nge;
            }
            return nge;
        }

        public static int GetLegacyLevelFor(string UID)
        {
            if (Plugin.SaveData.TryGetValue(UID, out NewGameExtension nge))
                return nge.LegacyLevel;

            int ret = 0;
            nge = TryLoadExtension<NewGameExtension>(UID);
            if (nge != null)
            {
                ret = nge.LegacyLevel;
                Plugin.SaveData.Remove(UID);
            }
            return ret;
        }
    }

    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        // Choose a GUID for your project. Change "myname" and "mymod".
        public const string GUID = "com.kmichailg.newgameplusde";
        // Choose a NAME for your project, generally the same as your Assembly Name.
        public const string NAME = "New Game Plus DE";
        // Increment the VERSION when you release a new version of your mod.
        public const string VERSION = "1.0.2";

        // For accessing your BepInEx Logger from outside of this class (eg Plugin.Log.LogMessage("");)
        internal static ManualLogSource Log;

        // If you need settings, define them like so:
        public static ConfigEntry<bool> cfgIsEnabled;
        public static ConfigEntry<bool> cfgIsDeleteKeys;
        public static ConfigEntry<bool> cfgIsTransferExalted;

        public static bool setMaxStats = false;

        const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

        public static Dictionary<string, NewGameExtension> SaveData = new Dictionary<string, NewGameExtension>();
        public static Plugin Instance;
        public static NewGameExtension SaveExt;
        
        // Awake is called when your plugin is created. Use this to set up your mod.
        internal void Awake()
        {
            cfgIsEnabled = Config.Bind("Legacy Character Creation Settings", Settings.DisableNG_Name, false, "Disable Legacy Character Creation");
            cfgIsDeleteKeys = Config.Bind("Legacy Character Creation Settings", Settings.DeleteKeys_Name, true, "Delete Keys on Creation");
            cfgIsTransferExalted = Config.Bind("Settings", Settings.TransferExalted_Name, false, "Transfer Exalted & Life Drain on Creation");

            StatusManager.InitializeEffects();
            SL.OnPacksLoaded += StatusManager.UpdateLevelData;

            // Harmony is for patching methods. If you're not patching anything, you can comment-out or delete this line.
            // new Harmony(GUID).PatchAll();
            // TODO: Use above implementation instead of below, check if no 'harmony' variable is used for subsequent calls.
            var harmony = new Harmony(GUID);
            harmony.PatchAll();

            SettingsChanged();

            Plugin.Log.LogMessage("New Game Plus DE starting...");
        }

        internal void Update()
        {
            // N/A - Not used
        }

        public static void SettingsChanged()
        {
            Settings.DisableNG = (bool) cfgIsEnabled.Value;
            Settings.DeleteKeys = (bool) cfgIsDeleteKeys.Value;
            Settings.TransferExalted = (bool) cfgIsTransferExalted.Value;
        }

        public static void CreateNewCharacter()
        {
            if (itemList != null)
            {
                Log.LogMessage("test before character get");
                Character player = CharacterManager.Instance.GetFirstLocalCharacter();
                Log.LogMessage("test after");
                NewGameExtension player_nge = NewGameExtension.CreateExtensionFor(player.UID);

                // Two Options
                //    Option1: 0.X Character with LegacyLevel set, but no PSE
                if (m_legacy.CharSave.PSave.LegacyLevel > 0)
                    player_nge.LegacyLevel = m_legacy.CharSave.PSave.LegacyLevel + 1;
                //    Option2: 1.0+ Character with PSE
                else
                    player_nge.LegacyLevel = NewGameExtension.GetLegacyLevelFor(m_legacy.CharSave.CharacterUID) + 1;

                Log.Log(LogLevel.Message, "Loading Legacy Gear from " + m_legacy.CharSave.PSave.Name);
                List<int> legacySkills = new List<int>();

                player.Inventory.Pouch.SetSilverCount(m_legacy.CharSave.PSave.Money);

                typeof(CharacterRecipeKnowledge).GetMethod("LoadLearntRecipe", FLAGS).Invoke(player.Inventory.RecipeKnowledge, new object[] { m_legacy.CharSave.PSave.RecipeSaves });

                foreach (BasicSaveData data in itemList) {
                    Item item = ItemManager.Instance.GetItem(data.m_saveIdentifier.ToString());
                    if (item != null)
                    {
                        int loc = data.SyncData.IndexOf("<Hierarchy>");
                        if (loc != -1)
                        {
                            char type = data.SyncData.Substring(loc + 11, 1)[0];
                            if (type == '2')
                            {
                                item.ChangeParent(player.Inventory.Pouch.transform);
                                Equipment clone = (Equipment)ItemManager.Instance.CloneItem(item);
                                clone.OnContainerChangedOwner(player);
                                clone.SetIsntNew();
                                clone.ChangeParent(player.Inventory.Pouch.transform);
                                player.Inventory.EquipItem(clone);
                                clone.ForceStartInit();

                                if (clone.GetType() == typeof(Bag))
                                {
                                    int loc1 = data.SyncData.IndexOf("BagSilver");
                                    if (loc1 != -1)
                                    {
                                        int len1 = data.SyncData.IndexOf(";", loc1) - loc1;
                                        string silver = data.SyncData.Substring(loc1 + 10, len1 - 10);
                                        if (int.TryParse(silver, out int money))
                                            ((Bag)clone).Container.SetSilverCount(money);
                                        else
                                            Log.Log(LogLevel.Error, "Couldn't parse integer: " + silver);
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (BasicSaveData data in itemList) {
                    Item item = ItemManager.Instance.GetItem(data.m_saveIdentifier.ToString());
                    if (item == null) {
                        Log.Log(LogLevel.Error, "Couldn't get Item -- " + data.m_saveIdentifier.ToString());
                    } else if (
                        !(item is Quest) &&
                        (
                            !Settings.DeleteKeys ||
                            !(item.GetType() == typeof(Item)) ||
                            !item.Name.Contains("Key")
                        )
                    ) {
                        int loc = data.SyncData.IndexOf("<Hierarchy>");
                        if (loc != -1) {
                            //Log.Log(LogLevel.Message, "Item: " + item.GetType() + " - " + item.Name + " - " + item.name + " - " + data.SyncData);
                            int len = data.SyncData.IndexOf("<", loc + 11) - (loc + 11);
                            string type = data.SyncData.Substring(loc + 11, len);
                            if (type.StartsWith("1Pouch"))
                            {
                                item.ChangeParent(player.Inventory.Pouch.transform);
                                Item clone = ItemManager.Instance.CloneItem(item);
                                if (item.RemainingAmount != clone.RemainingAmount)
                                    clone.RemainingAmount = item.RemainingAmount;
                                clone.OnContainerChangedOwner(player);
                                clone.SetIsntNew();
                                clone.ChangeParent(player.Inventory.Pouch.transform);
                                clone.ForceStartInit();
                            }
                            else {
                                switch (type[0])
                                {
                                    case '1':
                                        item.OnContainerChangedOwner(player);
                                        item.SetIsntNew();
                                        item.ChangeParent(player.Inventory.Pouch.transform);
                                        player.Inventory.TakeItemToBag(item);
                                        item.ForceStartInit();
                                        break;
                                    case '2':
                                        // Do nothing, cause already equipped
                                        break;
                                    case '3':
                                        if (!(item is Skill))
                                        {
                                            Log.Log(LogLevel.Error, "Can't learn a non-skill: " + item.Name);
                                            break;
                                        }
                                        // Check for Push Kick, Throw Lantern, Fire/Reload, or Dagger Slash (tutorial event will give duplicates)
                                        if (item.ItemID == 8100120 || item.ItemID == 8100010 || item.ItemID == 8100072 || item.ItemID == 8200600)
                                            break;
                                        // Check for Exalted, and remove it or add LifeDrain
                                        if (item.ItemID == 8205999)
                                        {
                                            if (!Settings.TransferExalted)
                                                break;
                                            foreach (BasicSaveData status in m_legacy.CharSave.PSave.StatusList)
                                            {
                                                if (status != null && !string.IsNullOrEmpty(status.SyncData))
                                                {
                                                    string _statusPrefabName = status.Identifier.ToString();
                                                    if (_statusPrefabName.Contains("Life Drain"))
                                                    {
                                                        string[] _splitData = StatusEffect.SplitNetworkData(status.SyncData);
                                                        StatusEffect statusEffectPrefab = ResourcesPrefabManager.Instance.GetStatusEffectPrefab(_statusPrefabName);
                                                        player.StatusEffectMngr.AddStatusEffect(statusEffectPrefab, null, _splitData);
                                                        break;
                                                    }
                                                }
                                            }

                                        }

                                        player.Inventory.TryUnlockSkill((Skill)item);
                                        if (!player.Inventory.LearnedSkill(item))
                                            Log.Log(LogLevel.Error, "Failed to learn skill: " + item.Name);
                                        else
                                            legacySkills.Add(item.ItemID);

                                        break;
                                    default:
                                        Log.Log(LogLevel.Error, "Unknown Item Detected: " + item.Name + " - " + data.SyncData);
                                        break;
                                }
                            }
                        }
                        else
                        {
                            Log.Log(LogLevel.Error, "Hierarchy not found -- " + item.Name + " - " + data.SyncData);
                        }
                    }
                }

                player.ApplyQuicklots(m_legacy.CharSave.PSave);

                PropertyInfo pi_DropBag = typeof(Character).GetProperty("HelpDropBagCount");
                pi_DropBag.SetValue(player, m_legacy.CharSave.PSave.HelpDropBagCount);

                PropertyInfo pi_UseBandage = typeof(Character).GetProperty("HelpUseBandageCount");
                pi_UseBandage.SetValue(player, m_legacy.CharSave.PSave.HelpBandageCount);

                player.TargetingSystem.SetHelpLockCount(m_legacy.CharSave.PSave.HelpLockCount);

                player_nge.LegacySkills = legacySkills.ToArray();
                Log.Log(LogLevel.Message, "Increasing Legacy Level to " + player_nge.LegacyLevel);
                Log.Log(LogLevel.Message, "Giving Character " + legacySkills.Count + " Legacy Skills");

                SaveData[player.UID] = player_nge;

                itemList.Clear();
                itemList = null;
                m_legacy = null;

                setMaxStats = true;
            }
        }

        // Copied from ItemManager
        private static string ItemSavesToString(BasicSaveData[] _itemSaves)
        {
            string text = "";
            for (int i = 0; i < _itemSaves.Length; i++)
            {
                string syncDataFromSaveData = Item.GetSyncDataFromSaveData(_itemSaves[i].SyncData);
                if (!string.IsNullOrEmpty(syncDataFromSaveData))
                {
                    text = text + syncDataFromSaveData + "~";
                }
            }
            return text;
        }

        private static bool CharacterHasLegacySkill(Character character, Skill skill)
        {
            return SaveData.TryGetValue(character.UID, out NewGameExtension nge) && nge.LegacySkills != null && nge.LegacySkills.Contains(skill.ItemID);
        }

        public static SaveInstance m_legacy;
        public static List<BasicSaveData> itemList;

        // To gain access to the legacy character save & to init item load
        [HarmonyPatch(typeof(CharacterCreationPanel), "OnConfirmSave")]
        public class CharacterCreationPanel_OnConfirmSave
        {
            [HarmonyPrefix]
            public static void Prefix(CharacterCreationPanel __instance, ref SaveInstance ___m_legacy)
            {
                if (!Settings.DisableNG && ___m_legacy != null)
                {
                    m_legacy = ___m_legacy;
                    itemList = new List<BasicSaveData>();
                    foreach (BasicSaveData data in ___m_legacy.CharSave.ItemList)
                        itemList.Add(data);

                    ItemManager.Instance.OnReceiveItemSync(ItemSavesToString(itemList.ToArray()), ItemManager.ItemSyncType.Character);
                }
            }
        }

        // To change character creation settings to match Legacy Character
        [HarmonyPatch(typeof(CharacterCreationPanel), "OnLegacySelected", new Type[] { typeof(SaveInstance) })]
        public class CharacterCreationPanel_OnLegacySelected
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterCreationPanel __instance, ref SaveInstance _instance)
            {
                // Character Creation Settings:
                //    Gender
                //    Race
                //    Face Style
                //    Hair Style
                //    Hair Color
                if (!Settings.DisableNG && _instance != null)
                {
                    CharacterVisualData legacy = _instance.CharSave.PSave.VisualData;
                    int legacyLevel = _instance.CharSave.PSave.LegacyLevel;
                    if (legacyLevel == 0)
                        legacyLevel = NewGameExtension.GetLegacyLevelFor(_instance.CharSave.CharacterUID);

                    string legacyName = _instance.CharSave.PSave.Name;
                    string[] splits = legacyName.Split(' ');
                    if (splits[splits.Length - 1] == ToRoman(legacyLevel + 1))
                    {
                        legacyName = legacyName.Substring(0, legacyName.Length - (splits[splits.Length - 1].Length + 1));
                    }

                    __instance.OnCharacterNameEndEdit(legacyName + " " + ToRoman(legacyLevel + 2));

                    __instance.OnSexeChanged((int)legacy.Gender);
                    __instance.OnSkinChanged(legacy.SkinIndex);
                    __instance.OnHeadVariationChanged(legacy.HeadVariationIndex + 1);
                    __instance.OnHairStyleChanged(legacy.HairStyleIndex + 1);
                    __instance.OnHairColorChanged(legacy.HairColorIndex + 1);

                    FieldInfo fi_ccp;
                    CharacterCreationDisplay selector;

                    fi_ccp = typeof(CharacterCreationPanel).GetField("m_sexeSelector", FLAGS);
                    selector = (CharacterCreationDisplay)fi_ccp.GetValue(__instance);
                    selector.SetSelectedIndex((int)legacy.Gender);

                    fi_ccp = typeof(CharacterCreationPanel).GetField("m_skinSelector", FLAGS);
                    selector = (CharacterCreationDisplay)fi_ccp.GetValue(__instance);
                    selector.SetSelectedIndex(legacy.SkinIndex);

                    fi_ccp = typeof(CharacterCreationPanel).GetField("m_faceSelector", FLAGS);
                    selector = (CharacterCreationDisplay)fi_ccp.GetValue(__instance);
                    selector.SetValue(legacy.HeadVariationIndex + 1);

                    fi_ccp = typeof(CharacterCreationPanel).GetField("m_hairStyleSelector", FLAGS);
                    selector = (CharacterCreationDisplay)fi_ccp.GetValue(__instance);
                    selector.SetValue(legacy.HairStyleIndex + 1);

                    fi_ccp = typeof(CharacterCreationPanel).GetField("m_hairColorSelector", FLAGS);
                    selector = (CharacterCreationDisplay)fi_ccp.GetValue(__instance);
                    selector.SetValue(legacy.HairColorIndex + 1);

                    typeof(CharacterCreationPanel).GetMethod("RefreshCharPreview", FLAGS).Invoke(__instance, new object[0]);
                }
            }
        }

        // Shamelessly stolen from https://stackoverflow.com/a/11749642 and modified
        public static string ToRoman(int number)
        {
            if (number >= 500) return "D" + ToRoman(number - 500);
            if (number >= 400) return "CD" + ToRoman(number - 400);
            if (number >= 100) return "C" + ToRoman(number - 100);
            if (number >= 90) return "XC" + ToRoman(number - 90);
            if (number >= 50) return "L" + ToRoman(number - 50);
            if (number >= 40) return "XL" + ToRoman(number - 40);
            if (number >= 10) return "X" + ToRoman(number - 10);
            if (number >= 9) return "IX" + ToRoman(number - 9);
            if (number >= 5) return "V" + ToRoman(number - 5);
            if (number >= 4) return "IV" + ToRoman(number - 4);
            if (number >= 1) return "I" + ToRoman(number - 1);
            return string.Empty;
        }

        // To fix issue with one of the 4 starter skills being in a quickslot when starting new game plus
        //     would cause the message of "skill not in inventory"
        public static void FixQuickSlots(Character player)
        {
            for (int i = 0; i < player.QuickSlotMngr.QuickSlotCount; i++)
            {
                QuickSlot slot = player.QuickSlotMngr.GetQuickSlot(i);
                if (slot.ActiveItem is Skill && !player.Inventory.OwnsItem(slot.ActiveItem.UID))
                {
                    Item skill = player.Inventory.SkillKnowledge.GetItemFromItemID(slot.ActiveItem.ItemID);
                    if (skill != null)
                        slot.SetQuickSlot(skill);
                }
            }
        }

        /*
        [HarmonyPatch(typeof(QuickSlot), "Activate")]
        public class QuickSlot_Activate
        {
            [HarmonyPrefix]
            public static void Prefix(QuickSlot __instance, ref Character ___m_owner)
            {
                //Log.Log(LogLevel.Message, "QuickSlot Activation for " + __instance.ActiveItem.Name + " - " + (__instance.ItemAsSkill == null));
                if (__instance.ActiveItem != null && !__instance.ItemIsSkill && __instance.ActiveItem is Skill)
                {
                    Item skill = ___m_owner.Inventory.SkillKnowledge.GetItemFromItemID(__instance.ActiveItem.ItemID);
                    if (skill != null)
                        __instance.SetQuickSlot(skill);
                    else
                        __instance.Clear();
                }
            }
        }
        */

        // LEGACY METHOD OF LOADING LegacyLevel
        //    Can't remove due to breaking older games
        [HarmonyPatch(typeof(Character), "LoadPlayerSave", new Type[] { typeof(PlayerSaveData) })]
        public class Character_LoadPlayerSave
        {
            [HarmonyPostfix]
            public static void Postfix(ref PlayerSaveData _save)
            {
                int level = _save.LegacyLevel;
                if (level > 0)
                {
                    if (SaveData.TryGetValue(_save.UID, out NewGameExtension nge))
                        nge.LegacyLevel = level;
                    else
                        Log.Log(LogLevel.Error, "Missing NewGameExtension for Loaded Save");
                }
            }
        }

        /*
         * Legacy Method of Saving LegacyLevel
         * 
        [HarmonyPatch(typeof(CharacterSave), "PrepareSave")]
        public class CharacterSave_PrepareSave
        {
            [HarmonyPostfix]
            public static void Postfix(ref CharacterSave __instance)
            {
                if(SaveData.TryGetValue(__instance.CharacterUID,out NewGameExtension nge))
                    __instance.PSave.LegacyLevel = nge.LegacyLevel;
            }
        }
         */

        public static void MarkAllSkillsAsNotNew(Character player)
        {
            foreach (Item item in player.Inventory.SkillKnowledge.GetLearnedItems())
                item.SetIsntNew();
        }

        // To set health to max when loading character in
        [HarmonyPatch(typeof(NetworkLevelLoader), "OnReportLoadingProgress", new Type[] { typeof(float) })]
        public class NetworkLevelLoader_OnReportLoadingProgress
        {
            [HarmonyPrefix]
            public static void Prefix(NetworkLevelLoader __instance, ref float _progress)
            {
                if (_progress > 0.6f)
                    CreateNewCharacter();

                if (_progress >= 1f)
                {
                    Character player = CharacterManager.Instance.GetFirstLocalCharacter();
                    if (setMaxStats)
                    {
                        Log.Log(LogLevel.Message, "Resetting Stats");
                        player.Stats.RestoreAllVitals();
                        setMaxStats = false;
                        FixQuickSlots(player);
                        MarkAllSkillsAsNotNew(player);
                    }
                    // Do special stuff for legacy characters
                    if (SaveData.TryGetValue(player.UID, out NewGameExtension nge) && nge.LegacyLevel > 0)
                    {
                        // Check if LegacySkills is blank or empty
                        if (nge.LegacySkills == null || nge.LegacySkills.Length == 0)
                        {
                            List<int> skills = new List<int>();
                            foreach (Item item in player.Inventory.SkillKnowledge.GetLearnedItems())
                            {
                                if (!skills.Contains(item.ItemID))
                                    skills.Add(item.ItemID);
                            }
                            SaveData[player.UID].LegacySkills = skills.ToArray();
                            Plugin.Log.Log(LogLevel.Info, "Loaded LegacySkills for " + player.Name + ": " + skills.Count);
                        }

                        // Check if debuffs exist, if not apply them
                        if (nge.LegacyLevel > 0)
                        {
                            LevelStatusEffect temp = (LevelStatusEffect)player.StatusEffectMngr.GetStatusEffectOfName("Stretched Thin");
                            if (temp == null)
                                temp = (LevelStatusEffect)player.StatusEffectMngr.AddStatusEffect("Stretched Thin");
                            temp.IncreaseLevel(nge.LegacyLevel - temp.CurrentLevel);
                        }
                    }
                }
            }
        }

        // to allow purchasing of ALL SKILLS!!!
        [HarmonyPatch(typeof(SkillSlot), "IsBlocked", new Type[] { typeof(Character), typeof(bool) })]
        public class SkillSlot_IsBlocked
        {
            [HarmonyPrefix]
            public static bool Prefix(SkillSlot __instance, ref bool __result, Character _character, ref bool _notify)
            {
                // If you gained this skill as a legacy skill, then show it as blocked
                if (Plugin.CharacterHasLegacySkill(_character, __instance.Skill))
                {
                    __result = true;
                    return false;
                }

                if (__instance.SiblingSlot != null && Plugin.CharacterHasLegacySkill(_character, __instance.SiblingSlot.Skill))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
