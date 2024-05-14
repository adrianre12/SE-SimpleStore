using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace SimpleStore.StoreBlock
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_StoreBlock), false, "SimpleStoreBlock")]
    public class IngameStoreBlockGameLogic : MyGameLogicComponent
    {
        const string ConfigSettings = "Settings";
        const string Resell = "ResellItems";
        const string SpawnDistance = "SpawnDistance";
        const string RefreshPeriod = "RefreshPeriodMins";

        const int DefaultSpawnDistance = 100;
        const int DefaultRefreshPeriod = 20; //mins
        const int MinRefreshPeriod = 750;  // 100's of ticks mins * 37.5

        List<string> BlacklistItems = new List<string> { "RestrictedConstruction", "CubePlacerItem", "GoodAIRewardPunishmentTool" };

        IMyStoreBlock myStoreBlock;
        MyIni config = new MyIni();

        bool lastBlockEnabledState = false;
        bool UpdateShop = true;
        int UpdateCounter = 0;
        bool resellItems = false;
        int spawnDistance = DefaultSpawnDistance;
        int refreshCounterLimit = DefaultRefreshPeriod;

        List<IMyPlayer> Players = new List<IMyPlayer>();
        List<Sandbox.ModAPI.Ingame.MyStoreQueryItem> StoreItems = new List<Sandbox.ModAPI.Ingame.MyStoreQueryItem>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;

            myStoreBlock = Entity as IMyStoreBlock;

            if (!MyAPIGateway.Session.IsServer)
                return;

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: loaded...");
        }

        public override void UpdateAfterSimulation100()
        {
            if (myStoreBlock.CustomData == "")
                CreateConfig();

            if (!MyAPIGateway.Session.IsServer)
                return;

            if (myStoreBlock.Enabled != lastBlockEnabledState)
            {
                lastBlockEnabledState = myStoreBlock.Enabled;

                if (!TryLoadConfig())
                    return;

                UpdateShop = true;
            }

            if (!myStoreBlock.IsWorking)
                return;

            UpdateCounter++;

            MyLog.Default.WriteLine($"SimpleStore.StoreBlock: UpdateCounter {UpdateCounter}");
            if (!UpdateShop && UpdateCounter <= refreshCounterLimit)
                return;

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Starting to update store");

            myStoreBlock.GetPlayerStoreItems(StoreItems);

            foreach (var item in StoreItems)
            {
                myStoreBlock.CancelStoreItem(item.Id);
            }

            Match match;
            string rawIniValue;
            ItemConfig itemConfig = new ItemConfig();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                match = Regex.Match(definition.Id.TypeId.ToString() + definition.Id.SubtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                var section = definition.Id.TypeId.ToString().Remove(0, 16);

                var currentInvItemAmount = MyVisualScriptLogicProvider.GetEntityInventoryItemAmount(myStoreBlock.Name, definition.Id);
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, definition.Id, currentInvItemAmount);

                if (!config.ContainsSection(section) || !config.ContainsKey(section, definition.Id.SubtypeName))
                    continue;

                if (!config.Get(section, definition.Id.SubtypeName).TryGetString(out rawIniValue))
                    continue;

                if (!itemConfig.TryParse(rawIniValue))
                {
                    myStoreBlock.Enabled = false;
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config error in Custom Data.");

                    continue;
                }

                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName);

                var result = Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success;

                long id;
                MyStoreItemData itemData;

                //sell from the players point of view
                if (prefab == null && itemConfig.Sell.Count > 0)
                {
                    itemData = new MyStoreItemData(definition.Id, itemConfig.Sell.Count, itemConfig.Sell.Price, null, null);
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: InsertOrder {definition.Id.SubtypeName}");
                    result = myStoreBlock.InsertOrder(itemData, out id);
                    if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyLog.Default.WriteLine("SimpleStore.StoreBlock: Sell result " + result);
                }

                //buy
                if (itemConfig.Buy.Count > 0)
                {
                    int sellCount = resellItems ? Math.Max(itemConfig.Buy.Count, currentInvItemAmount) : itemConfig.Buy.Count;
                    itemData = new MyStoreItemData(definition.Id, sellCount, itemConfig.Buy.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransaction(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);
                    MyLog.Default.WriteLine($"SimpleStore.StoreBlock: InsertOffer {definition.Id.SubtypeName}");
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, sellCount);
                    else
                        MyLog.Default.WriteLine("SimpleStore.StoreBlock: Buy result " + result);

                }
            }

            UpdateCounter = 0;
            UpdateShop = false;
        }

        private void OnTransaction(int amountSold, int amountRemaining, long priceOfTransaction, long ownerOfBlock, long buyerSeller, MyDefinitionBase compDef)
        {
            var prefab = MyDefinitionManager.Static.GetPrefabDefinition(compDef.Id.SubtypeName);

            if (prefab != null)
            {
                Players.Clear();
                MyAPIGateway.Multiplayer.Players.GetPlayers(Players);

                IMyPlayer player = Players.FirstOrDefault(_player => _player.IdentityId == buyerSeller);

                if (player != null)
                {
                    var inventory = player.Character.GetInventory();
                    var item = inventory.FindItem(compDef.Id);

                    if (item != null)
                    {
                        inventory.RemoveItemAmount(item, item.Amount);
                    }
                    else
                    {
                        player.RequestChangeBalance(priceOfTransaction);
                        MyLog.Default.WriteLine("SimpleStore.StoreBlock: Error can't find Prefab Item (no Spawn)");
                        return;
                    }

                    MyEntity mySafeZone = null;
                    Vector3D spawnPos;
                    float naturalGravityInterference;

                    var spawningOptions = SpawningOptions.RotateFirstCockpitTowardsDirection | SpawningOptions.SetAuthorship | SpawningOptions.UseOnlyWorldMatrix;
                    var position = player.GetPosition() + (player.Character.LocalMatrix.Forward * spawnDistance);

                    MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out naturalGravityInterference);

                    foreach (var safeZone in MySessionComponentSafeZones.SafeZones)
                    {
                        if (safeZone == null || myStoreBlock == null)
                            continue;

                        if (safeZone.PositionComp.WorldAABB.Contains(myStoreBlock.PositionComp.WorldAABB) == ContainmentType.Contains)
                        {
                            mySafeZone = safeZone;
                            break;
                        }
                    }

                    if (naturalGravityInterference != 0f)
                    {
                        var planet = MyGamePruningStructure.GetClosestPlanet(position);
                        var surfacePosition = planet.GetClosestSurfacePointGlobal(position);
                        var upDir = Vector3D.Normalize(surfacePosition - planet.PositionComp.GetPosition());
                        var forwardDir = Vector3D.CalculatePerpendicularVector(upDir);

                        spawnPos = (Vector3D)MyEntities.FindFreePlace(surfacePosition, prefab.BoundingSphere.Radius, ignoreEnt: mySafeZone);

                        MyVisualScriptLogicProvider.SpawnPrefabInGravity(compDef.Id.SubtypeName, spawnPos, forwardDir, ownerId: player.IdentityId, spawningOptions: spawningOptions);
                    }
                    else
                    {
                        spawnPos = (Vector3D)MyEntities.FindFreePlace(position, prefab.BoundingSphere.Radius, ignoreEnt: mySafeZone);

                        MyVisualScriptLogicProvider.SpawnPrefab(compDef.Id.SubtypeName, spawnPos, Vector3D.Forward, Vector3D.Up, ownerId: player.IdentityId, spawningOptions: spawningOptions);
                    }

                    MyVisualScriptLogicProvider.AddGPS(compDef.Id.SubtypeName, compDef.Id.SubtypeName, spawnPos, Color.Green, disappearsInS: 0, playerId: player.IdentityId);
                }
            }
        }


        private void CreateConfig()
        {
            config.Clear();
            config.AddSection(ConfigSettings);
            var sb = new StringBuilder();
            sb.AppendLine("Do not activate too many objects, the store has a limited number of slots.");
            sb.AppendLine("Auto-generated prices are the Keen minimum, setting lower value will log an error.");
            sb.AppendLine("To force a store refresh, turn store block off wait 3s and turn it on. Auto refresh minimum and default is 20 minutes");
            sb.AppendLine("Config errors will cause the store block to turn off");
            sb.AppendLine("Format is BuyAmount:BuyPrice,SellAmount:SellPrice");
            config.SetSectionComment(ConfigSettings, sb.ToString());

            config.Set(ConfigSettings, Resell, true);
            config.Set(ConfigSettings, SpawnDistance, DefaultSpawnDistance);
            config.Set(ConfigSettings, RefreshPeriod, DefaultRefreshPeriod);

            config.AddSection("Ore");
            config.AddSection("Ingot");
            config.AddSection("PhysicalGunObject");
            config.AddSection("AmmoMagazine");
            config.AddSection("Component");
            config.AddSection("OxygenContainerObject");
            config.AddSection("GasContainerObject");
            config.AddSection("ConsumableItem");

            string section;
            Match match;

            ItemConfig defaultItemConfig = new ItemConfig();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                if (BlacklistItems.Contains(definition.Id.SubtypeName))
                    continue;

                match = Regex.Match(definition.Id.TypeId.ToString() + definition.Id.SubtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                section = definition.Id.TypeId.ToString().Remove(0, 16); //remove "MyObjectBuilder_"

                if (config.ContainsSection(section))
                {
                    defaultItemConfig.SetDefaultPrices(definition.Id);
                    config.Set(section, definition.Id.SubtypeName, defaultItemConfig.ToString());
                }
            }

            config.Invalidate();
            myStoreBlock.CustomData = config.ToString();
        }

        private bool TryLoadConfig()
        {
            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Start TryLoadConfig");

            bool configOK = true;
            bool updateConfig = false;

            if (config.TryParse(myStoreBlock.CustomData))
            {
                if (!config.ContainsSection(ConfigSettings))
                    configOK = false;

                resellItems = config.Get(ConfigSettings, Resell).ToBoolean(false);
                spawnDistance = config.Get(ConfigSettings, SpawnDistance).ToInt32(DefaultSpawnDistance);
                refreshCounterLimit = Math.Max((int)(config.Get(ConfigSettings, RefreshPeriod).ToInt32(DefaultRefreshPeriod) * 37.5), MinRefreshPeriod);
                MyLog.Default.WriteLine($"SimpleStore.StoreBlock: RefreshCounterLimit={refreshCounterLimit}");

                List<string> sections = new List<string>();
                List<MyIniKey> keys = new List<MyIniKey>();
                ItemConfig itemConfig = new ItemConfig();
                string rawIniValue;

                config.GetSections(sections);
                foreach (var section in sections)
                {
                    config.GetKeys(section, keys);
                    foreach (var key in keys)
                    {
                        if (!config.Get(section, key.Name).TryGetString(out rawIniValue))
                        {
                            configOK = false;
                            MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Failed to get {section}:{key.Name}");
                            break;
                        }
                        if (section == ConfigSettings)
                        {
                            continue;
                        }

                        if (!itemConfig.TryParse(rawIniValue))
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            updateConfig = true;
                            myStoreBlock.Enabled = false;

                            continue;
                        }

                        if (itemConfig.IsRemoveError())
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            updateConfig = true;
                        }
                    }
                }

                if (updateConfig)
                {
                    config.Invalidate();
                    myStoreBlock.CustomData = config.ToString();
                }

                if (!configOK)
                {
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Value error");
                }

            }
            else
            {
                configOK = false;
                MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Syntax error");
            }
            return configOK;
        }
    }
}