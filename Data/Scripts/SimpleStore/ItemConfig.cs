using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;

namespace SimpleStore.StoreBlock
{
    class ItemConfig
    {
        private static Random rnd = new Random();
        Dictionary<MyDefinitionId, int> ComponentMinimalPrice = new Dictionary<MyDefinitionId, int>();
        Dictionary<MyDefinitionId, int> BlockMinimalPrice = new Dictionary<MyDefinitionId, int>();
        public class StoreItem
        {
            private int count = 0;
            public int Count
            {
                get { return CalculateCount(); }
                set { count = value; }
            }

            public int Price = 0;
            private int min = 0;
            private int max = 0;
            private bool isRandom = false;
            private int percent = 100;
            private bool isPercent = false;
            private bool isAutoResell = false;
            private int resellCount = 0;

            public void SetAutoResell()
            {
                isAutoResell = true;
            }

            public void SetResellCount(int value)
            {
                resellCount = value;
            }

            private int CalculateCount()
            {
                int value = count;
                if (isAutoResell)
                {
                    value = Math.Max(count, resellCount);
                }

                if (isRandom)
                {
                    int maxValue = max;
                    if (isAutoResell)
                    {
                        maxValue = Math.Max(max, resellCount);
                    }
                    value = rnd.Next(min, maxValue + 1);
                    value = value < 0 ? 0 : value;
                }

                if (isPercent)
                {
                    if (rnd.Next(1, 101) > percent)
                        value = 0;
                }

                return value;
            }

            public override string ToString()
            {
                string countStr = $"{Count}";
                if (isAutoResell)
                    countStr = "A";
                if (isRandom)
                    countStr = $"{min}<{max}";
                if (isRandom && isAutoResell)
                    countStr = $"{min}<A";
                if (isPercent)
                    countStr = $"{percent}%{countStr}";

                return $"{countStr}:{Price}";
            }
            private bool TryParseCount(string strValue)
            {
                this.count = 0;
                isRandom = false;
                isPercent = false;
                isAutoResell = false;
                resellCount = 0;
                max = 0;

                string[] percentValue = strValue.Split('%');
                switch (percentValue.Length)
                {
                    case 1:
                        {
                            break;
                        }
                    case 2:
                        {
                            isPercent = true;
                            strValue = percentValue[1];
                            if (!int.TryParse(percentValue[0], out percent))
                                return false;
                            percent = Math.Max(Math.Min(percent, 100), 0);
                            break;
                        }
                }

                string[] minMax = strValue.Split('<');
                switch (minMax.Length)
                {
                    case 1:
                        {
                            if (minMax[0].ToUpper() == "A")
                            {
                                isAutoResell = true;
                                break;
                            }
                            if (!int.TryParse(minMax[0], out this.count))
                                return false;
                            if (this.count < 0)
                                return false;
                            break;
                        }
                    case 2:
                        {
                            isRandom = true;
                            if (!int.TryParse(minMax[0], out min))
                                return false;

                            if (minMax[1].ToUpper() == "A")
                            {
                                isAutoResell = true;
                                break;
                            }

                            if (!int.TryParse(minMax[1], out max))
                                return false;
                            if (max < min)
                                return false;

                            break;
                        }
                    default:
                        {
                            return false;
                        }
                }


                return true;
            }

            public bool TryParse(string raw)
            {
                string[] countPrice = raw.Split(':');
                if (countPrice.Length != 2)
                    return false;

                if (!TryParseCount(countPrice[0]))
                    return false;

                if (!int.TryParse(countPrice[1], out this.Price))
                    return false;

                return true;
            }
        }

        const string errString = "<Error";

        public StoreItem Buy;
        public StoreItem Sell;
        bool error = false;
        bool removeError = false;
        string raw;

        public ItemConfig()
        {
            Buy = new StoreItem();
            Sell = new StoreItem();
            raw = "";
        }

        public bool TryParse(string raw)
        {
            this.raw = raw;
            this.removeError = false;
            this.error = true;
            string[] buySell = raw.Split(',');
            if (buySell.Length < 2)
                return false;

            if (!this.Buy.TryParse(buySell[0]))
                return false;

            if (!this.Sell.TryParse(buySell[1]))
                return false;

            if (buySell.Length > 2 && buySell[2].Contains(errString))
            {
                this.removeError = true;
            }

            this.error = false;
            return true;
        }

        public override string ToString()
        {
            if (this.error)
            {
                if (this.raw.Contains(errString))
                    return raw;
                return $"{raw},{errString}";
            }
            return $"{Buy},{Sell}";
        }

        public bool IsRemoveError()
        {
            return this.removeError;
        }

        public void SetDefaultPrices(MyDefinitionId definitionId)
        {
            var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definitionId.SubtypeName);

            int minimalPrice = 0;

            if (prefab == null)
            {
                CalculateItemMinimalPrice(definitionId, 1f, ref minimalPrice);
            }
            else
            {
                CalculatePrefabMinimalPrice(prefab.Id.SubtypeName, 1f, ref minimalPrice);
            }

