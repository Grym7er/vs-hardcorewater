using Vintagestory.API.MathTools;

namespace HardcoreWater
{
    public enum UnresolvedOwnerFallbackMode
    {
        VanillaFallback,
        SkipRefill
    }

    public class HardcoreWaterConfig
    {
        public static HardcoreWaterConfig Loaded { get; set; } = new HardcoreWaterConfig();

        public float AqueductUpdateFrequencySeconds { get; set; } = 0.75f;
        public UnresolvedOwnerFallbackMode UnresolvedOwnerFallbackMode { get; set; } = UnresolvedOwnerFallbackMode.VanillaFallback;

        /// <summary>When true, aqueduct channels can carry vanilla rapids (game:rapidwater-*) from rapid sources; when false, behavior matches legacy (still fresh water only).</summary>
        public bool EnableAqueductRapids { get; set; } = true;

        public void Sanitize()
        {
            AqueductUpdateFrequencySeconds = GameMath.Clamp(AqueductUpdateFrequencySeconds, 0.1f, 10f);
            if (!System.Enum.IsDefined(typeof(UnresolvedOwnerFallbackMode), UnresolvedOwnerFallbackMode))
            {
                UnresolvedOwnerFallbackMode = UnresolvedOwnerFallbackMode.VanillaFallback;
            }
        }
    }
}
