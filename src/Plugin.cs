using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Reliquary
{
    [BepInPlugin("com.ashley.reliquary", "Reliquary", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static List<KcTable> LoadedCustomTables = new List<KcTable>();
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Reliquary starting...");

            string customTablesDir = Path.Combine(Paths.PluginPath, "Reliquary/tables");
            if (!Directory.Exists(customTablesDir))
            {
                Directory.CreateDirectory(customTablesDir);
                Log.LogInfo("No CustomTables folder found, created one at: " + customTablesDir);
            }

            foreach (string bundleFile in Directory.GetFiles(customTablesDir))
            {
                if (bundleFile.EndsWith(".manifest") || bundleFile.EndsWith(".meta"))
                    continue;

                AssetBundle bundle = AssetBundle.LoadFromFile(bundleFile);
                if (bundle == null)
                {
                    Log.LogWarning("Skipped non-bundle file: " + bundleFile);
                    continue;
                }

                GameObject[] allPrefabs = bundle.LoadAllAssets<GameObject>();
                foreach (var prefab in allPrefabs)
                {
                    KcTable table = prefab.GetComponent<KcTable>();
                    if (table != null)
                    {
                        LoadedCustomTables.Add(table);
                        Log.LogInfo($"Found custom table '{table.TableID}' in {Path.GetFileName(bundleFile)}");
                    }
                }
            }

            Log.LogInfo($"Total custom tables loaded: {LoadedCustomTables.Count}");

            var harmony = new Harmony("com.ashley.reliquary");
            harmony.PatchAll();
            Log.LogInfo("Harmony patches applied.");
        }
    }

    [HarmonyPatch(typeof(KcBonusManager), "Awake")]
    public static class KcBonusManagerPatch
    {
        static void Postfix(KcBonusManager __instance)
        {
            if (Plugin.LoadedCustomTables.Count == 0) return;

            FieldInfo field = typeof(KcBonusManager).GetField("allTables",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var existing = ((KcTable[])field.GetValue(__instance)).ToList();

            foreach (var table in Plugin.LoadedCustomTables)
            {
                if (!existing.Any(t => t.TableID == table.TableID))
                    existing.Add(table);
            }

            field.SetValue(__instance, existing.ToArray());
            Plugin.Log.LogInfo($"allTables patched. New count: {existing.Count}");
        }
    }

    [HarmonyPatch(typeof(KcTableSelectPanel), "Start")]
    public static class KcTableSelectPanelPatch
    {
        static void Postfix(KcTableSelectPanel __instance)
        {
            Plugin.Log.LogInfo("KcTableSelectPanel.Start Postfix firing.");

            if (Plugin.LoadedCustomTables.Count == 0)
            {
                Plugin.Log.LogInfo("No custom tables to add.");
                return;
            }

            try
            {
                FieldInfo listField = typeof(KcTableSelectPanel).GetField("tableOptions",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var list = (List<KcTableSelectOption>)listField.GetValue(__instance);
                Plugin.Log.LogInfo($"Existing tableOptions count: {list.Count}");

                var templateCard = list[0];
                int nextIndex = list.Count + 1;

                foreach (var table in Plugin.LoadedCustomTables)
                {
                    var newCardObj = Object.Instantiate(templateCard.gameObject, templateCard.transform.parent);
                    var newCard = newCardObj.GetComponent<KcTableSelectOption>();

                    var stats = new TableStats
                    {
                        index = nextIndex,
                        tableID = table.TableID,
                        tableName = table.TableName,
                        tableDescription = table.TableDescription,
                        highestStage = KcAchievementManager.Instance.HighestStageWithTable(table.TableID),
                        highestScore = KcAchievementManager.Instance.HighestScoreWithTable(table.TableID),
                        highestDifficulty = (Difficulty)KcAchievementManager.Instance.HighestDifficultyWithTable(table.TableID)
                    };

                    newCard.Setup(nextIndex, stats, isLocked: false, __instance);
                    list.Add(newCard);
                    nextIndex++;
                    Plugin.Log.LogInfo($"Added card for {table.TableID}, new list count: {list.Count}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Exception in KcTableSelectPanelPatch: " + e);
            }
        }
    }
}