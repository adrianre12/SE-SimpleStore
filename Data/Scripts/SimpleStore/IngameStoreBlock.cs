using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        const string Resell = "ResellItems"; //deprecated
        const string SpawnDistance = "SpawnDistance";
        const string RefreshPeriod = "RefreshPeriodMins";
        const string RefineYield = "RefineYield";
        const string DebugLog = "Debug";

        const int DefaultSpawnDistance = 100;
        const int DefaultRefreshPeriod = 20; //mins
        const int MinRefreshPeriod = 375;  // 100's of ticks mins * 37.5
        const float DefaultRefineYield = 1;

        List<string> BlacklistItems = new List<string> { "RestrictedConstruction", "CubePlacerItem", "GoodAIRewardPunishmentTool" };
        List<MyDefinitionId> AutoRefineList = new List<MyDefinitionId>();

        IMyStoreBlock myStoreBlock;
        MyIni config = new MyIni();

        private static Random rnd = new Random();

        bool lastBlockEnabledState = false;
        bool UpdateShop = true;
        int UpdateCounter = 0;
        bool resellItems = false; //deprecated
        int spawnDistance = DefaultSpawnDistance;
        int refreshCounterLimit = DefaultRefreshPeriod;
        float refineYield = DefaultRefineYield;
        bool debugLog = false;

        List<IMyPlayer> Players = new List<IMyPlayer>();
        List<Sandbox.ModAPI.Ingame.MyStoreQueryItem> StoreItems = new List<Sandbox.ModAPI.Ingame.MyStoreQueryItem>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;

            myStoreBlock = Entity as IMyStoreBlock;

            if (!MyAPIGateway.Session.IsServer)
                return;

            MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: Ores loaded ({RefineOre.OresLoaded()})");

            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Loaded...");
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

                if (myStoreBlock.Enabled == false)
                    return;

                if (!TryLoadConfig())
                    return;

                UpdateShop = true;
            }

            if (!myStoreBlock.IsWorking)
                return;

            UpdateCounter++;

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
            string subtypeName = "";
            AutoRefineList.Clear();

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                subtypeName = FixKey(definition.Id.SubtypeName);
                match = Regex.Match(definition.Id.TypeId.ToString() + subtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                var section = definition.Id.TypeId.ToString().Remove(0, 16); //remove "MyObjectBuilder_"

                if (!config.ContainsSection(section) || !config.ContainsKey(section, subtypeName))
                    continue;

                if (!config.Get(section, subtypeName).TryGetString(out rawIniValue))
                    continue;

                if (!itemConfig.TryParse(rawIniValue))
                {
                    myStoreBlock.Enabled = false;
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config error in Custom Data.");

                    continue;
                }

                var currentInvItemAmount = MyVisualScriptLogicProvider.GetEntityInventoryItemAmount(myStoreBlock.Name, definition.Id);
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, definition.Id, currentInvItemAmount);
                itemConfig.Buy.SetResellCount(currentInvItemAmount);
                if (resellItems)
                    itemConfig.Buy.SetAutoResell();

                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(definition.Id.SubtypeName);

                var result = Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success;

                long id;
                MyStoreItemData itemData;

                //sell from the players point of view
                int sellCount = itemConfig.Sell.Count;
                if (prefab == null && sellCount > 0)
                {
                    itemData = new MyStoreItemData(definition.Id, sellCount, itemConfig.Sell.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransactionSell(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);

                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: InsertOrder {definition.Id.SubtypeName}  Count={sellCount} Price={itemConfig.Sell.Price}");
                    result = myStoreBlock.InsertOrder(itemData, out id);
                    if (result != Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Sell result {definition.Id.SubtypeName}: {result}");
                    if (itemConfig.Sell.IsAutoRefine)
                    {
                        MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: Sell AutoRefine {definition.Id.SubtypeName}");
                        AutoRefineList.Add(definition.Id);
                    }
                }

                //buy
                int buyCount = itemConfig.Buy.Count;
                if (buyCount > 0)
                {
                    itemData = new MyStoreItemData(definition.Id, buyCount, itemConfig.Buy.Price,
                        (amount, left, totalPrice, sellerPlayerId, playerId) => OnTransactionBuy(amount, left, totalPrice, sellerPlayerId, playerId, definition), null);

                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: InsertOffer {definition.Id.SubtypeName} Count={buyCount} Price={itemConfig.Buy.Price}");
                    result = myStoreBlock.InsertOffer(itemData, out id);

                    if (result == Sandbox.ModAPI.Ingame.MyStoreInsertResults.Success)
                        MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, definition.Id, buyCount);
                    else
                        MyLog.Default.WriteLine($"SimpleStore.StoreBlock: Buy result {definition.Id.SubtypeName}: {result}");

                }
            }

            UpdateCounter = 0;
            if (UpdateShop)
            {
                UpdateCounter = rnd.Next((int)(refreshCounterLimit - MinRefreshPeriod * 0.2)); // stop them all refreshing at the same time.
                MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: UpdateCounter={UpdateCounter}");
            }
            UpdateShop = false;
        }

        private void OnTransactionSell(int amountSold, int amountRemaining, long priceOfTransaction, long ownerOfBlock, long buyerSeller, MyDefinitionBase compDef)
        {
            MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionSell {compDef.Id.TypeId} {compDef.Id.SubtypeName}");
            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(ownerOfBlock, priceOfTransaction);

            if (!AutoRefineList.Contains(compDef.Id))
                return;

            MyBlueprintDefinitionBase.Item[] ingots;
            int amountReq;
            if (RefineOre.TryGetIngots(compDef.Id.SubtypeName, out amountReq, out ingots))
            {
                int refineN = amountSold / amountReq;
                MyVisualScriptLogicProvider.RemoveFromEntityInventory(myStoreBlock.Name, compDef.Id, refineN * amountReq);
                MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionSell amountSold={amountSold} amountReq={amountReq} refineN={refineN} oreUsed={refineN * amountReq}");
                foreach (var ingot in ingots)
                {
                    int amountRefined = (int)(refineYield * refineN * ((float)ingot.Amount));
                    MyVisualScriptLogicProvider.AddToInventory(myStoreBlock.Name, ingot.Id, amountRefined);
                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionSell added {amountRefined} {ingot.Id.SubtypeName}");
                }
            }
            else
            {
                MyLog.Default.WriteLine($"SimpleStore.StoreBlock: OnTransactionSell TryGetIngots failed {compDef.Id.SubtypeName}");
            }
        }

        private void OnTransactionBuy(int amountBought, int amountRemaining, long priceOfTransaction, long ownerOfBlock, long buyerSeller, MyDefinitionBase compDef)
        {
            MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionBuy {compDef.Id.TypeId} {compDef.Id.SubtypeName}");

            MyAPIGateway.Multiplayer.Players.RequestChangeBalance(ownerOfBlock, -priceOfTransaction);

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
                    MyLog.Default.WriteLineIf(debugLog, $"SimpleStore.StoreBlock: OnTransactionBuy Spawned {compDef.Id.SubtypeName}");

                }
            }
        }

        private string FixKey(string key)
        {
            return key.Replace('[', '{').Replace(']', '}'); // Replace [ ]  with { } for mods like Better Stone
        }

        private void CreateConfig()
        {
            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Start CreateConfig");

            config.Clear();
            config.AddSection(ConfigSettings);
            var sb = new StringBuilder();
            sb.AppendLine("Do not activate too many items, the store has a total of 30 slots.");
            sb.AppendLine("Auto-generated prices are the Keen minimum, setting lower value will log an error.");
            sb.AppendLine("To force a store refresh, turn store block off wait 3s and turn it on. Auto refresh minimum and default is 20 minutes");
            sb.AppendLine("Config errors will cause the store block to turn off");
            sb.AppendLine("Format is BuyAmount:BuyPrice,SellAmount:SellPrice");
            config.SetSectionComment(ConfigSettings, sb.ToString());

            //config.Set(ConfigSettings, Resell, false);
            config.Set(ConfigSettings, SpawnDistance, DefaultSpawnDistance);
            config.Set(ConfigSettings, RefreshPeriod, DefaultRefreshPeriod);
            config.Set(ConfigSettings, RefineYield, DefaultRefineYield);

            config.AddSection("Ore");
            config.AddSection("Ingot");
            config.AddSection("Component");
            config.AddSection("PhysicalGunObject");
            config.AddSection("AmmoMagazine");
            config.AddSection("OxygenContainerObject");
            config.AddSection("GasContainerObject");
            config.AddSection("ConsumableItem");

            string section;
            Match match;

            ItemConfig defaultItemConfig = new ItemConfig();
            string subtypeName = "";

            foreach (var definition in MyDefinitionManager.Static.GetAllDefinitions())
            {
                if (BlacklistItems.Contains(definition.Id.SubtypeName))
                    continue;
                subtypeName = FixKey(definition.Id.SubtypeName);
                match = Regex.Match(definition.Id.TypeId.ToString() + subtypeName, @"[\[\]\r\n|=]");
                if (match.Success)
                    continue;

                section = definition.Id.TypeId.ToString().Remove(0, 16); //remove "MyObjectBuilder_"

                if (config.ContainsSection(section))
                {
                    defaultItemConfig.SetDefaultPrices(definition.Id);
                    config.Set(section, subtypeName, defaultItemConfig.ToString());
                }
            }

            config.Invalidate();
            myStoreBlock.CustomData = config.ToStringSorted();
        }

        private bool TryLoadConfig()
        {
            MyLog.Default.WriteLine("SimpleStore.StoreBlock: Loading Config");

            bool configOK = true;
            bool storeConfig = false;

            if (config.TryParse(myStoreBlock.CustomData))
            {
                if (!config.ContainsSection(ConfigSettings))
                    configOK = false;

                resellItems = config.Get(ConfigSettings, Resell).ToBoolean(false);
                spawnDistance = config.Get(ConfigSettings, SpawnDistance).ToInt32(DefaultSpawnDistance);
                refreshCounterLimit = Math.Max((int)(config.Get(ConfigSettings, RefreshPeriod).ToInt32(DefaultRefreshPeriod) * 37.5), MinRefreshPeriod);
                refineYield = config.Get(ConfigSettings, RefineYield).ToSingle(DefaultRefineYield);
                debugLog = config.Get(ConfigSettings, DebugLog).ToBoolean(false);

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
                            storeConfig = true;
                            continue;
                        }

                        if (itemConfig.IsRemoveError())
                        {
                            config.Set(section, key.Name, itemConfig.ToString());
                            storeConfig = true;
                        }
                    }
                }

                if (storeConfig)
                {
                    config.Invalidate();
                    myStoreBlock.CustomData = config.ToString();
                }

                if (!configOK)
                {
                    MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Value error");
                    myStoreBlock.Enabled = false;
                }

            }
            else
            {
                configOK = false;
                MyLog.Default.WriteLine("SimpleStore.StoreBlock: Config Syntax error");
                myStoreBlock.Enabled = false;
            }
            return configOK;
        }
    }
}