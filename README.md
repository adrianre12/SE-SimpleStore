#SE SimpleStore

A store block with complete control over what is bought and sold via a simple Custom Data configuration.
Simply set the amounts and prices to buy and sell in the config, turn it on and it will do the rest. Automatically refreshing the store at every restart or every 20 minutes. No need to fill it with items, it creates them itself. 

The optional auto resell allows the SimpleStore to sell items that have been bought. 
Example: Configured to sell 1000 Ice and buy 50000 ice. Someone sells  50000 ice and at the next refresh the store will be selling 51000 ice.

If SimpleStore is run with an empty Custom Data it auto-populates it with all Ores, Ingots,  Components,  Ammunition,  Tools, Weapons, Bottles and Consumable Items. This includes items of these categories from mods.
All prices are set at the minimum SE allows. If you use a mod to change prices SimpleStore will use those instead.

It can also sell ships and rovers, and it comes pre-configured with a selection of built-in ships but it is easy to add custom ones.

This is a fork of the excellent mod [EconomySurvival Store](https://github.com/diKsens/SE.de-Community/tree/master/EconomySurvival%20Store) 


