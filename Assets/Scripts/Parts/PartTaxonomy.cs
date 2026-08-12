namespace VexDesigner.Parts
{
    /// <summary>
    /// Top-level category, matching how VEX organises its own catalogue.
    ///
    /// Kept aligned with the vendor's categories on purpose: it is what part
    /// numbers are grouped by on the site the parts come from, so anyone adding
    /// a part already knows which to pick without a judgement call.
    /// </summary>
    public enum PartClass
    {
        Structure = 0,
        Motion = 1,
        Electronics = 2,
        Pneumatics = 3,

        /// <summary>Field elements, game objects, anything not on a robot.</summary>
        Other = 99,
    }

    /// <summary>
    /// What kind of thing the part is within its class.
    ///
    /// Drives behaviour, not just tidiness: only some sub-classes can be cut,
    /// only some have the standard hole grid, and screws and nuts will need to
    /// find each other when fastening is built.
    /// </summary>
    public enum PartSubClass
    {
        Unknown = 0,

        // Structure
        CChannel = 10,
        Angle = 11,
        Bar = 12,
        Plate = 13,
        Standoff = 14,
        Bracket = 15,

        // Fasteners
        Screw = 30,
        Nut = 31,
        Spacer = 32,
        Rivet = 33,

        // Motion
        Shaft = 50,
        Gear = 51,
        Sprocket = 52,
        Chain = 53,
        Bearing = 54,
        Wheel = 55,
        Pulley = 56,

        // Electronics
        Motor = 70,
        Brain = 71,
        Sensor = 72,
        Battery = 73,

        // Pneumatics
        Cylinder = 90,
        Reservoir = 91,
        Valve = 92,
        Tubing = 93,
    }
}