            this.Buy.Price = Math.Max(minimalPrice, 1); // Keen wont allow 0 price
            this.Sell.Price = Math.Max(minimalPrice, 1);
        }


        //Sandbox.Game.World.Generator.MyMinimalPriceCalculator
        //Replaced MySession.Static with MyAPIGateway.Session
        //Removed log meassage
        private void CalculateItemMinimalPrice(MyDefinitionId itemId, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
        {
            minimalPrice = 0;
            MyPhysicalItemDefinition myPhysicalItemDefinition = null;
            if (MyDefinitionManager.Static.TryGetDefinition<MyPhysicalItemDefinition>(itemId, out myPhysicalItemDefinition) && myPhysicalItemDefinition.MinimalPricePerUnit != -1)
            {
                minimalPrice += myPhysicalItemDefinition.MinimalPricePerUnit;
                return;
            }
            MyBlueprintDefinitionBase myBlueprintDefinitionBase = null;
            if (!MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(itemId, out myBlueprintDefinitionBase))
            {
                return;
            }
            float num = myPhysicalItemDefinition.IsIngot ? 1f : MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
            int num2 = 0;
            foreach (MyBlueprintDefinitionBase.Item item in myBlueprintDefinitionBase.Prerequisites)
            {
                int num3 = 0;
                this.CalculateItemMinimalPrice(item.Id, baseCostProductionSpeedMultiplier, ref num3);
                float num4 = (float)item.Amount / num;
                num2 += (int)((float)num3 * num4);
            }
            float num5 = myPhysicalItemDefinition.IsIngot ? MyAPIGateway.Session.RefinerySpeedMultiplier : MyAPIGateway.Session.AssemblerSpeedMultiplier;
            for (int j = 0; j < myBlueprintDefinitionBase.Results.Length; j++)
            {
                MyBlueprintDefinitionBase.Item item2 = myBlueprintDefinitionBase.Results[j];
                if (item2.Id == itemId)
                {
                    float num6 = (float)item2.Amount;
                    if (num6 != 0f)
                    {
                        float num7 = 1f + (float)Math.Log((double)(myBlueprintDefinitionBase.BaseProductionTimeInSeconds + 1f)) * baseCostProductionSpeedMultiplier / num5;
                        minimalPrice += (int)((float)num2 * (1f / num6) * num7);
                        return;
                    }
                }
            }
        }

        //Sandbox.Game.World.Generator.MyMinimalPriceCalculator
        //Derived from CalculatePrefabInformation
        public void CalculatePrefabMinimalPrice(string prefabName, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
        {
            minimalPrice = 0;
            int num = 0;
            MyPrefabDefinition prefabDefinition = MyDefinitionManager.Static.GetPrefabDefinition(prefabName);
            if (prefabDefinition != null && prefabDefinition.CubeGrids != null && prefabDefinition.CubeGrids.Length != 0 && !string.IsNullOrEmpty(prefabDefinition.CubeGrids[0].DisplayName))
            {
                MyObjectBuilder_CubeGrid[] cubeGrids = prefabDefinition.CubeGrids;
                for (int j = 0; j < cubeGrids.Length; j++)
                {
                    foreach (MyObjectBuilder_CubeBlock myObjectBuilder_CubeBlock in cubeGrids[j].CubeBlocks)
                    {
                        MyDefinitionId myDefinitionId = new MyDefinitionId(myObjectBuilder_CubeBlock.TypeId, myObjectBuilder_CubeBlock.SubtypeName);
                        if (!BlockMinimalPrice.TryGetValue(myDefinitionId, out num))
                        {
                            CalculateBlockMinimalPrice(myDefinitionId, baseCostProductionSpeedMultiplier, ref num);
                            BlockMinimalPrice[myDefinitionId] = num;
                        }

                        minimalPrice += num;
                    }
                }
            }
        }

        //Sandbox.Game.World.Generator.MyMinimalPriceCalculator
        //Derived from CalculateBlockMinimalPriceAndPcu
        //Removed pcu calc
        //Changed m_minimalPrices to ComponentMinimalPrice
        private void CalculateBlockMinimalPrice(MyDefinitionId blockId, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
        {
            minimalPrice = 0;
            MyCubeBlockDefinition myCubeBlockDefinition;
            if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(blockId, out myCubeBlockDefinition))
            {
                return;
            }

            foreach (MyCubeBlockDefinition.Component component in myCubeBlockDefinition.Components)
            {
                int num = 0;
                if (!ComponentMinimalPrice.TryGetValue(component.Definition.Id, out num))
                {
                    CalculateItemMinimalPrice(component.Definition.Id, baseCostProductionSpeedMultiplier, ref num);
                    ComponentMinimalPrice[component.Definition.Id] = num;
                }
                minimalPrice += num * component.Count;
            }
        }
    }

}