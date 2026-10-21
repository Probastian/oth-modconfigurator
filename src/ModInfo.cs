using MelonLoader;

[assembly: MelonInfo(typeof(ModConfigurator.Mod), ModConfigurator.ModInfo.Name, ModConfigurator.ModInfo.Version, ModConfigurator.ModInfo.Author)]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace ModConfigurator
{
    internal static class ModInfo
    {
        public const string Name = "ModConfigurator";
        public const string Version = "0.3.0";
        public const string Author = "Probastian";
    }
}
