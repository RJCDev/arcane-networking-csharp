using Godot;
using System;
using MessagePack;
using System.Buffers;

namespace ArcaneNetworking
{
    public class NetworkWriter
    {
        internal byte[] Buffer;
        public int Position { get; private set; }

        public static int MaxAllocationBytes = 65535;

        public int RemainingBytes => Buffer.Length - Position;

        public NetworkWriter(int initialCapacity = 1500)
        {
            Buffer = new byte[initialCapacity];
            Position = 0;
        }

        internal void Resize(int sizeBytes)
        {
            if (Buffer.Length >= sizeBytes)
                return;
            
            Array.Resize(ref Buffer, sizeBytes);
        }

        public void Reset() => Position = 0;

        // Write bytes to the end of this writer
        public void WriteBytes(ArraySegment<byte> bytes)
        {
            // Ensure your buffer is large enough
            Resize(Position + bytes.Count);

            Array.ConstrainedCopy(bytes.Array, bytes.Offset, Buffer, Position, bytes.Count);
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
            Resize(Position + written.Length);

            written.CopyTo(Buffer.AsSpan(Position));
            Position += written.Length;
        }

        public ArraySegment<byte> ToArraySegment() =>
            new ArraySegment<byte>(Buffer, 0, Position);
    }
}
