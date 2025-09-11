using Godot;
using MessagePack;
using System;

namespace ArcaneNetworking
{
    public class NetworkReader
    {
        internal byte[] buffer;
        public int Position { get; private set; }

        public int MaxAllocationBytes = 65535;

        public int RemainingBytes => buffer.Length - Position;

        public NetworkReader(ArraySegment<byte> bytesIn)
        {
            Reset(bytesIn);
        }
        public NetworkReader()
        {
            buffer = new byte[MaxAllocationBytes]; // Only allocate once
            Reset();
        }

        public void Reset(ArraySegment<byte> bytes) // Resets to this arraysegment, sets the buffer and the offset
        {
            if (bytes.Count > MaxAllocationBytes)
                throw new InvalidOperationException(
                    $"Incoming buffer too large! ({bytes.Count} > {MaxAllocationBytes})");

            buffer = [.. bytes];
            Position = 0;
        }

        public void Reset()
        {
            Position = 0;
        }
        public bool ReadByte(out byte read) => Read(out read);
        /// <summary>
        /// Reads a message object from the current position.
        /// Advances Position based on how much was consumed.
        /// </summary>
        public bool Read<T>(out T read, Type concreteType = default)
        {

            var segment = new ReadOnlyMemory<byte>(buffer, Position, buffer.Length - Position);
            var reader = new MessagePackReader(segment);

            object msg;

            try
            {
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
            catch
            {
                GD.PrintErr("Couldn't deserialize type from reader: " + typeof(T).ToString());
                read = default;
                return false;
            }


        }

        public ArraySegment<byte> ToArraySegment(int startPos = 0) =>
            new ArraySegment<byte>(buffer, startPos, Position);

    }
}
