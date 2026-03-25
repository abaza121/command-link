using Unity.Collections;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Serializer contract for deterministic packet payloads.
    /// </summary>
    public interface INetworkSerializer<TMessage>
    {
        bool TrySerialize(in TMessage message, ref FixedList512Bytes<byte> destination);
        bool TryDeserialize(in FixedList512Bytes<byte> source, out TMessage message);
    }
}
