namespace ArrowFixes
{
    /// <summary>
    /// Cloth wraps for the bone and modern arrow models, in each model's mesh space (+Y = towards the tip).
    /// Shaft measurements come from the game meshes ('BoneArrow', 'RecurveBow_Arrow1'); the same meshes are used for
    /// the inventory, crafting mat and bow arrows, so one wrap per model fits everywhere.
    /// Re-check with scripts/render_cloth_preview.py after changing anything here.
    /// </summary>
    internal static class ClothWrapSpecs
    {
        /// <summary>
        /// Bone arrow: round shaft (radius ~0.0105) up to the flat blade (base at y ~0.43, point at 0.637). Like the
        /// game's wrap on the wooden arrow, it covers the head and a bit of shaft behind it, leaving the point free.
        /// The blade stations follow its width (x) and thickness (z); the barb tips at the blade's base poke out of the
        /// wrap, like the wooden arrow's head does.
        /// </summary>
        public static readonly ClothWrapSpec Bone = new ClothWrapSpec
        {
            Name = "BoneArrowCloth",
            Y0 = 0.36f, Y1 = 0.59f,
            StationY = new[] { 0.25f, 0.42f, 0.4375f, 0.4625f, 0.4875f, 0.5125f, 0.5375f, 0.5625f, 0.5875f, 0.6125f },
            CenterX = new[] { 0.0160f, 0.0160f, 0.0189f, 0.0176f, 0.0165f, 0.0193f, 0.0160f, 0.0196f, 0.0144f, 0.0199f },
            CenterZ = new[] { -0.0015f, -0.0015f, -0.0015f, -0.0015f, -0.0012f, -0.0007f, -0.0015f, -0.0012f, -0.0019f, -0.0013f },
            HalfX = new[] { 0.0106f, 0.0106f, 0.0438f, 0.0405f, 0.0332f, 0.0293f, 0.0225f, 0.0171f, 0.0063f, 0.0058f },
            HalfZ = new[] { 0.0102f, 0.0102f, 0.0107f, 0.0119f, 0.0119f, 0.0103f, 0.0081f, 0.0065f, 0.0042f, 0.0022f },
            Thickness = 0.009f,
            Seed = 1,
        };

        /// <summary>Modern arrow: one tapered carbon shaft (radius 0.0075 at the back, 0.0114 at the front insert, y 1.365).</summary>
        public static readonly ClothWrapSpec Modern = new ClothWrapSpec
        {
            Name = "ModernArrowCloth",
            Y0 = 1.12f, Y1 = 1.36f,
            StationY = new[] { -1.177f, 1.365f },
            CenterX = new[] { 0f, 0f }, CenterZ = new[] { -0.0019f, 0f },
            HalfX = new[] { 0.0079f, 0.0114f }, HalfZ = new[] { 0.0070f, 0.0114f },
            Thickness = 0.008f,
            Seed = 2,
        };
    }
}
