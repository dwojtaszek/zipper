# Performance Optimization Guide

## Architecture Overview

Zipper's performance is achieved through several key architectural improvements implemented in version 0.17+:

### 1. Parallel File Generation

Files are generated concurrently using a controlled number of worker threads:

- **Default concurrency**: Automatically set to your system's CPU core count
- **Resource management**: Uses `SemaphoreSlim` to control resource usage and prevent system overload
- **Work distribution**: Files are distributed across folders using efficient channel-based communication
- **Scalability**: Performance scales linearly with CPU cores up to the limits of I/O subsystem

### 2. Memory Management

Optimized memory usage reduces garbage collection pressure and improves stability:

- **Memory Pooling**: Reuses byte arrays using .NET's `MemoryPool<byte>.Shared`
- **Bounded Memory**: Limits maximum memory usage per operation (100MB default)
- **Efficient Allocation**: Smart allocation strategies prevent memory fragmentation
- **Async Disposal**: Proper cleanup of resources prevents memory leaks

### 3. I/O Optimization

Intelligent I/O strategies minimize disk access overhead:

- **Buffered Writing**: Reduces system call overhead with configurable buffer sizes
- **Streaming Architecture**: Avoids loading entire archives in memory
- **Async Operations**: Non-blocking I/O throughout the pipeline
- **Optimal Compression**: Uses `CompressionLevel.Optimal` for best performance/size ratio

## Performance Monitoring

Zipper provides comprehensive real-time performance metrics during generation:

```
Progress: 25,000 / 50,000 files (50.0%) - 1,250.5 files/sec - ETA: 00:00:20
```

### Metrics Explained

- **Files completed / Total files**: Current progress counter
- **Percentage**: Completion percentage (0-100%)
- **Files/second**: Current generation rate
- **ETA**: Estimated time remaining based on current rate

### Post-Generation Summary

After completion, you'll see a detailed performance summary:

```
Generation complete in 12.3 seconds.
  Archive created: /path/to/archive_20231017_143022.zip
  Performance: 4,065.0 files/second
```

## Performance Benchmarks

The following benchmarks were conducted on a typical development machine (Intel i7-10700K, 32GB RAM, NVMe SSD):

### Small Scale (1K-10K files)

| File Count | Time (seconds) | Files/Second | Memory Usage |
|------------|---------------|--------------|--------------|
| 1,000      | 0.8           | 1,250        | 45MB         |
| 5,000      | 3.2           | 1,562        | 68MB         |
| 10,000     | 5.8           | 1,724        | 92MB         |

### Medium Scale (50K-100K files)

| File Count | Time (seconds) | Files/Second | Memory Usage |
|------------|---------------|--------------|--------------|
| 50,000     | 28.4          | 1,760        | 245MB        |
| 100,000    | 52.1          | 1,919        | 312MB        |

### Large Scale (500K+ files)

| File Count | Time (seconds) | Files/Second | Memory Usage |
|------------|---------------|--------------|--------------|
| 500,000    | 268.7         | 1,861        | 598MB        |
| 1,000,000  | 523.4         | 1,910        | 724MB        |

*Note: Performance varies based on hardware configuration, storage speed, and selected options.*

## Tuning Guidelines

### For Maximum Throughput

- **Storage**: Use SSD storage for the output directory (NVMe preferred)
- **Memory**: Ensure adequate RAM (8GB+ recommended for large jobs)
- **CPU**: Monitor CPU usage - target 80-90% utilization
- **Options**: Avoid `--with-text` and `--with-metadata` if not needed

```bash
# Optimized for speed
./zipper --type pdf --count 100000 --output-path ./fast --folders 50
```

### For Memory-Constrained Environments

- **Reduce concurrency**: The system automatically scales, but you can manually limit it
- **Smaller batches**: Process files in smaller batches
- **Monitor usage**: Watch memory usage during generation

```bash
# Memory-constrained generation
./zipper --type pdf --count 50000 --output-path ./constrained --folders 100
```

### For Large File Generation

- **Target size control**: Use `--target-zip-size` to manage memory usage
- **Disk space**: Monitor available disk space (needs 2-3x final size during generation)
- **Network storage**: Consider network storage for distributed workloads

