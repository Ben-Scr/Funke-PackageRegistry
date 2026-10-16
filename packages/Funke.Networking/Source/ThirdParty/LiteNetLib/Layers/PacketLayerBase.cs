// Vendored from LiteNetLib 2.1.4 (MIT, see LICENSE.txt). Changes: types internal, nullable off.
#nullable disable
#pragma warning disable CS0649
using System.Net;

namespace LiteNetLib.Layers
{
    internal abstract class PacketLayerBase
    {
        public readonly int ExtraPacketSizeForLayer;

        protected PacketLayerBase(int extraPacketSizeForLayer)
        {
            ExtraPacketSizeForLayer = extraPacketSizeForLayer;
        }

        public abstract void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int length);
        public abstract void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length);
    }
}
