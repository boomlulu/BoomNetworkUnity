using System;
using System.Text;
using NUnit.Framework;
using BoomNetwork.Core;
using BoomNetwork.Core.Codec;

namespace BoomNetwork.Tests
{
    public class CodecTests
    {
        [Test]
        public void Encode_Decode_NoSeq()
        {
            var msg = new Message { Cmd = 42, Data = Array.Empty<byte>() };
            var buf = new byte[MessageCodec.EncodedSize(msg)];
            MessageCodec.Encode(msg, buf);

            var decoded = MessageCodec.Decode(buf);
            Assert.AreEqual(42, decoded.Cmd);
            Assert.IsFalse(decoded.HasSeq);
            Assert.AreEqual(0, decoded.DataLength);
        }

        [Test]
        public void Encode_Decode_WithSeq()
        {
            var payload = Encoding.UTF8.GetBytes("Hello Unity!");
            var msg = new Message
            {
                Cmd = 10, HasSeq = true, Seq = 999,
                Data = payload, DataLength = payload.Length,
            };
            var buf = new byte[MessageCodec.EncodedSize(msg)];
            MessageCodec.Encode(msg, buf);

            var decoded = MessageCodec.Decode(buf);
            Assert.AreEqual(10, decoded.Cmd);
            Assert.AreEqual(999, decoded.Seq);
            Assert.AreEqual("Hello Unity!", Encoding.UTF8.GetString(decoded.DataSpan));
        }

        [Test]
        public void Encode_Decode_LargeData()
        {
            var data = new byte[70000];
            new Random(42).NextBytes(data);
            var msg = new Message
            {
                Cmd = 5, HasSeq = true, Seq = 1,
                Data = data, DataLength = data.Length,
            };
            var buf = new byte[MessageCodec.EncodedSize(msg)];
            MessageCodec.Encode(msg, buf);

            var decoded = MessageCodec.Decode(buf);
            Assert.AreEqual(5, decoded.Cmd);
            Assert.AreEqual(70000, decoded.DataLength);
        }

        [Test]
        public void PeekFrameSize()
        {
            var msg = new Message { Cmd = 1, HasSeq = true, Seq = 5, Data = new byte[20], DataLength = 20 };
            var buf = new byte[MessageCodec.EncodedSize(msg)];
            MessageCodec.Encode(msg, buf);

            Assert.AreEqual(buf.Length, MessageCodec.PeekFrameSize(buf));
        }

        [Test]
        public void HeaderSize_Variants()
        {
            var m1 = new Message { Cmd = 1, Data = Array.Empty<byte>() };
            Assert.AreEqual(3, m1.HeaderSize); // FlagsCmd(1) + BodyLen(2)

            var m2 = new Message { Cmd = 1, HasSeq = true, Data = Array.Empty<byte>() };
            Assert.AreEqual(7, m2.HeaderSize); // + Seq(4)

            var m3 = new Message { Cmd = 1, Data = new byte[70000], DataLength = 70000 };
            Assert.AreEqual(5, m3.HeaderSize); // FlagsCmd(1) + BodyLen(4)
        }
    }
}
