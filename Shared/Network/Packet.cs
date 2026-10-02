using System;
using System.IO;
using System.Text;

namespace Shared.Network
{
    /// <summary>
    /// Represents a network packet with a length, opcode, and payload.
    /// Can be used for both reading incoming packets and writing outgoing packets.
    /// </summary>
    public class Packet : IDisposable
    {
        private MemoryStream _memoryStream;
        private BinaryWriter? _writer;
        private BinaryReader? _reader;
        
        public OpCode PacketId { get; private set; }

        /// <summary>
        /// Creates a new packet for WRITING (sending).
        /// </summary>
        public Packet(OpCode opCode)
        {
            PacketId = opCode;
            _memoryStream = new MemoryStream();
            _writer = new BinaryWriter(_memoryStream, Encoding.UTF8);
            
            // Reserve 2 bytes for the total packet length (we will overwrite this later)
            _writer.Write((ushort)0);
            
            // Write the OpCode
            _writer.Write((ushort)PacketId);
        }

        /// <summary>
        /// Creates a new packet for READING (receiving).
        /// </summary>
        public Packet(byte[] data)
        {
            _memoryStream = new MemoryStream(data);
            _reader = new BinaryReader(_memoryStream, Encoding.UTF8);
            
            // Skip the length (2 bytes) because we already used it to frame the packet
            _reader.ReadUInt16();
            
            // Read the OpCode
            PacketId = (OpCode)_reader.ReadUInt16();
        }

        // --- WRITING METHODS ---
        public void Write(byte value) => _writer!.Write(value);
        public void Write(bool value) => _writer!.Write(value);
        public void Write(int value) => _writer!.Write(value);
        public void Write(long value) => _writer!.Write(value);
        public void Write(float value) => _writer!.Write(value);
        public void Write(string value) => _writer!.Write(value); // BinaryWriter handles length prefix for strings automatically
        public void Write(Shared.Math.Vector3 vector) 
        {
            _writer!.Write(vector.X);
            _writer.Write(vector.Y);
            _writer.Write(vector.Z);
        }

        /// <summary>
        /// Finalizes the packet by writing the total length at the very beginning of the stream.
        /// Returns the final byte array to send over the network.
        /// </summary>
        public byte[] ToArray()
        {
            // The total length of the packet is the current stream length
            ushort length = (ushort)_memoryStream.Length;
            
            // Go back to the beginning of the stream (position 0)
            _writer!.Seek(0, SeekOrigin.Begin);
            
            // Overwrite the first 2 bytes with the actual length
            _writer.Write(length);
            
            return _memoryStream.ToArray();
        }

        // --- READING METHODS ---
        public byte ReadByte() => _reader!.ReadByte();
        public bool ReadBool() => _reader!.ReadBoolean();
        public int ReadInt() => _reader!.ReadInt32();
        public long ReadLong() => _reader!.ReadInt64();
        public float ReadFloat() => _reader!.ReadSingle();
        public string ReadString() => _reader!.ReadString();
        public Shared.Math.Vector3 ReadVector3() 
        {
            return new Shared.Math.Vector3(_reader!.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _memoryStream?.Dispose();
        }
    }
}
