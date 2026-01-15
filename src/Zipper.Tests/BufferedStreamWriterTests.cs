// <copyright file="BufferedStreamWriterTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Xunit;

namespace Zipper
{
    public class BufferedStreamWriterTests
    {
        [Fact]
        public async Task WriteAsync_ShouldBufferData()
        {
            using var outputStream = new MemoryStream();
            await using var writer = new BufferedStreamWriter(outputStream);

            var testData = new byte[100];
            new Random(42).NextBytes(testData);

            await writer.WriteAsync(testData.AsMemory());
            await writer.FlushAsync();

            Assert.Equal(testData.Length, outputStream.Length);
            Assert.Equal(testData, outputStream.ToArray());
        }

        [Fact]
        public async Task WriteMultipleSmallBuffers_ShouldCombine()
        {
            using var outputStream = new MemoryStream();
            await using var writer = new BufferedStreamWriter(outputStream, bufferSize: 64);

            var data1 = new byte[32];
            var data2 = new byte[32];
            new Random(42).NextBytes(data1);
            new Random(123).NextBytes(data2);

            await writer.WriteAsync(data1.AsMemory());
            await writer.WriteAsync(data2.AsMemory());
            await writer.FlushAsync();

            Assert.Equal(64, outputStream.Length);
        }
    }
}
