using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HardcoreWater
{
    public class HardcoreWaterConfig
    {
        public static HardcoreWaterConfig Loaded { get; set; } = new HardcoreWaterConfig();

        public float AqueductUpdateFrequencySeconds { get; set; } = 0.5f;
    }
}
