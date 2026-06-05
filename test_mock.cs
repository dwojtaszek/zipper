using System;
using System.Buffers;
public class MockMemoryOwner : IMemoryOwner<byte>
{
    public bool IsDisposed { get; private set; }
    public Memory<byte> Memory => new byte[10];
    public void Dispose() { IsDisposed = true; }
}
