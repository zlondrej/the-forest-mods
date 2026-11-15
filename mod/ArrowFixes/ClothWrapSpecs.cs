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
        /// <summary>Bone arrow: round shaft (radius ~0.0105) up to the flat blade, whose base is at y ~0.43.</summary>
        public static readonly ClothWrapSpec Bone = new ClothWrapSpec
        {
            Name = "BoneArrowCloth",
            Y0 = 0.28f, Y1 = 0.44f,
            StationY = new[] { 0.25f, 0.46f },
            CenterX = new[] { 0.0160f, 0.0160f }, CenterZ = new[] { -0.0015f, -0.0015f },
            HalfX = new[] { 0.0106f, 0.0106f }, HalfZ = new[] { 0.0102f, 0.0102f },
            Bulk = 1.0f,
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
            Bulk = 0.7f,
            Seed = 2,
        };
    }
}
