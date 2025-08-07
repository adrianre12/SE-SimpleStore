using Sandbox.Definitions;
using System.Collections.Generic;
using VRage.Utils;

namespace SimpleStore.StoreBlock
{
    internal class RefineOre
    {
        private static Dictionary<string, MyBlueprintDefinitionBase> oreToIngots = null;

        static RefineOre()
        {
            oreToIngots = new Dictionary<string, MyBlueprintDefinitionBase>();
            MyBlueprintClassDefinition ingotBpClass = MyDefinitionManager.Static.GetBlueprintClass("Ingots");
            foreach (var bpc in ingotBpClass)
            {

                if (bpc.Prerequisites.Length == 0)
                {
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock.RefineOre: {bpc.Id.SubtypeName} no prerequisites");
                    continue;
                }

                if (oreToIngots.ContainsKey(bpc.Prerequisites[0].Id.SubtypeName))
                    continue;

                oreToIngots.Add(bpc.Prerequisites[0].Id.SubtypeName, bpc);
            }
        }

        public static int OresLoaded()
        {
            return oreToIngots.Count;
        }

        public static bool TryGetIngots(string oreName, out int amount, out MyBlueprintDefinitionBase.Item[] ingots)
        {
            amount = 0;
            ingots = null;
            MyBlueprintDefinitionBase bpc;
            if (!oreToIngots.TryGetValue(oreName, out bpc))
            {
                return false;
            }
            amount = bpc.Prerequisites[0].Amount.ToIntSafe();
            ingots = bpc.Results;
            return true;
        }
    }
}
