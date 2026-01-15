// <copyright file="ILoadFileWriter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Zipper.LoadFiles;

/// <summary>
/// Interface for load file writers supporting multiple output formats.
/// </summary>
internal interface ILoadFileWriter
{
    /// <summary>
    /// Gets the human-readable format name.
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Gets the file extension for this format (including the dot).
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Writes the load file content to the specified stream.
    /// </summary>
    /// <param name="stream">Output stream.</param>
    /// <param name="request">File generation request parameters.</param>
    /// <param name="processedFiles">List of processed file data.</param>
    /// <returns>Task representing the write operation.</returns>
    Task WriteAsync(Stream stream, FileGenerationRequest request, System.Collections.Generic.List<FileData> processedFiles);
}
