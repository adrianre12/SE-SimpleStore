using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using VRage.Game;

namespace SimpleStore.StoreBlock
{
    class ItemConfig
    {
        public class StoreItem
        {
            public int Count = 0;
            public int Price = 0;

            public override string ToString()
            {
                return $"{Count}:{Price}";
            }

            public bool TryParse(string raw)
            {
                string[] countPrice = raw.Split(':');
                if (countPrice.Length != 2)
                    return false;

                if (!int.TryParse(countPrice[0], out this.Count))
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
                minimalPrice = 0; // CalculatePrefabMinimalPrice(prefab.Id.SubtypeName, 1f,ref minimalPrice);
            }

            this.Buy.Price = minimalPrice;
            this.Sell.Price = minimalPrice;
        }


        //Sandbox.Game.World.Generator.MyMinimalPriceCalculator
        //Replaced MySession.Static with MyAPIGateway.Session
        //Removed log meassage
        private void CalculateItemMinimalPrice(MyDefinitionId itemId, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
        {
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


        /*          public int CalculatePrefabMinimalPrice(string prefabName, float baseCostProductionSpeedMultiplier)
                  {
                      int minimumPrice = 0;
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
                                  }

                                  minimalPrice += num;
                              }
                          }
                      }
                  }*/

        /* private void CalculateBlockMinimalPrice(MyDefinitionId blockId, float baseCostProductionSpeedMultiplier, ref int minimalPrice)
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
         }*/

        /*
           foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var maxAmount = 0;
                var typeId = definition.Id.TypeId.ToString();

                match = Regex.Match(typeId + definition.Id.SubtypeName, @"[\[\]\r\n|=]");

                if (match.Success)
                    continue;

                var currentInvItemAmount = MyVisualScriptLogicProvider.GetEntityInventoryItemAmount(myStoreBlock.Name, definition.Id);
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, definition.Id, currentInvItemAmount);

                if (!config.ContainsSection(typeId) || !config.ContainsKey(typeId, definition.Id.SubtypeName))
                    continue;

                if (!config.Get(typeId, definition.Id.SubtypeName).ToBoolean())
                    continue;

                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName);

                if (definition.Id.TypeId == typeof(MyObjectBuilder_Component) || definition.Id.TypeId == typeof(MyObjectBuilder_Ore)
                    || definition.Id.TypeId == typeof(MyObjectBuilder_Ingot))
                {
                    maxAmount = prefab == null ? config.Get(ConfigSettings, ConfigComponent).ToInt32() : config.Get(ConfigSettings, ConfigShip).ToInt32();
                }
                else if (definition.Id.TypeId == typeof(MyObjectBuilder_AmmoMagazine))
                {
                    maxAmount = config.Get(ConfigSettings, ConfigAmmo).ToInt32();
                }
                else if (definition.Id.TypeId == typeof(MyObjectBuilder_PhysicalGunObject) || definition.Id.TypeId == typeof(MyObjectBuilder_OxygenContainerObject)
                    || definition.Id.TypeId == typeof(MyObjectBuilder_GasContainerObject) || definition.Id.TypeId == typeof(MyObjectBuilder_ConsumableItem))
                {
                    maxAmount = config.Get(ConfigSettings, ConfigCharacter).ToInt32();
                }

                var minimalPrice = 0;
                var result = Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success;
                var orderOrOffer = random.Next(0, 3);

                if (prefab == null)
                {
                    CalculateItemMinimalPrice(definition.Id, 1f, ref minimalPrice);
                }
                else
                {
                    CalculatePrefabMinimalPrice(prefab.Id.SubtypeName, 1f, ref minimalPrice);
                }

                long id;
                MyStoreItemData itemData;

                var itemAmount = random.Next(1, Math.Max(maxAmount + 1, 1));
                var itemPrice = (int)Math.Round(minimalPrice * ((random.Next(5000, 15001) / 100000.0f) + 1.0f));

                if (orderOrOffer == 0)
                {
                    itemData = new MyStoreItemData(definition.Id, itemAmount, itemPrice,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransaction(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                    {
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, itemAmount);
                    }
                }
                else if (prefab == null && orderOrOffer == 2)
                {
                    itemData = new MyStoreItemData(definition.Id, itemAmount, (int)Math.Round(itemPrice * 0.7), null, null);
                    result = myStoreBlock.InsertOrder(itemData, out id);
                }

                if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                {
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: result " + result);
                    break;
                }
            }

            UpdateCounter = 0;
            UpdateShop = false;
         */
    }

}