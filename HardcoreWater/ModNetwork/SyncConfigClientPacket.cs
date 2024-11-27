using ProtoBuf;

namespace HardcoreWater.ModNetwork
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SyncConfigClientPacket
    {
        public float AqueductUpdateFrequencySeconds;
    }
}