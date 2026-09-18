; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------------
JEV001  | JevGen   | Error    | [JevClient] target must be an interface
JEV002  | JevGen   | Error    | Unsupported method return type
JEV003  | JevGen   | Error    | Missing state parameter
JEV004  | JevGen   | Error    | Multiple state parameters
JEV005  | JevGen   | Error    | Unsupported Choice type
JEV006  | JevGen   | Error    | Invalid score definition
JEV007  | JevGen   | Warning  | Missing enum option criteria
JEV008  | JevGen   | Error    | Duplicate question ID
JEV009  | JevGen   | Error    | Unsupported property result type
JEV010  | JevGen   | Error    | CancellationToken duplicated
JEV011  | JevGen   | Warning  | CancellationToken position
JEV012  | JevGen   | Warning  | State type cannot be serialized
JEV013  | JevGen   | Error    | Unsupported generic client
JEV014  | JevGen   | Error    | Question attribute missing
JEV015  | JevGen   | Error    | Duplicate question attributes
JEV016  | JevGen   | Error    | Invalid confidence threshold
JEV017  | JevGen   | Warning  | Provider capability unsupported
JEV018  | JevGen   | Warning  | Interface declares questions but is not a JevGen client
JEV019  | JevGen   | Info     | Method takes no CancellationToken
JEV020  | JevGen   | Info     | No serializer context declared
