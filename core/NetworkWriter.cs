using Godot;
using System;
using MessagePack;
using System.Buffers;

namespace ArcaneNetworking
{
    public class NetworkWriter : IBufferWriter<byte>
    {
        internal byte[] buffer;
        public int Position { get; private set; }

        public static int MaxAllocationBytes = 65535;

        public int RemainingBytes => buffer.Length - Position;

        public NetworkWriter(int initialCapacity = 1500)
        {
            buffer = new byte[initialCapacity];
            Position = 0;
        }

        internal void EnsureCapacity(int size)
        {
            if (size > MaxAllocationBytes)
            {
                GD.PrintErr("[Network Writer] Write Failed! Buffer Too Large: " + size + "b! Write Failed!");
                throw new InvalidOperationException("NetworkWriter exceeded MaxAllocationBytes");
            }

            if (size > buffer.Length)
            {
                int newSize = buffer.Length;
                while (newSize < size)
                    newSize *= 2;

                Array.Resize(ref buffer, newSize);
            }

        }

        public void Reset() => Position = 0;

        // Write bytes to the end of this writer
        public void WriteBytes(ArraySegment<byte> bytes)
        {
            // Ensure your buffer is large enough
            EnsureCapacity(Position + bytes.Count);

            Buffer.BlockCopy(bytes.Array, bytes.Offset, buffer, Position, bytes.Count);
            Position += bytes.Count;
        }
        /// <summary>
        /// Writes an object into the buffer using MessagePack.
        /// </summary>
        public void Write<T>(T obj)
        {
            var mpWriter = new MessagePackWriter(this); // this implements IBufferWriter
            MessagePackSerializer.Serialize(ref mpWriter, obj, MessagePackSerializer.DefaultOptions);
            mpWriter.Flush();

        }
        public void Advance(int count)
        {
            Position += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(Position + sizeHint);
            return buffer.AsMemory(Position);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(Position + sizeHint);
            return buffer.AsSpan(Position);
        }

        public ArraySegment<byte> ToArraySegment() =>
            new ArraySegment<byte>(buffer, 0, Position);
    }
}
