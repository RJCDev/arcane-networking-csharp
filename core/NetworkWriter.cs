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

        public int MaxAllocationBytes = 65535;

        public int RemainingBytes => Buffer.Length - Position;

        public NetworkWriter(int initialCapacity = 1500)
        {
            Buffer = new byte[initialCapacity];
            Position = 0;
        }

        internal void EnsureCapacity(int sizeBytes)
        {
            if (Buffer.Length >= sizeBytes)
                return;
            Array.Resize(ref Buffer, sizeBytes);
        }

        public void Reset() => Position = 0;

        /// <summary>
        /// Writes an object into the buffer using MessagePack.
        /// </summary>
        public void Write<T>(T obj)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            var mpWriter = new MessagePackWriter(bufferWriter);

            MessagePackSerializer.Serialize(ref mpWriter, obj, MessagePackSerializerOptions.Standard);

            mpWriter.Flush();

            ReadOnlySpan<byte> written = bufferWriter.WrittenSpan;

            // Ensure your buffer is large enough
            EnsureCapacity(Position + written.Length);

            written.CopyTo(Buffer.AsSpan(Position));
            Position += written.Length;
        }

        public ArraySegment<byte> ToArraySegment() =>
            new ArraySegment<byte>(Buffer, 0, Position);
    }
}