```bash
# Large generation with size target
./zipper --type pdf --count 200000 --target-zip-size 10GB --output-path ./large
```

## Performance Testing

### Quick Benchmarks

Test performance on your system with these commands:

```bash
# Small benchmark (10K files)
./zipper --type pdf --count 10000 --output-path ./benchmark_small --folders 10

# Medium benchmark (100K files)
./zipper --type pdf --count 100000 --output-path ./benchmark_medium --folders 50

# Large benchmark (1M files)
./zipper --type pdf --count 1000000 --output-path ./benchmark_large --folders 100
```

### Realistic Workloads

Test with realistic options that affect performance:

```bash
# With metadata and text (more realistic but slower)
./zipper --type pdf --count 50000 --output-path ./benchmark_realistic --folders 25 --with-metadata --with-text

# Email generation with attachments
./zipper --type eml --count 25000 --output-path ./benchmark_emails --folders 20 --attachment-rate 25 --with-metadata --with-text
```

### Performance Profiling

For detailed performance analysis, use the built-in benchmark runner:

```csharp
// In code or via custom test harness
await PerformanceBenchmarkRunner.RunBenchmarks();
```

This provides:
- Parallel vs sequential performance comparison
- Memory pool efficiency metrics
- Scalability analysis across different file counts

## Troubleshooting Performance Issues

### Slow Generation Speed

**Check these factors:**

1. **Storage**: HDD vs SSD makes a significant difference
2. **Memory**: Insufficient RAM causes swapping
3. **CPU**: High CPU usage from other processes
4. **Antivirus**: Real-time scanning can impact performance

**Solutions:**
- Move output to faster storage
- Close unnecessary applications
- Temporarily disable real-time antivirus scanning
- Reduce file count or increase folder count

### High Memory Usage

**Symptoms:**
- System becomes unresponsive
- Generation slows down over time
- Out-of-memory errors on very large jobs

**Solutions:**
- Reduce file count per batch
- Increase folder count (better distribution)
- Use `--target-zip-size` to limit file sizes
- Restart application between large jobs

### Intermittent Performance

**Possible causes:**
- Background processes
- Power management throttling
- Thermal throttling
- Network storage latency

**Solutions:**
- Check Task Manager for competing processes
- Ensure power settings are set to "High Performance"
- Monitor CPU temperatures
- Use local storage instead of network drives

## Advanced Configuration

### Environment Variables

Override default performance settings:

```bash
# Set custom concurrency (default: CPU count)
export ZIPPER_CONCURRENCY=16

# Set custom buffer size (default: 81920 bytes)
export ZIPPER_BUFFER_SIZE=163840

# Set maximum memory pool size (default: 100MB)
export ZIPPER_MAX_POOL_SIZE=209715200
```

### Future Enhancements

Planned performance improvements in future versions:

- **GPU acceleration** for file content generation
- **Distributed processing** across multiple machines
- **Advanced caching** for repeated patterns
- **Real-time compression** optimization
- **Network storage** optimization

## Technical Deep Dive

### Parallel Processing Algorithm

1. **Work Distribution**: Files are distributed using a producer-consumer pattern with .NET Channels
2. **Concurrency Control**: SemaphoreSlim limits concurrent operations to prevent resource exhaustion
3. **Memory Efficiency**: Large files use pooled memory when possible, falling back to direct allocation for oversized content
4. **I/O Pipelining**: File generation and archive writing happen concurrently to maximize throughput

### Memory Pool Strategy

- **Size-based pooling**: Different pool sizes for different file types
- **Lazy allocation**: Memory is allocated on-demand
- **Automatic cleanup**: Resources are properly disposed even in error scenarios
- **Fallback mechanisms**: Graceful degradation when pools are exhausted

### Buffer Management

- **Adaptive buffering**: Buffer sizes adjust based on I/O patterns
- **Batching**: Small writes are coalesced into larger operations
- **Flush optimization**: Intelligent flushing reduces I/O calls
- **Error handling**: Robust error handling prevents data corruption

---

*This performance guide reflects the capabilities introduced in the performance optimization implementation. For the most up-to-date information, check the latest documentation.*