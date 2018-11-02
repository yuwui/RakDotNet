using System;

namespace RakDotNet
{
    public class BitStream
    {
        private byte[] _buffer;
        private int _bitsRead;
        private int _bitsWritten;

        public byte[] BaseBuffer => _buffer;

        public int BitCount => _bitsWritten;
        public int ReadPosition => _bitsRead;

        public bool AllRead => _bitsRead >= _bitsWritten;
        
        public int Capacity
        {
            get => _buffer.Length;
            set
            {
                if (_buffer.Length == value)
                    return;

                var buffer = new byte[value];

                Buffer.BlockCopy(_buffer, 0, buffer, 0, _buffer.Length);

                _buffer = buffer;
            }
        }
        
        public BitStream()
        {
            _buffer = Array.Empty<byte>();
            _bitsRead = 0;
            _bitsWritten = 0;
        }

        public BitStream(byte[] buffer)
        {
            _buffer = buffer;
            _bitsRead = 0;
            _bitsWritten = buffer.Length << 3;
        }

        public void AlignWrite()
        {
            _bitsWritten += 8 - (((_bitsWritten - 1) & 7) + 1);
        }
        
        public void AlignRead()
        {
            _bitsRead += 8 - (((_bitsRead - 1) & 7) + 1);
        }

        #region Bits

        public void WriteBits(byte[] input, int bitCount, bool rightAligned = true)
        {
            if (bitCount <= 0)
                throw new ArgumentException("Bit count has to be >0", nameof(bitCount));

            var bytes = BitsToBytes(_bitsWritten + bitCount);

            if (bytes > _buffer.Length)
                Capacity = bytes;

            var count = bitCount;
            var offset = 0;
            var bitIndex = _bitsWritten & 7;
            
            while (count > 0)
            {
                var b = input[offset];

                if (count < 8 && rightAligned)
                    b <<= 8 - count;

                if (bitIndex == 0)
                {
                    _buffer[_bitsWritten >> 3] = b;
                }
                else
                {
                    _buffer[_bitsWritten >> 3] |= (byte) (b >> bitIndex);

                    if (8 - bitIndex < 8 && 8 - bitIndex < count)
                    {
                        _buffer[(_bitsWritten >> 3) + 1] = (byte) (b << (8 - bitIndex));
                    }
                }

                _bitsWritten += count >= 8 ? 8 : count;

                count -= 8;
                offset++;
            }
        }

        public byte[] ReadBits(int bitCount, bool rightAligned = true)
        {
            if (bitCount <= 0)
                throw new ArgumentException("Bit count has to be >0", nameof(bitCount));

            var output = new byte[BitsToBytes(bitCount)];
            var count = bitCount;
            var offset = 0;
            var bitIndex = _bitsRead & 7;

            while (count > 0)
            {
                output[offset] |= (byte) (_buffer[_bitsRead >> 3] << bitIndex);

                if (bitIndex > 0 && count > 8 - bitIndex)
                    output[offset] |= (byte) (_buffer[(_bitsRead >> 3) + 1] >> (8 - bitIndex));

                count -= 8;

                if (count < 0)
                {
                    if (rightAligned)
                        output[offset] >>= -count;

                    _bitsRead += 8 + count;
                }
                else
                {
                    _bitsRead += 8;
                }

                offset++;
            }

            return output;
        }

        public void WriteBit(bool bit)
        {
            var bytes = BitsToBytes(_bitsWritten + 1);

            if (bytes > _buffer.Length)
                Capacity = bytes;

            var bitIndex = _bitsWritten & 7;

            if (bit)
            {
                if (bitIndex == 0)
                    _buffer[_bitsWritten >> 3] = 0x80;
                else
                    _buffer[_bitsWritten >> 3] |= (byte) (0x80 >> bitIndex);
            }
            
            _bitsWritten++;
        }

        public bool ReadBit()
        {
            var res = _buffer[_bitsRead >> 3] & (0x80 >> (_bitsRead & 7));

            _bitsRead++;

            return res == 1;
        }
        
        public bool TryReadBits(int bitCount, out byte[] output, bool rightAligned = true)
        {
            try
            {
                output = ReadBits(bitCount, rightAligned);

                return true;
            }
            catch (IndexOutOfRangeException)
            {
                output = null;
            }
            
            return false;
        }

        public bool TryReadBit(out bool bit)
        {
            try
            {
                bit = ReadBit();

                return true;
            }
            catch (IndexOutOfRangeException)
            {
                bit = false;
            }
            
            return false;
        }
        
        #endregion
        
        #region Compressed

