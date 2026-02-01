using Sandbox.Definitions;
using Sandbox.ModAPI;
using SimpleStore.StoreBlock;
using System;
using System.Linq;
using System.Text;
using VRage.Game.Components;
using VRage.Utils;

namespace SimpleStore
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    class Onezer : MySessionComponentBase
    {
        public static Onezer Instance;

        private OnezerConfig config;
        const string VariableId = nameof(IngameStoreBlockGameLogic);

        public string DefaultCustomData;
        public override void LoadData()
        {
            Instance = this;
            if (MyAPIGateway.Session.IsServer)
                LoadDataHost();
            else
                LoadDataClient();
        }

        private void LoadDataHost()
        {
            config = OnezerConfig.LoadConfig();
            Log.Debug = config.Debug;

            DefaultCustomData = DefaultConfig.CreateDefaultConfigString();
            MyAPIGateway.Utilities.SetVariable<string>(VariableId, Convert.ToBase64String(ASCIIEncoding.UTF8.GetBytes(DefaultCustomData)));

            if (config.Enabled)
            {
                var allDefs = MyDefinitionManager.Static.GetAllDefinitions();
                foreach (var physicalItem in allDefs.OfType<MyPhysicalItemDefinition>())
                {
                    if (Log.Debug) Log.Msg($"Setting '{physicalItem.Id} from {physicalItem.MinimalPricePerUnit} to {config.MinimalPricePerUnit}");
                    physicalItem.MinimalPricePerUnit = config.MinimalPricePerUnit;
                }
            }
            else
            {
                Log.Msg("Onezer not Enabled");
            }
        }

        private void LoadDataClient()
        {
            try
            {
                string saveText;
                if (!MyAPIGateway.Utilities.GetVariable<string>(VariableId, out saveText))
                    throw new Exception($"Variable {VariableId} not found in game save!");
                DefaultCustomData = Encoding.UTF8.GetString(Convert.FromBase64String(saveText));
                MyLog.Default.WriteLine("Client loaded DefaultCustomData");
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"Error getting DefaultCustomData\n {e}");
                DefaultCustomData = "";
            }
        }

        protected override void UnloadData()
        {
            try
            {
                Instance = null;
            }
            catch (Exception e)
            {
                Log.Msg($"Error in UnloadData\n{e.ToString()}");
            }
        }
    }
}
