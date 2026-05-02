using System.Collections.Generic;

namespace PoshMcp;

public sealed record ConfiguredFunctionStatus(
    string FunctionName,
    string ExpectedToolName,
    bool Found,
    List<string> MatchedToolNames,
    string? ResolutionReason = null);