        public void WriteBitsCompressed(byte[] input, int bitCount, bool unsigned)
        {
            for (var i = (bitCount >> 3) - 1; i > 0; i--)
            {   
                var match = input[i] == (unsigned ? 0x00 : 0xFF);
                
                WriteBit(match);
                
                if (!match)
                {
                    WriteBits(input, (i + 1) << 3);

                    return;
                }
            }

            var match2 = (input[0] & 0xF0) == (unsigned ? 0x00 : 0xF0);
            
            WriteBit(match2);

            WriteBits(new[] {input[0]}, match2 ? 4 : 8);
        }

        public byte[] ReadCompressedBits(int bitCount, bool unsigned)
        {
            var output = new byte[BitsToBytes(bitCount)];
            
            for (var i = (bitCount >> 3) - 1; i > 0; i--)
            {
                if (ReadBit())
                {
                    output[i] = (byte) (unsigned ? 0x00 : 0xFF);
                }
                else
                {
                    return ReadBits((i + 1) << 3);
                }
            }

            return _bitsRead + 1 > _bitsWritten ? output : ReadBits(ReadBit() ? 4 : 8);
        }

        #endregion

        #region Bytes
        
        public void Write(byte[] input)
        {
            if (input.Length == 0) return;

            if ((_bitsWritten & 7) == 0)
            {
                Capacity += input.Length;
                
                Buffer.BlockCopy(input, 0, _buffer, BitsToBytes(_bitsWritten), input.Length);

                _bitsWritten += BytesToBits(_buffer.Length);
            }
            else
            {
                WriteBits(input, BytesToBits(_buffer.Length));
            }
        }

        public byte[] Read(int byteCount)
        {
            if (byteCount <= 0)
                throw new ArgumentException("Count has to be >0", nameof(byteCount));

            if ((_bitsRead & 7) != 0)
                return ReadBits(BytesToBits(byteCount));

            var buffer = new byte[byteCount];
                
            Buffer.BlockCopy(_buffer, _bitsRead >> 3, buffer, 0, byteCount);

            _bitsRead += byteCount << 3;

            return buffer;
        }

        public void WriteCompressed(byte[] input, bool unsigned)
            => WriteBitsCompressed(input, BytesToBits(input.Length), unsigned);

        public byte[] ReadCompressed(int byteCount, bool unsigned)
            => ReadCompressedBits(BytesToBits(byteCount), unsigned);
        
        public bool TryRead(int byteCount, out byte[] output)
        {
            try
            {
                output = Read(byteCount);

                return true;
            }
            catch (IndexOutOfRangeException)
            {
                output = null;
            }

            return false;
        }

        #endregion
        
        #region Read/Write

        #region uint8

        public void WriteByte(byte input)
            => Write(BitConverter.GetBytes(input));

        public void WriteByteCompressed(byte input)
            => WriteCompressed(BitConverter.GetBytes(input), true);

        public void WriteUInt8(byte input)
            => WriteByte(input);

        public void WriteUInt8Compressed(byte input)
            => WriteByteCompressed(input);

        public byte ReadByte()
            => Read(sizeof(byte))[0];

        public byte ReadCompressedByte()
            => ReadCompressed(sizeof(byte), true)[0];

        public byte ReadUInt8()
            => ReadByte();

        public byte ReadCompressedUInt8()
            => ReadCompressedByte();
        
        #endregion

        #region int16
        
        public void WriteShort(short input)
            => Write(BitConverter.GetBytes(input));

        public void WriteShortCompressed(short input)
            => WriteCompressed(BitConverter.GetBytes(input), false);

        public void WriteInt16(short input)
            => WriteShort(input);

        public void WriteInt16Compressed(short input)
            => WriteShortCompressed(input);

        public short ReadShort()
            => BitConverter.ToInt16(Read(sizeof(short)), 0);

        public short ReadCompressedShort()
            => BitConverter.ToInt16(ReadCompressed(sizeof(short), false), 0);

        public short ReadInt16()
            => ReadShort();

        public short ReadCompressedInt16()
            => ReadCompressedShort();
        
        #endregion
        
        #region uint16

        public void WriteUShort(ushort input)
            => Write(BitConverter.GetBytes(input));

        public void WriteUShortCompressed(ushort input)
            => WriteCompressed(BitConverter.GetBytes(input), true);

        public void WriteUInt16(ushort input)
            => WriteUShort(input);

        public void WriteUInt16Compressed(ushort input)
            => WriteUShortCompressed(input);

        public ushort ReadUShort()
            => BitConverter.ToUInt16(Read(sizeof(ushort)), 0);

