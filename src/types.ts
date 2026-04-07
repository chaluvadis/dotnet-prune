export interface Finding {
  Project: string;
  Solution?: string;
  FilePath: string;
  FilePathDisplay: string;
  DisplayName: string;
  ProjectFilePath: string;
  Line: number;
  SymbolKind: string;
  ContainingType: string;
  SymbolName: string;
  Accessibility: string;
  Remarks: string;
  confidence?: number;
  severity?: "error" | "warning" | "information" | "hint";
  Icon: string;
  referenceCount?: number;
  references?: FindingReference[];
}

export interface FindingReference {
  filePath: string;
  line: number;
  column?: number;
  type: "direct" | "base" | "delegate" | "possible";
  context?: string;
}

export interface MetricsData {
  totalUnused: number;
  filesAffected: number;
  bySymbolKind: Record<string, number>;
  byProject: Record<string, number>;
  lastAnalyzed?: string;
}

export interface AnalysisConfig {
  includePublicSymbols: boolean;
  includeInternalSymbols: boolean;
  excludeGeneratedCode: boolean;
  mode: "strict" | "loose";
  maxFindings: number;
  minConfidence: number;
  logLevel: "off" | "error" | "warn" | "info" | "debug";
}
