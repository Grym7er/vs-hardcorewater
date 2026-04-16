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