        public ushort ReadCompressedUShort()
            => BitConverter.ToUInt16(ReadCompressed(sizeof(ushort), true), 0);

        public ushort ReadUInt16()
            => ReadUShort();

        public ushort ReadCompressedUInt16()
            => ReadCompressedUShort();
        
        #endregion

        #region int32

        public void WriteInt(int input)
            => Write(BitConverter.GetBytes(input));

        public void WriteIntCompressed(int input)
            => WriteCompressed(BitConverter.GetBytes(input), false);

        public void WriteInt32(int input)
            => WriteInt(input);

        public void WriteInt32Compressed(int input)
            => WriteIntCompressed(input);

        public int ReadInt()
            => BitConverter.ToInt32(Read(sizeof(int)), 0);

        public int ReadCompressedInt()
            => BitConverter.ToInt32(ReadCompressed(sizeof(int), false), 0);

        public int ReadInt32()
            => ReadInt();

        public int ReadCompressedInt32()
            => ReadCompressedInt();
        
        #endregion

        #region uint32

        public void WriteUInt(uint input)
            => Write(BitConverter.GetBytes(input));

        public void WriteUIntCompressed(uint input)
            => WriteCompressed(BitConverter.GetBytes(input), true);

        public void WriteUInt32(uint input)
            => WriteUInt(input);

        public void WriteUInt32Compressed(uint input)
            => WriteUIntCompressed(input);

        public uint ReadUInt()
            => BitConverter.ToUInt32(Read(sizeof(uint)), 0);

        public uint ReadCompressedUInt()
            => BitConverter.ToUInt32(ReadCompressed(sizeof(uint), true), 0);

        public uint ReadUInt32()
            => ReadUInt();

        public uint ReadCompressedUInt32()
            => ReadCompressedUInt();

        #endregion

        #region int64

        public void WriteLong(long input)
            => Write(BitConverter.GetBytes(input));

        public void WriteLongCompressed(long input)
            => WriteCompressed(BitConverter.GetBytes(input), false);

        public void WriteInt64(long input)
            => WriteLong(input);

        public void WriteInt64Compressed(long input)
            => WriteLongCompressed(input);

        public long ReadLong()
            => BitConverter.ToInt64(Read(sizeof(long)), 0);

        public long ReadCompressedLong()
            => BitConverter.ToInt64(ReadCompressed(sizeof(long), false), 0);

        public long ReadInt64()
            => ReadLong();

        public long ReadCompressedInt64()
            => ReadCompressedLong();

        #endregion

        #region uint64

        public void WriteULong(ulong input)
            => Write(BitConverter.GetBytes(input));

        public void WriteULongCompressed(ulong input)
            => WriteCompressed(BitConverter.GetBytes(input), true);
        
        public void WriteUInt64(ulong input)
            => WriteULong(input);

        public void WriteUInt64Compressed(ulong input)
            => WriteULongCompressed(input);

        public ulong ReadULong()
            => BitConverter.ToUInt64(Read(sizeof(ulong)), 0);

        public ulong ReadCompressedULong()
            => BitConverter.ToUInt64(ReadCompressed(sizeof(ulong), true), 0);

        public ulong ReadUInt64()
            => ReadULong();

        public ulong ReadCompressedUInt64()
            => ReadCompressedULong();

        #endregion

        #region double

        public void WriteDouble(double input)
            => Write(BitConverter.GetBytes(input));

        public void WriteDoubleCompressed(double input)
            => WriteCompressed(BitConverter.GetBytes(input), false);

        public double ReadDouble()
            => BitConverter.ToDouble(Read(sizeof(double)), 0);

        public double ReadCompressedDouble()
            => BitConverter.ToDouble(ReadCompressed(sizeof(double), false), 0);

        #endregion

        #region float

        public void WriteFloat(float input)
            => Write(BitConverter.GetBytes(input));

        public void WriteFloatCompressed(float input)
            => WriteCompressed(BitConverter.GetBytes(input), false);

        public float ReadFloat()
            => BitConverter.ToSingle(Read(sizeof(float)), 0);

        public float ReadCompressedFloat()
            => BitConverter.ToSingle(ReadCompressed(sizeof(float), false), 0);

        #endregion
        
        #endregion

        #region Serializables

        public void WriteSerializable<T>(T input)
            where T : Serializable
            => input.Serialize(this);

        public void ReadSerializable<T>(T output)
            where T : Serializable
            => output.Deserialize(this);

        #endregion

        public static int BitsToBytes(int bits)
            => (int) Math.Ceiling(bits / (double) sizeof(byte));

        public static int BytesToBits(int bytes)
            => bytes * 8;
    }
}