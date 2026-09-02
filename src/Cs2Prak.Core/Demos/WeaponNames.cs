namespace Cs2Prak.Core.Demos;

public static class WeaponNames
{
    public static string? For(int itemDefinitionIndex) =>
        ByIndex.GetValueOrDefault(itemDefinitionIndex);

    private static readonly Dictionary<int, string> ByIndex = new()
    {
        [1] = "Desert Eagle",
        [2] = "Dual Berettas",
        [3] = "Five-SeveN",
        [4] = "Glock-18",
        [32] = "P2000",
        [36] = "P250",
        [61] = "USP-S",
        [63] = "CZ75-Auto",
        [64] = "R8 Revolver",

        [7] = "AK-47",
        [8] = "AUG",
        [9] = "AWP",
        [10] = "FAMAS",
        [11] = "G3SG1",
        [13] = "Galil AR",
        [16] = "M4A4",
        [38] = "SCAR-20",
        [39] = "SG 553",
        [40] = "SSG 08",
        [60] = "M4A1-S",

        [17] = "MAC-10",
        [19] = "P90",
        [23] = "MP5-SD",
        [24] = "UMP-45",
        [26] = "PP-Bizon",
        [33] = "MP7",
        [34] = "MP9",

        [14] = "M249",
        [25] = "XM1014",
        [27] = "MAG-7",
        [28] = "Negev",
        [29] = "Sawed-Off",
        [35] = "Nova",

        [31] = "Zeus x27",
        [49] = "C4 Explosive",

        [43] = "Flashbang",
        [44] = "High Explosive Grenade",
        [45] = "Smoke Grenade",
        [46] = "Molotov",
        [47] = "Decoy Grenade",
        [48] = "Incendiary Grenade",

        [42] = "knife",
        [59] = "knife_t",

        [500] = "Bayonet",
        [503] = "Classic Knife",
        [505] = "Flip Knife",
        [506] = "Gut Knife",
        [507] = "Karambit",
        [508] = "M9 Bayonet",
        [509] = "Huntsman Knife",
        [512] = "Falchion Knife",
        [514] = "Bowie Knife",
        [515] = "Butterfly Knife",
        [516] = "Shadow Daggers",
        [517] = "Paracord Knife",
        [518] = "Survival Knife",
        [519] = "Ursus Knife",
        [520] = "Navaja Knife",
        [521] = "Nomad Knife",
        [522] = "Stiletto Knife",
        [523] = "Talon Knife",
        [525] = "Skeleton Knife",
        [526] = "Kukri Knife",
    };
}
