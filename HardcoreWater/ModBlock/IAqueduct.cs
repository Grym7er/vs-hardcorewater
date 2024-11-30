using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HardcoreWater.ModBlock
{
    internal interface IAqueduct
    {
        string Orientation { get; }

        bool IsEnclosed { get; }
    }
}
