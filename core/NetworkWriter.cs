using Godot;
using System;
using MessagePack;
using System.Buffers;

namespace ArcaneNetworking
{
    public class NetworkWriter
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

        internal void EnsureCapacity(int sizeBytes)
        {
            if (buffer.Length >= sizeBytes)
                return;
            
            int newSize = Math.Max(sizeBytes, buffer.Length * 2);
            Array.Resize(ref buffer, newSize);

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
            var bufferWriter = new ArrayBufferWriter<byte>();
            var mpWriter = new MessagePackWriter(bufferWriter);

            MessagePackSerializer.Serialize(ref mpWriter, obj, MessagePackSerializer.DefaultOptions);

            mpWriter.Flush();

            ReadOnlySpan<byte> written = bufferWriter.WrittenSpan;

            if (Position + written.Length > MaxAllocationBytes)
            {
                GD.PrintErr("[Network Writer] Write Failed! Buffer Too Large: " + (Position + written.Length) + "b! Write Failed!");
                return;
            }

            // Ensure your buffer is large enough
            EnsureCapacity(Position + written.Length);

            written.CopyTo(buffer.AsSpan(Position));
            Position += written.Length;
        }

        public ArraySegment<byte> ToArraySegment() =>
            new ArraySegment<byte>(buffer, 0, Position);
    }
}
