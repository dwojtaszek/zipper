# Zipper Architecture

## Three-Mode Pipeline

```mermaid
graph TD
    CLI["CLI (Program.cs)"]
    CLI --> SelectMode["SelectMode(request)"]
    SelectMode -->|"default"| StandardMode["StandardMode"]
    SelectMode -->|"--loadfile-only"| LoadfileOnlyMode["LoadfileOnlyMode"]
    SelectMode -->|"--production-set"| ProductionSetMode["ProductionSetMode"]

    StandardMode --> PFG["ParallelFileGenerator"]
    PFG -->|"Work Channel"| Producers["N Concurrent Producers"]
    Producers -->|"Result Channel"| ZAS["ZipArchiveService (Consumer)"]
    ZAS --> ZIP["ZIP Archive"]
    ZAS --> LF1["Load Files (DAT/OPT)"]

    LoadfileOnlyMode --> LOG["LoadfileOnlyGenerator"]
    LOG --> LF2["Load Files (DAT/OPT/CSV/XML/Concordance)"]
    LOG -->|"optional"| Chaos["ChaosEngine (Floyd's algorithm)"]
    Chaos --> Audit["_properties.json Audit"]

    ProductionSetMode --> PSG["ProductionSetGenerator"]
    PSG --> PSP["ProductionSetPlanner (no I/O)"]
    PSP --> Tree["Directory Tree (NATIVES/IMAGES/DATA/TEXT)"]
    PSG --> LF3["Load Files + Manifest"]
```

## Component Map

```mermaid
graph LR
    subgraph CLI Layer
        CliParser["CliParser"]
        CliValidator["CliValidator"]
        RequestBuilder["RequestBuilder"]
    end

    subgraph Config
        FGR["FileGenerationRequest"]
        FGR --> Output["Output"]
        FGR --> Metadata["Metadata"]
        FGR --> LoadFile["LoadFile"]
        FGR --> Delimiters["Delimiters"]
        FGR --> Bates["Bates"]
        FGR --> Tiff["Tiff"]
        FGR --> Chaos["Chaos"]
        FGR --> Production["Production"]
    end

    subgraph File Generators
        EML["EmlFileGenerator"]
        TIFF["TiffFileGenerator"]
        Office["OfficeFileGenerator"]
        Placeholder["PlaceholderFileGenerator"]
    end

    subgraph Load File Writers
        DAT["DatWriter"]
        OPT["OptWriter"]
        CSV["CsvWriter"]
        XML["XmlLoadFileWriter"]
        CONC["ConcordanceWriter"]
    end

    subgraph Profiles
        Loader["ColumnProfileLoader"]
        DataGen["DataGenerator"]
        BuiltIns["BuiltInProfiles"]
    end

    CliParser --> CliValidator --> RequestBuilder --> FGR
    FGR --> File Generators
    FGR --> Load File Writers
    Profiles --> DataGen
```
