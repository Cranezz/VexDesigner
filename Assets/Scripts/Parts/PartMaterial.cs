namespace VexDesigner.Parts
{
    /// <summary>
    /// What a part is made of.
    ///
    /// Currently drives impact sound only. It will also drive mass estimation
    /// for parts with no published weight, where assuming aluminium
    /// overestimates anything plastic by roughly a factor of two.
    /// </summary>
    public enum PartMaterial
    {
        /// <summary>Structure: C-channels, plates, bars.</summary>
        Aluminium = 0,

        /// <summary>Fasteners, shafts, bearings.</summary>
        Steel = 1,

        /// <summary>Gears, spacers, some brackets.</summary>
        Plastic = 2,

        /// <summary>Wheels, tread, stoppers.</summary>
        Rubber = 3,
    }
}
