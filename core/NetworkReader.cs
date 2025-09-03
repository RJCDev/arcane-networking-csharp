using Godot;
using MessagePack;
using System;

namespace ArcaneNetworking
{
    public class NetworkReader
    {
        internal byte[] Buffer;
        public int Position { get; private set; }

        public int MaxAllocationBytes = 65535;

        public NetworkReader(ArraySegment<byte> bytesIn)
        {
            Reset(bytesIn);
        }
        public NetworkReader()
        {
            Buffer = new byte[MaxAllocationBytes]; // Only allocate once
            Reset();
        }

        public void Reset(ArraySegment<byte> bytes)
        {
            if (bytes.Array.Length > MaxAllocationBytes)
                throw new InvalidOperationException(
                    $"Incoming buffer too large! ({bytes.Array.Length} > {MaxAllocationBytes})");

            Buffer = bytes.Array;
            Position = bytes.Offset;
        }

        public void Reset()
        {
            Position = 0;
        }

        /// <summary>
        /// Reads a message object from the current position.
        /// Advances Position based on how much was consumed.
        /// </summary>
        public bool Read<T>(out T read, Type concreteType = default)
        {
            try
            {
                var segment = new ReadOnlyMemory<byte>(Buffer, Position, Buffer.Length - Position);
                var reader = new MessagePackReader(segment);

                object msg;

                // Read in the type
                if (concreteType != default)
                {
                    msg = MessagePackSerializer.Deserialize(concreteType, ref reader, MessagePackSerializer.DefaultOptions);
                }
                else // Read in a T
                {
                    msg = MessagePackSerializer.Deserialize<T>(ref reader, MessagePackSerializer.DefaultOptions);
                }

                Position += (int)reader.Consumed;
                read = (T)msg;

                return true;
            }
            catch (MessagePackSerializationException e)
            {
                GD.PrintErr("Couldn't deserialize type from writer: " + typeof(T).ToString());
                GD.PrintErr(e.Message);
                read = default;
                return false;
            }

        }

        public ArraySegment<byte> ToArraySegment(int startPos = 0) =>
            new ArraySegment<byte>(Buffer, startPos, Position);

    }
}
