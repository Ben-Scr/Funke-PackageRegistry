// Vendored from LiteNetLib 2.1.4 (MIT, see LICENSE.txt). Changes: types internal, nullable off.
#nullable disable
#pragma warning disable CS0649
namespace LiteNetLib
{
    internal readonly ref struct PooledPacket
    {
        internal readonly NetPacket _packet;
        internal readonly byte _channelNumber;

        /// <summary>
        /// Maximum data size that you can put into such packet
        /// </summary>
        public readonly int MaxUserDataSize;

        /// <summary>
        /// Offset for user data when writing to Data array
        /// </summary>
        public readonly int UserDataOffset;

        /// <summary>
        /// Raw packet data. Do not modify header! Use UserDataOffset as start point for your data
        /// </summary>
        public byte[] Data => _packet.RawData;

        internal PooledPacket(NetPacket packet, int maxDataSize, byte channelNumber)
        {
            _packet = packet;
            UserDataOffset = _packet.HeaderSize;
            _packet.Size = UserDataOffset;
            MaxUserDataSize = maxDataSize - UserDataOffset;
            _channelNumber = channelNumber;
        }
    }
}
