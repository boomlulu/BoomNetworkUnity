using System;
using System.Text;
using NUnit.Framework;
using BoomNetwork.Core;
using BoomNetwork.Core.Codec;
using BoomNetwork.Core.Framing;

namespace BoomNetwork.Tests
{
    public class FramingTests
    {
        private LengthPrefixFraming _framing;

        [SetUp]
        public void SetUp() => _framing = new LengthPrefixFraming();

        [TearDown]
        public void TearDown() => _framing.Reset();

        [Test]
        public void SingleFrame()
        {
            var bytes = Encode(1, "test");
            Assert.AreEqual(1, _framing.Feed(bytes, 0, bytes.Length));
            Assert.IsTrue(_framing.TryDequeueFrame(out var frame));
            using (frame)
            {
                var msg = MessageCodec.Decode(frame.Span);
                Assert.AreEqual(1, msg.Cmd);
                Assert.AreEqual("test", Encoding.UTF8.GetString(msg.DataSpan));
            }
        }

        [Test]
        public void StickyPackets()
        {
            var b1 = Encode(1, "aa");
            var b2 = Encode(2, "bb");
            var combined = new byte[b1.Length + b2.Length];
            Buffer.BlockCopy(b1, 0, combined, 0, b1.Length);
            Buffer.BlockCopy(b2, 0, combined, b1.Length, b2.Length);

            Assert.AreEqual(2, _framing.Feed(combined, 0, combined.Length));
            AssertCmd(1);
            AssertCmd(2);
        }

        [Test]
        public void SplitPacket()
        {
            var bytes = Encode(30, "split");
            int mid = bytes.Length / 2;
            Assert.AreEqual(0, _framing.Feed(bytes, 0, mid));
            Assert.AreEqual(1, _framing.Feed(bytes, mid, bytes.Length - mid));
            AssertCmd(30);
        }

        [Test]
        public void ByteByByte()
        {
            var bytes = Encode(7, "x");
            for (int i = 0; i < bytes.Length - 1; i++)
                Assert.AreEqual(0, _framing.Feed(bytes, i, 1));
            Assert.AreEqual(1, _framing.Feed(bytes, bytes.Length - 1, 1));
            AssertCmd(7);
        }

        private void AssertCmd(byte expected)
        {
            Assert.IsTrue(_framing.TryDequeueFrame(out var frame));
            using (frame)
                Assert.AreEqual(expected, MessageCodec.Decode(frame.Span).Cmd);
        }

        private static byte[] Encode(byte cmd, string data)
        {
            var d = Encoding.UTF8.GetBytes(data);
            var msg = new Message { Cmd = cmd, Data = d, DataLength = d.Length };
            var buf = new byte[MessageCodec.EncodedSize(msg)];
            MessageCodec.Encode(msg, buf);
            return buf;
        }
    }
}
