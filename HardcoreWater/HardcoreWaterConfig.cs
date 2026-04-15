using Vintagestory.API.MathTools;

namespace HardcoreWater
{
    public class HardcoreWaterConfig
    {
        public static HardcoreWaterConfig Loaded { get; set; } = new HardcoreWaterConfig();

        public float AqueductUpdateFrequencySeconds { get; set; } = 0.75f;

        public void Sanitize()
        {
            AqueductUpdateFrequencySeconds = GameMath.Clamp(AqueductUpdateFrequencySeconds, 0.1f, 10f);
        }
    }
}
