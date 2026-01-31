using Sandbox.Definitions;
using Sandbox.ModAPI;
using System.Linq;
using VRage.Game.Components;

namespace SimpleStore.Onezer
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    class Onezer : MySessionComponentBase
    {
        private OnezerConfig config;
        public override void LoadData()
        {
            base.LoadData();
            if (!MyAPIGateway.Session.IsServer)
                return;


            config = OnezerConfig.LoadConfig();
            Log.Debug = config.Debug;

            if (!config.Enabled)
            {
                Log.Msg("Onezer not Enabled");
                return;
            }

            var allDefs = MyDefinitionManager.Static.GetAllDefinitions();
            foreach (var physicalItem in allDefs.OfType<MyPhysicalItemDefinition>())
            {
                if (Log.Debug) Log.Msg($"Setting '{physicalItem.Id} from {physicalItem.MinimalPricePerUnit} to {config.MinimalPricePerUnit}");
                physicalItem.MinimalPricePerUnit = config.MinimalPricePerUnit;
            }
        }
    }
}
