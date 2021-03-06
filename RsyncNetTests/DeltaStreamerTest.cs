﻿namespace RsyncNetTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using RsyncNet.Delta;
    using RsyncNet.Helpers;

    [TestClass]
    public class DeltaStreamerTest
    {
        #region Methods: public

        #region Send
        [TestMethod]
        public void Send_has_correct_data_for_byte_delta()
        {
            var streamer = new DeltaStreamer();
            streamer.StreamChunkSize = 2;
            var deltas = new[] {new ByteDelta {Offset = 0, Length = 4}};
            var dataStream = new MemoryStream(Encoding.ASCII.GetBytes("TEST"));
            var outStream = new MemoryStream();
            streamer.Send(deltas, dataStream, outStream);
            Assert.IsTrue(Encoding.ASCII.GetBytes("TEST").SequenceEqual(outStream.GetBuffer().Skip(5).Take(4)));
        }

        [TestMethod]
        public void Send_has_correct_length_for_byte_delta()
        {
            var streamer = new DeltaStreamer();
            var deltas = new[] {new ByteDelta {Offset = 0, Length = 4}};
            var dataStream = new MemoryStream(Encoding.ASCII.GetBytes("TEST"));
            var outStream = new MemoryStream();
            streamer.Send(deltas, dataStream, outStream);
            Assert.AreEqual(4, BitConverter.ToInt32(outStream.GetBuffer(), 1));
        }

        [TestMethod]
        public void Send_has_correct_length_for_copy_delta()
        {
            var streamer = new DeltaStreamer();
            var deltas = new[] {new CopyDelta {Offset = 42, Length = 24}};
            var outStream = new MemoryStream();
            streamer.Send(deltas, new MemoryStream(), outStream);
            Assert.AreEqual(24, BitConverter.ToInt32(outStream.GetBuffer(), 9));
        }

        [TestMethod]
        public void Send_has_correct_offset_for_copy_delta()
        {
            var streamer = new DeltaStreamer();
            var deltas = new[] {new CopyDelta {Offset = 42, Length = 24}};
            var outStream = new MemoryStream();
            streamer.Send(deltas, new MemoryStream(), outStream);
            Assert.AreEqual(42, BitConverter.ToInt64(outStream.GetBuffer(), 1));
        }

        [TestMethod]
        public void Send_starts_with_copy_command_for_copy_delta()
        {
            var streamer = new DeltaStreamer();
            var deltas = new[] {new CopyDelta {Offset = 42, Length = 24}};
            var outStream = new MemoryStream();
            streamer.Send(deltas, new MemoryStream(), outStream);
            Assert.AreEqual(DeltaStreamer.DeltaStreamConstants.COPY_BLOCK_START_MARKER, outStream.GetBuffer()[0]);
        }

        [TestMethod]
        public void Send_starts_with_new_command_for_byte_delta()
        {
            var streamer = new DeltaStreamer();
            var deltas = new[] {new ByteDelta {Offset = 0, Length = 4}};
            var dataStream = new MemoryStream(Encoding.ASCII.GetBytes("TEST"));
            var outStream = new MemoryStream();
            streamer.Send(deltas, dataStream, outStream);
            Assert.AreEqual(DeltaStreamer.DeltaStreamConstants.NEW_BLOCK_START_MARKER, outStream.GetBuffer()[0]);
        }

        [TestMethod]
        [ExpectedException(typeof (IOException))]
        public void Send_throws_for_delta_out_of_inputStream_bounds_for_byte_delta()
        {
            var deltas = new[] {new ByteDelta {Offset = 3, Length = 3}};
            var dataStream = new MemoryStream(new byte[2]);
            var streamer = new DeltaStreamer();
            streamer.Send(deltas, dataStream, new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (ArgumentException))]
        public void Send_throws_for_empty_delta_array()
        {
            var deltas = new List<IDelta>();
            var streamer = new DeltaStreamer();
            streamer.Send(deltas, new MemoryStream(), new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (IOException))]
        public void Send_throws_for_inputStream_with_insufficient_data_for_byte_delta()
        {
            var deltas = new[] {new ByteDelta {Offset = 0, Length = 3}};
            var dataStream = new MemoryStream(new byte[2]);
            var streamer = new DeltaStreamer();
            streamer.Send(deltas, dataStream, new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (IOException))]
        public void Send_throws_for_inputStream_without_forward_seekability_for_copy_delta()
        {
            var deltas = new[] {new CopyDelta {Offset = 0, Length = 1234}};
            var dataStreamMock = new Mock<MemoryStream>(MockBehavior.Strict);
            dataStreamMock.SetupGet(x => x.CanSeek).Returns(false);
            var streamer = new DeltaStreamer();
            streamer.Send(deltas, dataStreamMock.Object, new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (ArgumentNullException))]
        public void Send_throws_for_null_deltas()
        {
            var streamer = new DeltaStreamer();
            streamer.Send(null, new MemoryStream(), new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (ArgumentNullException))]
        public void Send_throws_for_null_inputStream()
        {
            var streamer = new DeltaStreamer();
            streamer.Send(new ByteDelta[10], null, new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (ArgumentNullException))]
        public void Send_throws_for_null_output_stream()
        {
            var streamer = new DeltaStreamer();
            streamer.Send(new ByteDelta[10], new MemoryStream(), null);
        }

        [TestMethod]
        public void Send_will_chunk_data_reads_into_provided_sizes_for_byte_delta()
        {
            var streamer = new DeltaStreamer();
            int chunkSize = 10;
            streamer.StreamChunkSize = chunkSize;
            var deltas = new[] {new ByteDelta {Offset = 0, Length = 100}};
            var dataStreamMock = new Mock<MemoryStream>();
            dataStreamMock.Setup(x =>
                                 x.Read(It.IsAny<byte[]>(),
                                        It.IsAny<int>(),
                                        It.IsAny<int>()))
                .Returns((byte[] buf, int offset, int length) =>
                             {
                                 if (length > chunkSize) Assert.Fail("Invalid read size");
                                 return length;
                             });
            streamer.Send(deltas, dataStreamMock.Object, new MemoryStream());
        }

        [TestMethod]
        public void Send_will_read_all_chunks_from_stream_for_byte_delta()
        {
            var streamer = new DeltaStreamer();
            int chunkSize = 10;
            streamer.StreamChunkSize = chunkSize;
            var deltas = new[] {new ByteDelta {Offset = 0, Length = 100}};
            var dataStreamMock = new Mock<MemoryStream>();
            int totalLengthRead = 0;
            dataStreamMock.Setup(x =>
                                 x.Read(It.IsAny<byte[]>(),
                                        It.IsAny<int>(),
                                        It.IsAny<int>()))
                .Returns((byte[] buf, int offset, int length) =>
                             {
                                 totalLengthRead += length;
                                 return length;
                             });
            streamer.Send(deltas, dataStreamMock.Object, new MemoryStream());
            Assert.AreEqual(100, totalLengthRead);
        }

        [TestMethod]
        public void Send_will_seek_past_input_data_for_copy_delta()
        {
            var deltas = new[] {new CopyDelta {Offset = 0, Length = 1234}};
            var dataStreamMock = new Mock<MemoryStream>(MockBehavior.Strict);
            dataStreamMock.Setup(x =>
                                 x.Seek(
                                     It.Is<long>(o => o == 1234),
                                     It.Is<SeekOrigin>(o => o == SeekOrigin.Current)))
                .Returns(1234);
            dataStreamMock.SetupGet(x => x.CanSeek).Returns(true);
            var streamer = new DeltaStreamer();
            streamer.Send(deltas, dataStreamMock.Object, new MemoryStream());
            dataStreamMock.VerifyAll();
        }
        #endregion

        #region Receive

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Receive_throws_for_unseekable_inputStream()
        {
            var inputStreamMock = new Mock<MemoryStream>(MockBehavior.Strict);
            inputStreamMock.SetupGet(x => x.CanSeek).Returns(false);
            var streamer = new DeltaStreamer();
            streamer.Receive(new MemoryStream(), inputStreamMock.Object, new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Receive_throws_for_null_deltaStream()
        {
            var streamer = new DeltaStreamer();
            streamer.Receive(null, new MemoryStream(), new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Receive_throws_for_null_inputStream()
        {
            var streamer = new DeltaStreamer();
            streamer.Receive(null, new MemoryStream(), new MemoryStream());
        }
        
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Receive_throws_for_null_outputStream()
        {
            var streamer = new DeltaStreamer();
            streamer.Receive(null, new MemoryStream(), new MemoryStream());
        }

        [TestMethod]
        [ExpectedException(typeof (IOException))]
        public void Receive_throws_for_illegal_command_byte()
        {
            var streamer = new DeltaStreamer();
            streamer.Receive(new MemoryStream(new[] { (byte) 'G' }), new MemoryStream(), new MemoryStream());            
        }

        [TestMethod]
        public void Receive_copies_data_from_inputStream()
        {
            var deltaStream = new MemoryStream();
            deltaStream.WriteByte(DeltaStreamer.DeltaStreamConstants.COPY_BLOCK_START_MARKER);
            deltaStream.WriteLong(4); // Start the copy from byte four
            deltaStream.WriteInt(10); // Copy 10 bytes
            deltaStream.Seek(0, SeekOrigin.Begin);

            var inputStream = new MemoryStream(); // Must have 4 + 10 bytes
            for (int i = 0; i < 20; ++i) inputStream.WriteByte((byte) (255 - i));
            inputStream.Seek(0, SeekOrigin.Begin);

            var outputStream = new MemoryStream();
            
            var streamer = new DeltaStreamer();
            streamer.Receive(deltaStream, inputStream, outputStream);

            Assert.AreEqual(10, outputStream.Length);
            outputStream.GetBuffer().Take(14).SequenceEqual(inputStream.GetBuffer().Skip(3).Take(14));
        }

        [TestMethod]
        public void Receive_writes_new_bytes_from_deltaStream()
        {
            var deltaStream = new MemoryStream();
            deltaStream.WriteByte(DeltaStreamer.DeltaStreamConstants.NEW_BLOCK_START_MARKER);
            deltaStream.WriteInt(10); // Write 10 bytes
            for (int i = 0; i < 10; ++i) deltaStream.WriteByte((byte) (200 - i));
            deltaStream.Seek(0, SeekOrigin.Begin);

            var inputStream = new MemoryStream(); // empty
            var outputStream = new MemoryStream();

            var streamer = new DeltaStreamer();
            streamer.Receive(deltaStream, inputStream, outputStream);

            Assert.AreEqual(10, outputStream.Length);
            outputStream.GetBuffer().Take(10).SequenceEqual(deltaStream.GetBuffer().Skip(5).Take(10));
        }

        #endregion

        [TestMethod]
        [ExpectedException(typeof (ArgumentException))]
        public void StreamChunkSize_setter_throws_exception_for_negative_chunk_size()
        {
            var streamer = new DeltaStreamer();
            streamer.StreamChunkSize = -1;
        }

        [TestMethod]
        [ExpectedException(typeof (ArgumentException))]
        public void StreamChunkSize_setter_throws_exception_for_zero_chunk_size()
        {
            var streamer = new DeltaStreamer();
            streamer.StreamChunkSize = 0;
        }

        #endregion
    }
}