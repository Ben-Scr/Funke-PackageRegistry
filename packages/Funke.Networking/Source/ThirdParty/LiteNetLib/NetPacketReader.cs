// Vendored from LiteNetLib 2.1.4 (MIT, see LICENSE.txt). Changes: types internal, nullable off.
#nullable disable
#pragma warning disable CS0649
using LiteNetLib.Utils;

namespace LiteNetLib
{
    internal sealed class NetPacketReader : NetDataReader
    {
        private NetPacket _packet;
        private readonly LiteNetManager _manager;
        private readonly NetEvent _evt;

        internal NetPacketReader(LiteNetManager manager, NetEvent evt)
        {
            _manager = manager;
            _evt = evt;
        }

        internal void SetSource(NetPacket packet, int headerSize)
        {
            if (packet == null)
                return;
            _packet = packet;
            _data = packet.RawData;
            _position = headerSize;
            _offset = headerSize;
            _dataSize = packet.Size;
        }

        internal void RecycleInternal()
        {
            Clear();
            if (_packet != null)
                _manager.PoolRecycle(_packet);
            _packet = null;
            _manager.RecycleEvent(_evt);
        }

        public void Recycle()
        {
            if (_manager.AutoRecycle)
                return;
            RecycleInternal();
        }
    }
}
