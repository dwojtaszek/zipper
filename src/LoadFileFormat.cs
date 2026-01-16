// <copyright file="LoadFileFormat.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Zipper;

/// <summary>
/// Defines the supported load file output formats.
/// </summary>
public enum LoadFileFormat
{
    /// <summary>
    /// Default DAT format with ASCII 20/254/174 delimiters.
    /// </summary>
    Dat,

    /// <summary>
    /// OPT (Opticon) format - comma-separated, page-level image references.
    /// </summary>
    Opt,

    /// <summary>
    /// CSV format - comma-separated values with RFC 4180 escaping.
    /// </summary>
    Csv,

    /// <summary>
    /// XML format - structured markup (legacy, same as EdrmXml).
    /// </summary>
    Xml,

    /// <summary>
    /// EDRM XML format - Electronic Discovery Reference Model schema v1.2.
    /// </summary>
    EdrmXml,

    /// <summary>
    /// CONCORDANCE database format with specific delimiters (legacy).
    /// </summary>
    Concordance,
}
